using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Megruli.App;
using Megruli.App.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddLocalization();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<ProgressService>();
builder.Services.AddScoped<AudioClipLabelService>();
builder.Services.AddScoped<ExerciseGenerator>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<LocalizationService>();

var host = builder.Build();

// Satellite resource assemblies are picked per-culture at startup, so the saved
// language preference has to be applied before any component renders — a toggle
// later on (LocalizationService.SetCultureAsync) works by saving + reloading the page.
var js = host.Services.GetRequiredService<IJSRuntime>();
var storedCulture = await js.InvokeAsync<string?>("localStorage.getItem", "megruli.culture");
var cultureName = storedCulture is "en" or "ka" ? storedCulture : "en";
if (storedCulture is not null && storedCulture != cultureName)
{
    await js.InvokeVoidAsync("localStorage.setItem", "megruli.culture", cultureName);
}
var culture = new CultureInfo(cultureName);
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await host.RunAsync();
