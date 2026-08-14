using Microsoft.JSInterop;

namespace Megruli.App.Services;

/// <summary>Persists which learning track the user selected independently of the UI language.</summary>
public class CourseLanguageService
{
    private const string StorageKey = "megruli.courseLanguage";
    private readonly IJSRuntime _js;
    private readonly LocalizationService _localization;
    private bool _loaded;

    public CourseLanguageService(IJSRuntime js, LocalizationService localization)
    {
        _js = js;
        _localization = localization;
    }

    public string Current { get; private set; } = "ka";

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        Current = stored is "ka" or "en" ? stored : (_localization.IsGeorgian ? "ka" : "en");
        _loaded = true;
    }

    public async Task SetAsync(string language)
    {
        if (language is not ("ka" or "en")) return;
        Current = language;
        _loaded = true;
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, language);
    }
}
