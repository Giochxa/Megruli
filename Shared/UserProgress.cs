namespace Megruli.Shared;

public class UserProgress
{
    public int Xp { get; set; }
    public int Streak { get; set; }
    public DateOnly? LastActiveDate { get; set; }
    public int Hearts { get; set; } = 5;
    public bool UnlimitedHearts { get; set; }
    public HashSet<string> CompletedLessonIds { get; set; } = new();
    public Dictionary<string, LessonResult> LessonResults { get; set; } = new();
    public Dictionary<string, WordMastery> Mastery { get; set; } = new();
}

public class LessonResult
{
    public int CorrectAnswers { get; set; }
    public int TotalAnswers { get; set; }
    public DateTime CompletedAt { get; set; }
    public bool IsPerfect => TotalAnswers <= 0 || CorrectAnswers >= TotalAnswers;
}

public class WordMastery
{
    /// <summary>Simple leitner box, 0 (new) to 5 (mastered).</summary>
    public int Box { get; set; }
    public DateTime LastReviewed { get; set; }
    public int TimesCorrect { get; set; }
    public int TimesWrong { get; set; }
}
