// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
// Audio (lesson recordings + sliced clips) is deliberately NOT in this list: at ~600MB
// total it would make the install/precache step huge. Instead it's cached at runtime,
// the first time each file is actually played — see handleAudioFetch below.
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Separate, unversioned cache for audio so it survives app updates (re-downloading
// ~600MB of lesson audio on every deploy would defeat the point of caching it).
const audioCacheName = 'megruli-audio-cache-v1';

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
    // Activate a successfully cached release immediately. Without this, an installed
    // PWA can keep showing an older lesson UI until every app/browser tab is closed.
    await self.skipWaiting();
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    if ((event.request.method === 'GET' || event.request.method === 'HEAD') && event.request.url.includes('/audio/')) {
        return handleAudioFetch(event.request);
    }

    let cachedResponse = null;
    if (event.request.method === 'GET') {
        // For all navigation requests, try to serve index.html from cache,
        // unless that request is for an offline resource.
        // If you need some URLs to be server-rendered, edit the following check to exclude those URLs
        const shouldServeIndexHtml = event.request.mode === 'navigate'
            && !manifestUrlList.some(url => url === event.request.url);

        const request = shouldServeIndexHtml ? 'index.html' : event.request;
        const cache = await caches.open(cacheName);
        cachedResponse = await cache.match(request);
    }

    return cachedResponse || fetch(event.request);
}

// Cache-on-first-play for lesson/clip audio, with manual Range-request support so
// the <audio> element's normal ranged fetches still work once a file is cached
// (Cache API stores whole responses, so a 206 request is served by slicing the
// cached full body rather than by caching each individual byte range).
async function handleAudioFetch(request) {
    const cache = await caches.open(audioCacheName);
    // Always cache/match against the un-ranged request so we store (and can reuse)
    // one full copy of the file regardless of which byte range was first requested.
    const fullRequest = new Request(request.url, { method: 'GET' });

    let fullResponse = await cache.match(fullRequest);
    if (!fullResponse) {
        try {
            const networkResponse = await fetch(fullRequest);
            if (networkResponse && networkResponse.ok) {
                await cache.put(fullRequest, networkResponse.clone());
                fullResponse = networkResponse;
            } else {
                return networkResponse || new Response('Not found', { status: 404 });
            }
        } catch (err) {
            return new Response('Offline and this clip has not been played before', { status: 503 });
        }
    }

    if (request.method === 'HEAD') {
        const headers = new Headers(fullResponse.headers);
        headers.set('Accept-Ranges', 'bytes');
        return new Response(null, { status: 200, headers });
    }

    const rangeHeader = request.headers.get('range');
    if (!rangeHeader) return fullResponse;

    const blob = await fullResponse.clone().blob();
    const totalLength = blob.size;
    const range = parseSingleByteRange(rangeHeader, totalLength);
    if (!range) {
        return new Response(null, {
            status: 416,
            statusText: 'Range Not Satisfiable',
            headers: {
                'Content-Range': `bytes */${totalLength}`,
                'Accept-Ranges': 'bytes',
            },
        });
    }

    const contentType = fullResponse.headers.get('Content-Type') || 'audio/mpeg';
    const slice = blob.slice(range.start, range.end + 1, contentType);
    const headers = new Headers({
        'Content-Type': contentType,
        'Content-Range': `bytes ${range.start}-${range.end}/${totalLength}`,
        'Content-Length': String(slice.size),
        'Accept-Ranges': 'bytes',
    });
    const etag = fullResponse.headers.get('ETag');
    const lastModified = fullResponse.headers.get('Last-Modified');
    if (etag) headers.set('ETag', etag);
    if (lastModified) headers.set('Last-Modified', lastModified);

    return new Response(slice, {
        status: 206,
        statusText: 'Partial Content',
        headers,
    });
}

// RFC 9110 single byte-range forms:
//   bytes=100-199  explicit range
//   bytes=100-     from offset to the end
//   bytes=-65536   final N bytes (used by some mobile media players)
function parseSingleByteRange(value, totalLength) {
    const match = /^bytes=(\d*)-(\d*)$/i.exec(value.trim());
    if (!match || (!match[1] && !match[2]) || totalLength <= 0) return null;

    if (match[1]) {
        const start = Number(match[1]);
        if (!Number.isSafeInteger(start) || start >= totalLength) return null;
        let end = match[2] ? Number(match[2]) : totalLength - 1;
        if (!Number.isSafeInteger(end) || end < start) return null;
        return { start, end: Math.min(end, totalLength - 1) };
    }

    const suffixLength = Number(match[2]);
    if (!Number.isSafeInteger(suffixLength) || suffixLength <= 0) return null;
    return {
        start: Math.max(totalLength - suffixLength, 0),
        end: totalLength - 1,
    };
}
