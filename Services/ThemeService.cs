using Microsoft.JSInterop;

namespace Megruli.App.Services;

public enum AppTheme { Light, Dark }

/// <summary>Dark/light mode, persisted in localStorage. index.html applies the saved value
/// synchronously before Blazor boots so there's no flash of the wrong theme.</summary>
public class ThemeService
{
    private const string StorageKey = "megruli.theme";
    private readonly IJSRuntime _js;
    private AppTheme _theme = AppTheme.Light;
    private bool _loaded;

    public event Action? OnChange;

    public ThemeService(IJSRuntime js)
    {
        _js = js;
    }

    public AppTheme Theme => _theme;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        var stored = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        _theme = stored == "dark" ? AppTheme.Dark : AppTheme.Light;
        await ApplyAsync();
    }

    public async Task ToggleAsync()
    {
        _theme = _theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, _theme == AppTheme.Dark ? "dark" : "light");
        await ApplyAsync();
        OnChange?.Invoke();
    }

    private async Task ApplyAsync()
    {
        await _js.InvokeVoidAsync("megruliTheme.apply", _theme == AppTheme.Dark ? "dark" : "light");
    }
}
