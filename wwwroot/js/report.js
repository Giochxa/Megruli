window.megruliReport = {
    share: async function (title, text, reportUrl) {
        const data = { title: title, text: text, url: reportUrl };

        if (navigator.share) {
            try {
                await navigator.share(data);
                return 'shared';
            } catch (error) {
                if (error && error.name === 'AbortError') return 'cancelled';
            }
        }

        const fullText = `${text}\n${reportUrl}`;
        try {
            await navigator.clipboard.writeText(fullText);
            return 'copied';
        } catch {
            window.prompt(title, fullText);
            return 'shown';
        }
    }
};
