window.megruliAudio = {
    play: function (el) {
        if (!el) return;
        try {
            el.currentTime = 0;
            el.play();
        } catch (e) {
            console.warn('audio play failed', e);
        }
    },
    playSegment: function (el, startMs, endMs) {
        if (!el) return;
        const start = Math.max(0, startMs / 1000);
        const end = Math.max(start, endMs / 1000);
        if (el._megruliTimeUpdate) el.removeEventListener('timeupdate', el._megruliTimeUpdate);
        el._megruliTimeUpdate = function () {
            if (el.currentTime >= end) {
                el.pause();
                el.currentTime = start;
            }
        };
        el.addEventListener('timeupdate', el._megruliTimeUpdate);
        const startPlayback = function () {
            el.currentTime = start;
            const promise = el.play();
            if (promise) promise.catch(e => console.warn('audio segment play failed', e));
        };
        if (el.readyState >= 1) startPlayback();
        else el.addEventListener('loadedmetadata', startPlayback, { once: true });
    },
    playSegmentById: function (id, startMs, endMs) {
        const el = document.getElementById(id);
        if (!el) {
            console.warn('audio element not found', id);
            return;
        }
        window.megruliAudio.playSegment(el, startMs, endMs);
    }
};
