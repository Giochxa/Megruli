using System.Text;

namespace Megruli.Shared;

/// <summary>Reader-friendly Latin transliteration for Megrelian Mkhedruli text.</summary>
public static class MegrelianTransliterator
{
    private static readonly IReadOnlyDictionary<char, string> Latin = new Dictionary<char, string>
    {
        ['ა'] = "a", ['ბ'] = "b", ['გ'] = "g", ['დ'] = "d", ['ე'] = "e", ['ვ'] = "v",
        ['ზ'] = "z", ['თ'] = "t", ['ი'] = "i", ['კ'] = "k'", ['ლ'] = "l", ['მ'] = "m",
        ['ნ'] = "n", ['ჲ'] = "y", ['ო'] = "o", ['პ'] = "p'", ['ჟ'] = "zh", ['რ'] = "r",
        ['ს'] = "s", ['ტ'] = "t'", ['უ'] = "u", ['ჷ'] = "ə", ['ფ'] = "p", ['ქ'] = "k",
        ['ღ'] = "gh", ['ყ'] = "q'", ['ჸ'] = "’", ['შ'] = "sh", ['ჩ'] = "ch", ['ც'] = "ts",
        ['ძ'] = "dz", ['წ'] = "ts'", ['ჭ'] = "ch'", ['ხ'] = "kh", ['ჯ'] = "j", ['ჰ'] = "h"
    };

    public static string ToLatin(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var result = new StringBuilder(value.Length * 2);
        foreach (var character in value)
        {
            result.Append(Latin.TryGetValue(character, out var latin) ? latin : character);
        }
        return result.ToString();
    }
}
