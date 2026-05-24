using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal static partial class TranslationTextNormalizer
{
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HanZeroNoiseRegex = new(@"(?<=[\u3400-\u9fff])[0oO](?=[\u3400-\u9fff])", RegexOptions.Compiled);
    private static readonly Regex LatinZeroRegex = new(@"(?<=[A-Za-z])0(?=[A-Za-z])", RegexOptions.Compiled);

    public static string NormalizeForTranslation(string text, OcrLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text;
        if (language.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(result, @"(?<=[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef])\s+", "");
            result = Regex.Replace(result, @"\s+(?=[\u4e00-\u9fff\u3000-\u303f\uff00-\uffef])", "");
        }

        return MultipleSpacesRegex.Replace(result, " ").Trim();
    }

    public static string CanonicalizeCacheText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        normalized = LatinZeroRegex.Replace(normalized, "o");
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || IsIgnorablePunctuation(character))
            {
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (char.IsLetterOrDigit(character) || IsCjk(category))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return HanZeroNoiseRegex.Replace(builder.ToString(), "");
    }

    public static bool HasExpectedSourceScript(string message, OcrLanguage language)
    {
        if (language.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return message.Any(IsHan);
        }

        if (language.Tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return message.Any(character => IsHan(character) || character is >= '\u3040' and <= '\u30FF');
        }

        return true;
    }

    public static double CalculateCanonicalSimilarity(string leftCanonical, string rightCanonical)
    {
        var leftTokens = TokenizeCanonical(leftCanonical);
        var rightTokens = TokenizeCanonical(rightCanonical);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var overlap = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union == 0 ? 0 : overlap / (double)union;
    }

    public static bool IsMostlyNumericOrSymbolic(string text)
    {
        var meaningful = 0;
        var sourceLetters = 0;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (char.IsLetterOrDigit(character) || IsHan(character) || character is >= '\u3040' and <= '\u30FF')
            {
                meaningful++;
            }

            if (IsHan(character) || character is >= '\u3040' and <= '\u30FF')
            {
                sourceLetters++;
            }
        }

        return meaningful == 0 || sourceLetters < 2 || sourceLetters / (double)Math.Max(1, meaningful) < 0.25;
    }

    private static HashSet<string> TokenizeCanonical(string canonical)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (canonical.Length == 0)
        {
            return tokens;
        }

        if (canonical.Length == 1)
        {
            tokens.Add(canonical);
            return tokens;
        }

        for (var index = 0; index < canonical.Length - 1; index++)
        {
            tokens.Add(canonical.Substring(index, 2));
        }

        return tokens;
    }

    private static bool IsCjk(UnicodeCategory category) =>
        category is UnicodeCategory.OtherLetter;

    private static bool IsHan(char character) => character is >= '\u3400' and <= '\u9FFF';

    private static bool IsIgnorablePunctuation(char character)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        return category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }
}
