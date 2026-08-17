window.megruliReport = {
    share: async function (title, text) {
        const data = { title: title, text: text, url: window.location.href };

        if (navigator.share) {
            try {
                await navigator.share(data);
                return 'shared';
            } catch (error) {
                if (error && error.name === 'AbortError') return 'cancelled';
            }
        }

        const fullText = `${text}\n${window.location.href}`;
        try {
            await navigator.clipboard.writeText(fullText);
            return 'copied';
        } catch {
            window.prompt(title, fullText);
            return 'shown';
        }
    }
};
