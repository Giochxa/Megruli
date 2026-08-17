using System.Text.RegularExpressions;

namespace Megruli.Shared;

/// <summary>Expands authored dictionary notation into exact answers a learner may type.</summary>
public static partial class AnswerVariants
{
    private static readonly IReadOnlyDictionary<string, string[]> EquivalentAnswers =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Both forms are used for Megrelian "spring" and must be accepted even
            // when an exercise was authored with only one of them.
            ["გაზარხული"] = ["გაზაფხული"],
            ["გაზაფხული"] = ["გაზარხული"],
        };

    private static readonly string[] NoteMarkers =
    [
        "დიალექტ", "ფორმა", "ფორმები", "ძვ.", "აღმ.", "დას.", "იხ.", "მრ."
    ];

    public static IReadOnlyList<string> Expand(string answer)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var optionalExpansion in ExpandAttachedParentheses(answer))
        {
            var withoutParentheticalNotes = ParentheticalGroupRegex().Replace(optionalExpansion, " ");
            foreach (var alternative in SplitAlternatives(withoutParentheticalNotes))
                results.Add(alternative);

            // Parentheses separated by whitespace normally contain a full alternative
            // (ზოთონჯი (ბზოთონჯი)) or a note (აღმ. დიალექტი).
            foreach (Match match in ParentheticalGroupRegex().Matches(optionalExpansion))
            {
                var content = match.Groups[1].Value.Trim();
                if (content.StartsWith("ან ", StringComparison.OrdinalIgnoreCase))
                    content = content[3..];

                foreach (var alternative in SplitAlternatives(content))
                {
                    if (IsUsableParentheticalAlternative(alternative)) results.Add(alternative);
                }
            }
        }

        foreach (var value in results.ToList())
        {
            if (!EquivalentAnswers.TryGetValue(value, out var equivalents)) continue;
            foreach (var equivalent in equivalents) results.Add(equivalent);
        }

        return results.Where(value => value.Length > 0).ToList();
    }

    private static IEnumerable<string> ExpandAttachedParentheses(string value)
    {
        var match = AttachedParentheticalRegex().Match(value);
        if (!match.Success)
        {
            yield return value;
            yield break;
        }

        // Attached parentheses denote optional letters/suffixes: გეშვი(თ), მე(ვ)ულჷ.
        foreach (var expansion in ExpandAttachedParentheses(
                     value.Remove(match.Index, match.Length)))
            yield return expansion;
        foreach (var expansion in ExpandAttachedParentheses(
                     value.Remove(match.Index, match.Length).Insert(match.Index, match.Groups[1].Value)))
            yield return expansion;
    }

    private static IEnumerable<string> SplitAlternatives(string value) =>
        AlternativeSeparatorRegex().Split(value)
            .Select(Clean)
            .Where(item => item.Length > 0);

    private static string Clean(string value) =>
        WhitespaceRegex().Replace(value, " ").Trim().Trim('?', '!', '.', ',', ';', ':', '—', '-');

    private static bool IsUsableParentheticalAlternative(string value)
    {
        if (value.Count(char.IsLetter) < 3) return false;
        return !NoteMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?<=\S)\(([^()]*)\)")]
    private static partial Regex AttachedParentheticalRegex();

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex ParentheticalGroupRegex();

    [GeneratedRegex(@"\s*(?:/|,|;|!)+\s*")]
    private static partial Regex AlternativeSeparatorRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
