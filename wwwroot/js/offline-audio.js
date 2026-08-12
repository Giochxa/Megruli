window.megruliOfflineAudio = (() => {
    const cacheName = 'megruli-audio-cache-v1';

    function absoluteUrl(path) {
        return new URL(path, document.baseURI).href;
    }

    async function openCache() {
        if (!('caches' in window)) throw new Error('Offline storage is not supported by this browser.');
        return await caches.open(cacheName);
    }

    async function getStatus(paths) {
        const cache = await openCache();
        const downloaded = [];
        for (const path of paths) {
            if (await cache.match(absoluteUrl(path))) downloaded.push(path);
        }

        let usage = null;
        let quota = null;
        let persisted = false;
        if (navigator.storage) {
            if (navigator.storage.estimate) {
                const estimate = await navigator.storage.estimate();
                usage = estimate.usage ?? null;
                quota = estimate.quota ?? null;
            }
            if (navigator.storage.persisted) persisted = await navigator.storage.persisted();
        }

        return { downloaded, usage, quota, persisted, online: navigator.onLine };
    }

    async function download(path) {
        const cache = await openCache();
        const url = absoluteUrl(path);
        if (await cache.match(url)) return;

        const response = await fetch(url, { cache: 'no-store' });
        if (!response.ok) throw new Error(`Download failed (${response.status}).`);
        await cache.put(url, response.clone());
    }

    async function remove(path) {
        const cache = await openCache();
        await cache.delete(absoluteUrl(path));
    }

    async function clear() {
        await caches.delete(cacheName);
        await caches.open(cacheName);
    }

    async function requestPersistence() {
        if (!navigator.storage?.persist) return false;
        return await navigator.storage.persist();
    }

    return { getStatus, download, remove, clear, requestPersistence };
})();
