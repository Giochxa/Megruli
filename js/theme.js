window.megruliTheme = {
    apply: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute('content', theme === 'dark' ? '#131f24' : '#58cc02');
    }
};
