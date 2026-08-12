using System.Net.Http.Json;
using Megruli.Shared;

namespace Megruli.App.Services;

/// <summary>Loads and caches the static course content shipped under wwwroot/data.</summary>
public class ContentService
{
    private readonly HttpClient _http;
    private List<CourseUnit>? _units;
    private List<VocabWord>? _vocabulary;
    private List<Phrase>? _phrases;
    private List<Phrase>? _proverbs;
    private List<AudioClip>? _clips;
    private Dictionary<string, VocabWord>? _vocabById;
    private Dictionary<string, Phrase>? _phraseById;
    private Dictionary<string, AudioClip>? _clipById;

    public ContentService(HttpClient http)
    {
        _http = http;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_units is not null) return;

        _units = await _http.GetFromJsonAsync<List<CourseUnit>>("data/units.json", JsonDefaults.Options) ?? new();
        _vocabulary = await _http.GetFromJsonAsync<List<VocabWord>>("data/vocabulary.json", JsonDefaults.Options) ?? new();
        _phrases = await _http.GetFromJsonAsync<List<Phrase>>("data/phrases.json", JsonDefaults.Options) ?? new();
        _proverbs = await _http.GetFromJsonAsync<List<Phrase>>("data/proverbs.json", JsonDefaults.Options) ?? new();
        _clips = await _http.GetFromJsonAsync<List<AudioClip>>("audio/clips/clips-manifest.json", JsonDefaults.Options) ?? new();
        try
        {
            var autoClips = await _http.GetFromJsonAsync<List<AudioClip>>("audio/clips/auto-clips-manifest.json", JsonDefaults.Options) ?? new();
            _clips.AddRange(autoClips);
        }
        catch
        {
            // Auto-generated pronunciations are optional during initial development.
        }

        _vocabById = _vocabulary.ToDictionary(w => w.Id);
        _phraseById = _phrases.Concat(_proverbs).ToDictionary(p => p.Id);
        _clipById = _clips.ToDictionary(c => c.Id);
    }

    public async Task<List<CourseUnit>> GetUnitsAsync()
    {
        await EnsureLoadedAsync();
        return _units!;
    }

    public async Task<CourseUnit?> GetUnitAsync(string unitId)
    {
        await EnsureLoadedAsync();
        return _units!.FirstOrDefault(u => u.Id == unitId);
    }

    public async Task<Lesson?> GetLessonAsync(string lessonId)
    {
        await EnsureLoadedAsync();
        return _units!.SelectMany(u => u.Lessons).FirstOrDefault(l => l.Id == lessonId);
    }

    public async Task<List<VocabWord>> GetAllVocabularyAsync()
    {
        await EnsureLoadedAsync();
        return _vocabulary!;
    }

    public async Task<List<Phrase>> GetAllPhrasesAsync()
    {
        await EnsureLoadedAsync();
        return _phrases!;
    }

    public async Task<List<Phrase>> GetAllProverbsAsync()
    {
        await EnsureLoadedAsync();
        return _proverbs!;
    }

    /// <summary>Resolves a word id from either the vocabulary or phrase/proverb sets.</summary>
    public async Task<(string Megruli, string Georgian, string Category)?> ResolveWordAsync(string wordId)
    {
        await EnsureLoadedAsync();
        if (_vocabById!.TryGetValue(wordId, out var w)) return (w.Megruli, w.Georgian, w.Category);
        if (_phraseById!.TryGetValue(wordId, out var p)) return (p.Megruli, p.Georgian, p.Topic);
        return null;
    }

    public async Task<List<AudioClip>> GetClipsForSourceAsync(string sourceFile)
    {
        await EnsureLoadedAsync();
        return _clips!.Where(c => c.SourceFile == sourceFile).OrderBy(c => c.StartMs).ToList();
    }

    public async Task<AudioClip?> GetClipAsync(string clipId)
    {
        await EnsureLoadedAsync();
        return _clipById!.GetValueOrDefault(clipId);
    }

    public async Task<List<string>> GetDistinctSourceFilesAsync()
    {
        await EnsureLoadedAsync();
        return _clips!.Select(c => c.SourceFile).Distinct().OrderBy(f => f).ToList();
    }
}
