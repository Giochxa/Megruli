using System.Text.Json.Serialization;

namespace Megruli.Shared;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MultipleChoiceExercise), "multipleChoice")]
[JsonDerivedType(typeof(TypeAnswerExercise), "typeAnswer")]
[JsonDerivedType(typeof(MatchPairsExercise), "matchPairs")]
[JsonDerivedType(typeof(ListenChooseExercise), "listenChoose")]
[JsonDerivedType(typeof(FillMissingWordExercise), "fillMissingWord")]
public abstract class ExerciseBase
{
    public string Id { get; set; } = "";
    /// <summary>Optional related vocabulary/phrase id, used to record mastery progress.</summary>
    public string? WordId { get; set; }
    public string TranslationLanguage { get; set; } = "ka";
}

public class MultipleChoiceExercise : ExerciseBase
{
    public string Prompt { get; set; } = "";
    public bool PromptIsGeorgian { get; set; }
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
}

public class TypeAnswerExercise : ExerciseBase
{
    public string Prompt { get; set; } = "";
    public bool PromptIsGeorgian { get; set; }
    public List<string> AcceptedAnswers { get; set; } = new();
    /// <summary>SharedResource key for the instruction line above the prompt; defaults to "Exercise_TypeMegruli" when null.</summary>
    public string? InstructionKey { get; set; }
}

public class MatchPairsExercise : ExerciseBase
{
    public List<MatchPair> Pairs { get; set; } = new();
}

public class MatchPair
{
    public string Megruli { get; set; } = "";
    public string Georgian { get; set; } = "";
}

public class ListenChooseExercise : ExerciseBase
{
    public string AudioClipId { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
}

/// <summary>A Megruli sentence with one word replaced by a blank and a small word bank.</summary>
public class FillMissingWordExercise : ExerciseBase
{
    public string SentenceBefore { get; set; } = "";
    public string SentenceAfter { get; set; } = "";
    public string GeorgianTranslation { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int CorrectIndex { get; set; }
}
