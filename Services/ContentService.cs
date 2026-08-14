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
    private Dictionary<string, string> _englishTranslations = new();
    private List<CourseUnit>? _englishUnits;

    public ContentService(HttpClient http)
    {
        _http = http;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_units is not null) return;

        _units = await _http.GetFromJsonAsync<List<CourseUnit>>("data/units.json", JsonDefaults.Options) ?? new();
        _vocabulary = await _http.GetFromJsonAsync<List<VocabWord>>("data/vocabulary.json", JsonDefaults.Options) ?? new();
        var sourceVocabulary = new List<VocabWord>();
        try
        {
            sourceVocabulary = await _http.GetFromJsonAsync<List<VocabWord>>(
                "data/source-vocabulary.json", JsonDefaults.Options) ?? new();
            var existingIds = _vocabulary.Select(word => word.Id).ToHashSet(StringComparer.Ordinal);
            _vocabulary.AddRange(sourceVocabulary.Where(word => existingIds.Add(word.Id)));
            AddSourceVocabularyUnit(_units, sourceVocabulary);
        }
        catch
        {
            // Keep an older installed PWA usable while its new source data is still caching.
        }
        _phrases = await _http.GetFromJsonAsync<List<Phrase>>("data/phrases.json", JsonDefaults.Options) ?? new();
        _proverbs = await _http.GetFromJsonAsync<List<Phrase>>("data/proverbs.json", JsonDefaults.Options) ?? new();
        try
        {
            _englishTranslations = await _http.GetFromJsonAsync<Dictionary<string, string>>(
                "data/english-translations.json", JsonDefaults.Options) ?? new();
        }
        catch
        {
            // An installed PWA can briefly run a newer app shell with an older asset cache.
            // English content is optional in that state; keep the Georgian course usable.
            _englishTranslations = new();
        }
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

    private static void AddSourceVocabularyUnit(List<CourseUnit> units, List<VocabWord> words)
    {
        if (words.Count == 0 || units.Any(unit => unit.Id == "source-vocabulary")) return;

        const int wordsPerLesson = 18;
        var categoryNames = new Dictionary<string, (string English, string Georgian)>
        {
            ["adjectives"] = ("Adjectives", "ზედსართავები"),
            ["animals"] = ("Animals and birds", "ცხოველები და ფრინველები"),
            ["body-parts"] = ("Body parts", "სხეულის ნაწილები"),
            ["family"] = ("Family", "ოჯახი"),
            ["household-extra"] = ("Household", "საოჯახო ნივთები"),
            ["numbers-cardinal"] = ("Cardinal numbers", "რაოდენობითი რიცხვები"),
            ["numbers-fractions"] = ("Fractions", "წილობითი რიცხვები"),
            ["numbers-ordinal"] = ("Ordinal numbers", "რიგობითი რიცხვები"),
            ["plants-extra"] = ("Plants and trees", "მცენარეები და ხეები"),
            ["pronouns"] = ("Pronouns", "ნაცვალსახელები"),
            ["time-extra"] = ("Time", "დრო"),
            ["weather"] = ("Nature and weather", "ბუნება და ამინდი")
        };

        var lessons = new List<Lesson>();
        foreach (var categoryGroup in words.GroupBy(word => word.Category).OrderBy(group => group.Key))
        {
            var names = categoryNames.TryGetValue(categoryGroup.Key, out var localizedNames)
                ? localizedNames
                : (English: categoryGroup.Key, Georgian: categoryGroup.Key);
            var categoryWords = categoryGroup.ToList();
            var lessonNumber = 0;
            for (var offset = 0; offset < categoryWords.Count; offset += wordsPerLesson)
            {
                lessonNumber++;
                var suffix = categoryWords.Count > wordsPerLesson ? $" {lessonNumber}" : "";
                lessons.Add(new Lesson
                {
                    Id = $"source-{categoryGroup.Key}-{lessonNumber}",
                    Title = $"{names.English}{suffix} / {names.Georgian}{suffix}",
                    Kind = LessonKind.Vocabulary,
                    WordIds = categoryWords.Skip(offset).Take(wordsPerLesson).Select(word => word.Id).ToList(),
                    IntroduceWordsBeforeExercises = true
                });
            }
        }

        units.Add(new CourseUnit
        {
            Id = "source-vocabulary",
            Title = "Expanded source vocabulary",
            TitleGeorgian = "წყაროებიდან დამატებული ლექსიკა",
            Icon = "📚",
            Lessons = lessons
        });
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

    public async Task<List<CourseUnit>> GetEnglishUnitsAsync()
    {
        await EnsureLoadedAsync();
        if (_englishUnits is not null) return _englishUnits;

        _englishUnits = _units!
            .Select(unit => new CourseUnit
            {
                Id = $"{unit.Id}-en",
                Title = unit.Title,
                TitleGeorgian = unit.TitleGeorgian,
                Icon = unit.Icon,
                Lessons = unit.Lessons
                    .Where(lesson => lesson.Kind != LessonKind.Listening)
                    .Select(lesson => new Lesson
                    {
                        Id = $"{lesson.Id}-en",
                        Title = lesson.Title,
                        Kind = lesson.Kind == LessonKind.Grammar ? LessonKind.Vocabulary : lesson.Kind,
                        WordIds = lesson.WordIds.Where(_englishTranslations.ContainsKey).ToList(),
                        IntroduceWordsBeforeExercises = true,
                        TranslationLanguage = "en"
                    })
                    .Where(lesson => lesson.WordIds.Count >= 3)
                    .ToList()
            })
            .Where(unit => unit.Lessons.Count > 0)
            .ToList();
        return _englishUnits;
    }

    public async Task<Lesson?> GetLessonAsync(string lessonId)
    {
        await EnsureLoadedAsync();
        var lesson = _units!.SelectMany(u => u.Lessons).FirstOrDefault(l => l.Id == lessonId);
        if (lesson is not null) return lesson;
        return (await GetEnglishUnitsAsync()).SelectMany(u => u.Lessons).FirstOrDefault(l => l.Id == lessonId);
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
    public async Task<(string Megruli, string Georgian, string Category)?> ResolveWordAsync(
        string wordId, string translationLanguage = "ka")
    {
        await EnsureLoadedAsync();
        var useEnglish = translationLanguage == "en";
        if (_vocabById!.TryGetValue(wordId, out var w))
        {
            var translation = useEnglish && _englishTranslations.TryGetValue(wordId, out var english)
                ? english : w.Georgian;
            var megruli = useEnglish && wordId.StartsWith("numbers-cardinal-", StringComparison.Ordinal)
                ? w.Georgian
                : w.Megruli;
            return (useEnglish ? MegrelianTransliterator.ToLatin(megruli) : megruli,
                translation, w.Category);
        }
        if (_phraseById!.TryGetValue(wordId, out var p))
        {
            var translation = useEnglish && _englishTranslations.TryGetValue(wordId, out var english)
                ? english : p.Georgian;
            return (useEnglish ? MegrelianTransliterator.ToLatin(p.Megruli) : p.Megruli,
                translation, p.Topic);
        }
        return null;
    }

    public async Task<List<(string Megruli, string Georgian, string Category)>> GetVocabularyForLanguageAsync(
        string translationLanguage)
    {
        await EnsureLoadedAsync();
        var result = new List<(string, string, string)>();
        foreach (var word in _vocabulary!)
        {
            if (translationLanguage == "en" && !_englishTranslations.ContainsKey(word.Id)) continue;
            var resolved = await ResolveWordAsync(word.Id, translationLanguage);
            if (resolved is { } item) result.Add(item);
        }
        return result;
    }

    public async Task<List<(string Id, string Megruli, string Translation, string Category)>> GetEnglishDictionaryAsync()
    {
        await EnsureLoadedAsync();
        var entries = new List<(string, string, string, string)>();
        foreach (var word in _vocabulary!)
        {
            if (!_englishTranslations.TryGetValue(word.Id, out var english)) continue;
            entries.Add((word.Id, MegrelianTransliterator.ToLatin(word.Megruli), english, word.Category));
        }
        foreach (var phrase in _phrases!.Concat(_proverbs!))
        {
            if (!_englishTranslations.TryGetValue(phrase.Id, out var english)) continue;
            entries.Add((phrase.Id, MegrelianTransliterator.ToLatin(phrase.Megruli), english, phrase.Topic));
        }
        return entries;
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
