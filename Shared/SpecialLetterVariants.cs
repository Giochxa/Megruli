namespace Megruli.Shared;

/// <summary>
/// ჷ and ჸ aren't on a standard Georgian keyboard layout (see the Alphabet page), so typed
/// answers need to accept the nearest substitute letters a learner would realistically type
/// instead. Substitutes are generated only for the exact positions where the *correct* answer
/// has one of these letters — this expands the accepted-answer set rather than fuzzy-matching
/// arbitrary input, so it can't accidentally accept an unrelated word.
/// </summary>
public static class SpecialLetterVariants
{
    // ჷ (schwa) — the Alphabet page notes the Senaki-Martvili dialect uses ო/ი/უ in its place.
    private static readonly char[] SchwaSubstitutes = ['ი', 'უ', 'ო'];
    // ჸ has no equivalent in standard Georgian; ყ is the nearest available letter (and shares
    // its keyboard key — Left Alt+Y for ჸ vs plain Y for ყ on a Georgian layout).
    private static readonly char[] QSubstitutes = ['ყ'];

    public static List<string> Expand(string answer)
    {
        var results = new List<string> { answer };
        ExpandChar(results, 'ჷ', SchwaSubstitutes);
        ExpandChar(results, 'ჸ', QSubstitutes);
        return results.Distinct().ToList();
    }

    private static void ExpandChar(List<string> results, char target, char[] substitutes)
    {
        int count = results.Count;
        for (int i = 0; i < count; i++)
        {
            var s = results[i];
            if (s.IndexOf(target) < 0) continue;
            foreach (var sub in substitutes)
            {
                results.Add(s.Replace(target, sub));
            }
        }
    }
}
