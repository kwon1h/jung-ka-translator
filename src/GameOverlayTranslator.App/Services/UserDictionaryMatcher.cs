using System.Text;
using System.Text.RegularExpressions;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal sealed class UserDictionaryMatcher
{
    private readonly Dictionary<string, UserDictEntry> exactEntries = new(StringComparer.Ordinal);
    private readonly List<Pattern> patterns = [];

    public UserDictionaryMatcher(IEnumerable<UserDictEntry> entries)
    {
        foreach (var entry in entries)
        {
            var normalizedSource = TranslationTextNormalizer.NormalizeForDictionaryMatch(entry.Source);
            if (string.IsNullOrWhiteSpace(normalizedSource))
            {
                continue;
            }

            exactEntries.TryAdd(normalizedSource, entry);
            patterns.Add(new Pattern(entry, normalizedSource, BuildFlexRegex(entry.Source)));
        }
    }

    public bool TryGetExact(string text, out UserDictEntry entry) =>
        exactEntries.TryGetValue(
            TranslationTextNormalizer.NormalizeForDictionaryMatch(text),
            out entry!);

    public string ReplaceSubstrings(string text, out bool replaced)
    {
        replaced = false;
        var result = text;
        var normalizedResult = TranslationTextNormalizer.NormalizeForDictionaryMatch(result);
        if (normalizedResult.Length == 0)
        {
            return result;
        }

        foreach (var pattern in patterns)
        {
            if (!normalizedResult.Contains(pattern.NormalizedSource, StringComparison.Ordinal))
            {
                continue;
            }

            var next = pattern.Regex.Replace(result, pattern.Entry.Target);
            if (string.Equals(next, result, StringComparison.Ordinal))
            {
                continue;
            }

            result = next;
            replaced = true;
            normalizedResult = TranslationTextNormalizer.NormalizeForDictionaryMatch(result);
        }

        return result;
    }

    private static Regex BuildFlexRegex(string source)
    {
        var coreChars = new List<string>();
        foreach (var character in source.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character)
                || TranslationTextNormalizer.IsIgnorablePunctuation(character))
            {
                continue;
            }

            coreChars.Add(Regex.Escape(character.ToString()));
        }

        if (coreChars.Count == 0)
        {
            return new Regex(
                Regex.Escape(source),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        const string noiseClass = @"[\s\p{P}\p{S}]*";
        return new Regex(
            string.Join(noiseClass, coreChars),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed record Pattern(UserDictEntry Entry, string NormalizedSource, Regex Regex);
}
