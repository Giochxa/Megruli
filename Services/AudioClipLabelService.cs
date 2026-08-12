using System.Text.Json;
using Megruli.Shared;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace Megruli.App.Services;

/// <summary>
/// Stores user-entered labels for auto-sliced audio clips in localStorage, merged at
/// runtime over the shipped (unlabeled) clips-manifest.json. This is what turns a raw
/// silence-detected clip into real, linkable pronunciation audio for a vocabulary word —
/// no rebuild needed, and no backend required.
/// </summary>
public class AudioClipLabelService
{
    private const string StorageKey = "megruli.cliplabels";
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private Dictionary<string, AudioClipLabel>? _labels;

    public AudioClipLabelService(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_labels is not null) return;
        try
        {
            List<AudioClipLabel> shipped;
            try
            {
                shipped = await _http.GetFromJsonAsync<List<AudioClipLabel>>(
                    "audio/clips/auto-labels.json", JsonDefaults.Options) ?? new();
            }
            catch
            {
                shipped = new();
            }

            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            var local = string.IsNullOrWhiteSpace(json)
                ? new List<AudioClipLabel>()
                : JsonSerializer.Deserialize<List<AudioClipLabel>>(json, JsonDefaults.Options) ?? new();
            // Labels created before language classification existed were entered through
            // the "Megruli text" workflow, so preserve them as usable Megruli clips.
            foreach (var label in local.Where(l => l.Language == AudioClipLanguage.Unknown &&
                         (!string.IsNullOrWhiteSpace(l.Megruli) || l.LinkedWordId is not null)))
            {
                label.Language = AudioClipLanguage.Megruli;
            }
            _labels = shipped.ToDictionary(l => l.ClipId);
            foreach (var label in local) _labels[label.ClipId] = label;
        }
        catch
        {
            _labels = new();
        }
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(_labels!.Values.ToList(), JsonDefaults.Options);
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public async Task<Dictionary<string, AudioClipLabel>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return _labels!;
    }

    public async Task SaveLabelAsync(AudioClipLabel label)
    {
        await EnsureLoadedAsync();
        _labels![label.ClipId] = label;
        await PersistAsync();
    }

    public async Task<AudioClipLabel?> GetLabelAsync(string clipId)
    {
        await EnsureLoadedAsync();
        return _labels!.GetValueOrDefault(clipId);
    }

    public static bool IsUsableMegruli(AudioClipLabel label) =>
        !label.Skipped && label.Language == AudioClipLanguage.Megruli;

    /// <summary>The first labeled, non-skipped clip linked to a given vocabulary/phrase word id, if any.</summary>
    public async Task<string?> GetClipIdForWordAsync(string wordId)
    {
        await EnsureLoadedAsync();
        return _labels!.Values.FirstOrDefault(l => l.LinkedWordId == wordId && IsUsableMegruli(l))?.ClipId;
    }

    /// <summary>
    /// Falls back to matching a clip by its labeled Megruli text rather than a word id — used
    /// where the caller only has raw text (e.g. a match-pairs chip or a multiple-choice
    /// distractor), not the vocabulary entry's id.
    /// </summary>
    public async Task<string?> GetClipIdForMegruliTextAsync(string megruli)
    {
        await EnsureLoadedAsync();
        var target = megruli.Trim();
        return _labels!.Values
            .FirstOrDefault(l => IsUsableMegruli(l) && string.Equals(l.Megruli?.Trim(), target, StringComparison.Ordinal))
            ?.ClipId;
    }

    public async Task<HashSet<string>> GetLabeledWordIdsAsync()
    {
        await EnsureLoadedAsync();
        return _labels!.Values.Where(l => l.LinkedWordId is not null && IsUsableMegruli(l))
            .Select(l => l.LinkedWordId!).ToHashSet();
    }
}
