using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Megruli.App.Services;

/// <summary>
/// Interface-language switch (Georgian/English). Blazor WebAssembly loads a culture's
/// satellite resource assembly once at startup (see Program.cs), so changing culture at
/// runtime saves the preference and reloads the page rather than trying to hot-swap it.
/// </summary>
public class LocalizationService
{
    private const string StorageKey = "megruli.culture";
    private readonly IJSRuntime _js;
    private readonly NavigationManager _nav;

    public LocalizationService(IJSRuntime js, NavigationManager nav)
    {
        _js = js;
        _nav = nav;
    }

    public string CurrentCulture => CultureInfo.CurrentUICulture.Name;

    public bool IsGeorgian => CurrentCulture.StartsWith("ka", StringComparison.OrdinalIgnoreCase);

    public async Task SetCultureAsync(string cultureName)
    {
        if (string.Equals(CurrentCulture, cultureName, StringComparison.OrdinalIgnoreCase)) return;
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, cultureName);
        _nav.NavigateTo(_nav.Uri, forceLoad: true);
    }
}
