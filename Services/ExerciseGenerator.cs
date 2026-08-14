using Megruli.Shared;
using System.Text.RegularExpressions;

namespace Megruli.App.Services;

/// <summary>
/// Builds a session's worth of exercises on the fly from a lesson's word list, instead
/// of requiring every one of the ~1,000 vocabulary/phrase entries to be hand-authored
/// into exercises. Grammar lessons are the exception — they ship pre-authored
/// (<see cref="Lesson.FixedExercises"/>) since pronoun/verb drills aren't generator-friendly.
/// </summary>
public class ExerciseGenerator
{
    private const int MaxExercisesPerLesson = 20;
    private readonly ContentService _content;
    private readonly AudioClipLabelService _labels;

    public ExerciseGenerator(ContentService content, AudioClipLabelService labels)
    {
        _content = content;
        _labels = labels;
    }

    public async Task<List<ExerciseBase>> GenerateAsync(Lesson lesson)
    {
        if (lesson.Kind == LessonKind.Grammar)
        {
            return GenerateGrammarExercises(lesson);
        }

        var translationLanguage = lesson.TranslationLanguage;
        var words = new List<(string Id, string Megruli, string Georgian, string Category)>();
        foreach (var id in lesson.WordIds)
        {
            var resolved = await _content.ResolveWordAsync(id, translationLanguage);
            if (resolved is { } r) words.Add((id, r.Megruli, r.Georgian, r.Category));
        }
        if (words.Count == 0) return new();

        var rng = Random.Shared;
        var pool = new List<ExerciseBase>();
        int exId = 0;
        string NextId() => $"{lesson.Id}-ex-{++exId}";

        // Distractor pool: prefer other words in this same lesson; top up from the
        // wider vocabulary set of the same category if the lesson is small.
        List<(string Megruli, string Georgian)> distractorPool = words.Select(w => (w.Megruli, w.Georgian)).ToList();
        if (distractorPool.Count < 6)
        {
            var category = words[0].Category;
            var extra = (await _content.GetVocabularyForLanguageAsync(translationLanguage))
                .Where(v => v.Category == category)
                .Select(v => (v.Megruli, v.Georgian));
            distractorPool.AddRange(extra);
        }

        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool promptIsGeorgian = i % 2 == 0;
            string correctAnswer = promptIsGeorgian ? w.Megruli : w.Georgian;
            string prompt = promptIsGeorgian ? w.Georgian : w.Megruli;

            var distractors = distractorPool
                .Where(d => (promptIsGeorgian ? d.Megruli : d.Georgian) != correctAnswer)
                .OrderBy(_ => rng.Next())
                .Take(3)
                .Select(d => promptIsGeorgian ? d.Megruli : d.Georgian)
                .ToList();

            if (distractors.Count >= 2)
            {
                var options = distractors.Append(correctAnswer).OrderBy(_ => rng.Next()).ToList();
                pool.Add(new MultipleChoiceExercise
                {
                    Id = NextId(),
                    WordId = w.Id,
                    Prompt = prompt,
                    PromptIsGeorgian = promptIsGeorgian,
                    Options = options,
                    CorrectIndex = options.IndexOf(correctAnswer),
                    TranslationLanguage = translationLanguage,
                });
            }

            if (i % 3 == 0)
            {
                pool.Add(new TypeAnswerExercise
                {
                    Id = NextId(),
                    WordId = w.Id,
                    Prompt = w.Georgian,
                    PromptIsGeorgian = true,
                    AcceptedAnswers = w.Megruli.Split('/').Select(s => s.Trim()).ToList(),
                    TranslationLanguage = translationLanguage,
                });
            }

            var clipId = await _labels.GetClipIdForWordAsync(w.Id);
            if (clipId is not null)
            {
                var listenDistractors = distractorPool
                    .Where(d => d.Megruli != w.Megruli)
                    .OrderBy(_ => rng.Next())
                    .Take(3)
                    .Select(d => d.Megruli)
                    .ToList();
                if (listenDistractors.Count >= 2)
                {
                    var options = listenDistractors.Append(w.Megruli).OrderBy(_ => rng.Next()).ToList();
                    pool.Add(new ListenChooseExercise
                    {
                        Id = NextId(),
                        WordId = w.Id,
                        AudioClipId = clipId,
                        Options = options,
                        CorrectIndex = options.IndexOf(w.Megruli),
                        TranslationLanguage = translationLanguage,
                    });
                }
            }

            // Multi-word vocabulary entries are also real course sentences/expressions,
            // so they can support the same gap exercise as phrases and proverbs.
            var gap = CreateMissingWordExercise(w, words, NextId(), rng, translationLanguage);
            if (gap is not null) pool.Add(gap);
        }

        foreach (var group in words.Chunk(5))
        {
            if (group.Length < 3) continue;
            pool.Add(new MatchPairsExercise
            {
                Id = NextId(),
                Pairs = group.Select(g => new MatchPair { Megruli = g.Megruli, Georgian = g.Georgian }).ToList(),
                TranslationLanguage = translationLanguage,
            });
        }

        var shuffled = pool.OrderBy(_ => rng.Next()).ToList();

        // Sentence lessons should reliably contain gap exercises even when their overall
        // generated pool is much larger than the 20-exercise session cap.
        var requiredGaps = shuffled.OfType<FillMissingWordExercise>().Take(5).Cast<ExerciseBase>().ToList();
        var remaining = shuffled.Where(e => !requiredGaps.Contains(e))
            .Take(MaxExercisesPerLesson - requiredGaps.Count);
        return requiredGaps.Concat(remaining).OrderBy(_ => rng.Next()).ToList();
    }

    private static List<ExerciseBase> GenerateGrammarExercises(Lesson lesson)
    {
        var rng = Random.Shared;
        var fixedExercises = lesson.FixedExercises?.ToList() ?? new();
        var typeAnswers = fixedExercises.OfType<TypeAnswerExercise>()
            .Where(exercise => exercise.AcceptedAnswers.Count > 0)
            .ToList();
        var tokenPool = typeAnswers
            .SelectMany(exercise => Regex.Matches(CleanSentence(exercise.AcceptedAnswers[0]), @"[\p{L}\p{M}’']+")
                .Select(match => match.Value))
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var gaps = new List<ExerciseBase>();
        int gapIndex = 0;
        foreach (var exercise in typeAnswers.OrderBy(_ => rng.Next()))
        {
            var sentence = CleanSentence(exercise.AcceptedAnswers[0]);
            var tokens = Regex.Matches(sentence, @"[\p{L}\p{M}’']+")
                .Where(match => match.Length >= 2)
                .ToList();
            if (tokens.Count < 2) continue;

            var missing = tokens[rng.Next(tokens.Count)];
            var distractors = tokenPool
                .Where(token => !string.Equals(token, missing.Value, StringComparison.OrdinalIgnoreCase))
                .OrderBy(_ => rng.Next())
                .Take(3)
                .ToList();
            if (distractors.Count < 2) continue;

            var options = distractors.Append(missing.Value).OrderBy(_ => rng.Next()).ToList();
            gaps.Add(new FillMissingWordExercise
            {
                Id = $"{lesson.Id}-gap-{++gapIndex}",
                WordId = exercise.WordId,
                SentenceBefore = sentence[..missing.Index],
                SentenceAfter = sentence[(missing.Index + missing.Length)..],
                GeorgianTranslation = exercise.Prompt,
                Options = options,
                CorrectIndex = options.IndexOf(missing.Value),
                TranslationLanguage = lesson.TranslationLanguage
            });
            if (gaps.Count == 5) break;
        }

        return gaps.Concat(fixedExercises.OrderBy(_ => rng.Next()))
            .Take(MaxExercisesPerLesson)
            .OrderBy(_ => rng.Next())
            .ToList();
    }

    private static FillMissingWordExercise? CreateMissingWordExercise(
        (string Id, string Megruli, string Georgian, string Category) word,
        List<(string Id, string Megruli, string Georgian, string Category)> lessonWords,
        string id,
        Random rng,
        string translationLanguage)
    {
        var sentence = CleanSentence(word.Megruli);
        var tokens = Regex.Matches(sentence, @"[\p{L}\p{M}’']+")
            .Where(m => m.Length >= 2)
            .ToList();
        if (tokens.Count < 2) return null;

        var missing = tokens[rng.Next(tokens.Count)];
        var correct = missing.Value;
        var distractors = lessonWords
            .Where(w => w.Id != word.Id)
            .SelectMany(w => Regex.Matches(CleanSentence(w.Megruli), @"[\p{L}\p{M}’']+").Select(m => m.Value))
            .Where(value => value.Length >= 2 && !string.Equals(value, correct, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => rng.Next())
            .Take(3)
            .ToList();
        if (distractors.Count < 2) return null;

        var options = distractors.Append(correct).OrderBy(_ => rng.Next()).ToList();
        return new FillMissingWordExercise
        {
            Id = id,
            WordId = word.Id,
            SentenceBefore = sentence[..missing.Index],
            SentenceAfter = sentence[(missing.Index + missing.Length)..],
            GeorgianTranslation = word.Georgian,
            Options = options,
            CorrectIndex = options.IndexOf(correct),
            TranslationLanguage = translationLanguage
        };
    }

    private static string CleanSentence(string value)
    {
        var sentence = value.Split('(')[0].Trim();
        return sentence.Split('/')[0].Trim();
    }
}
