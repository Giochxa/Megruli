using Megruli.Shared;

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
            return lesson.FixedExercises?.ToList() ?? new();
        }

        var words = new List<(string Id, string Megruli, string Georgian, string Category)>();
        foreach (var id in lesson.WordIds)
        {
            var resolved = await _content.ResolveWordAsync(id);
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
            var extra = (await _content.GetAllVocabularyAsync())
                .Where(v => v.Category == category && !words.Any(w => w.Id == v.Id))
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
                    });
                }
            }
        }

        foreach (var group in words.Chunk(5))
        {
            if (group.Length < 3) continue;
            pool.Add(new MatchPairsExercise
            {
                Id = NextId(),
                Pairs = group.Select(g => new MatchPair { Megruli = g.Megruli, Georgian = g.Georgian }).ToList(),
            });
        }

        var shuffled = pool.OrderBy(_ => rng.Next()).ToList();
        return shuffled.Take(MaxExercisesPerLesson).ToList();
    }
}
