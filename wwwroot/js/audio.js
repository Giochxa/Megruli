window.megruliAudio = {
    play: function (el) {
        if (!el) return;
        try {
            el.currentTime = 0;
            el.play();
        } catch (e) {
            console.warn('audio play failed', e);
        }
    }
};
