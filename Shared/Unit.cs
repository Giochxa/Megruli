namespace Megruli.Shared;

public class CourseUnit
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string TitleGeorgian { get; set; } = "";
    public string Icon { get; set; } = "🔤";
    public List<Lesson> Lessons { get; set; } = new();
}

public enum LessonKind
{
    Vocabulary,
    Grammar,
    Phrases,
    Listening,
    Proverbs
}

public class Lesson
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public LessonKind Kind { get; set; } = LessonKind.Vocabulary;

    /// <summary>Ids into vocabulary.json / phrases.json that this lesson draws exercises from.</summary>
    public List<string> WordIds { get; set; } = new();

    /// <summary>For Grammar lessons: pre-authored exercises shipped as-is (not generated).</summary>
    public List<ExerciseBase>? FixedExercises { get; set; }

    /// <summary>For Listening lessons: the source lesson audio file, e.g. "Megruli1.mp3".</summary>
    public string? LessonAudioFile { get; set; }

    /// <summary>Whether this lesson must present its WordIds before any exercises.</summary>
    public bool IntroduceWordsBeforeExercises { get; set; }
}
