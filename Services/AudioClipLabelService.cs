using System.Text.Json;
using Megruli.Shared;
using Microsoft.JSInterop;

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
    private Dictionary<string, AudioClipLabel>? _labels;

    public AudioClipLabelService(IJSRuntime js)
    {
        _js = js;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_labels is not null) return;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            var list = string.IsNullOrWhiteSpace(json)
                ? new List<AudioClipLabel>()
                : JsonSerializer.Deserialize<List<AudioClipLabel>>(json, JsonDefaults.Options) ?? new();
            _labels = list.ToDictionary(l => l.ClipId);
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

    /// <summary>The first labeled, non-skipped clip linked to a given vocabulary/phrase word id, if any.</summary>
    public async Task<string?> GetClipIdForWordAsync(string wordId)
    {
        await EnsureLoadedAsync();
        return _labels!.Values.FirstOrDefault(l => l.LinkedWordId == wordId && !l.Skipped)?.ClipId;
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
            .FirstOrDefault(l => !l.Skipped && string.Equals(l.Megruli?.Trim(), target, StringComparison.Ordinal))
            ?.ClipId;
    }

    public async Task<HashSet<string>> GetLabeledWordIdsAsync()
    {
        await EnsureLoadedAsync();
        return _labels!.Values.Where(l => l.LinkedWordId is not null && !l.Skipped)
            .Select(l => l.LinkedWordId!).ToHashSet();
    }
}
