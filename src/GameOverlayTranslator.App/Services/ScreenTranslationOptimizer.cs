using System.Text;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal sealed class ScreenTranslationMemory
{
    private const int MaxEntries = 1024;
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, Entry> exactEntries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> orderedEntries = new();

    public bool TryGet(string canonicalText, out string translatedText)
    {
        translatedText = string.Empty;
        if (string.IsNullOrWhiteSpace(canonicalText))
        {
            return false;
        }

        Prune();
        if (exactEntries.TryGetValue(canonicalText, out var exact))
        {
            exact.LastSeen = DateTimeOffset.UtcNow;
            translatedText = exact.TranslatedText;
            return true;
        }

        var bestScore = 0d;
        Entry? best = null;
        foreach (var entry in orderedEntries)
        {
            if (!IsFuzzyEligible(canonicalText, entry.CanonicalText))
            {
                continue;
            }

            var score = TranslationTextNormalizer.CalculateCanonicalSimilarity(canonicalText, entry.CanonicalText);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        if (best is null)
        {
            return false;
        }

        var threshold = Math.Min(canonicalText.Length, best.CanonicalText.Length) < 12 ? 0.94 : 0.86;
        if (bestScore < threshold)
        {
            return false;
        }

        best.LastSeen = DateTimeOffset.UtcNow;
        translatedText = best.TranslatedText;
        return true;
    }

    public void Remember(string canonicalText, string sourceText, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(canonicalText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return;
        }

        Prune();
        if (exactEntries.TryGetValue(canonicalText, out var existing))
        {
            existing.SourceText = sourceText;
            existing.TranslatedText = translatedText;
            existing.LastSeen = DateTimeOffset.UtcNow;
            return;
        }

        var entry = new Entry(canonicalText, sourceText, translatedText, DateTimeOffset.UtcNow);
        exactEntries[canonicalText] = entry;
        orderedEntries.AddFirst(entry);

        while (orderedEntries.Count > MaxEntries)
        {
            var last = orderedEntries.Last;
            if (last is null)
            {
                break;
            }

            exactEntries.Remove(last.Value.CanonicalText);
            orderedEntries.RemoveLast();
        }
    }

    private static bool IsFuzzyEligible(string left, string right)
    {
        var minLength = Math.Min(left.Length, right.Length);
        if (minLength < 4)
        {
            return false;
        }

        var ratio = left.Length / (double)Math.Max(1, right.Length);
        return ratio is >= 0.75 and <= 1.33;
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        for (var node = orderedEntries.Last; node is not null;)
        {
            var previous = node.Previous;
            if (now - node.Value.LastSeen > EntryTtl)
            {
                exactEntries.Remove(node.Value.CanonicalText);
                orderedEntries.Remove(node);
            }
            node = previous;
        }
    }

    private sealed class Entry(string canonicalText, string sourceText, string translatedText, DateTimeOffset lastSeen)
    {
        public string CanonicalText { get; } = canonicalText;
        public string SourceText { get; set; } = sourceText;
        public string TranslatedText { get; set; } = translatedText;
        public DateTimeOffset LastSeen { get; set; } = lastSeen;
    }
}

public static class ScreenTranslationSegmenter
{
    public static IReadOnlyList<ScreenTextSegment> Split(string text, OcrLanguage language)
    {
        var normalized = TranslationTextNormalizer.NormalizeForTranslation(text, language);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<ScreenTextSegment>();
        }

        var chunks = SplitByHardBreaks(normalized)
            .SelectMany(chunk => chunk.Length > 120 ? SplitBySoftBreaks(chunk) : [chunk])
            .SelectMany(chunk => UsesUnspacedScript(language)
                ? chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [chunk])
            .Select(chunk => TranslationTextNormalizer.NormalizeForTranslation(chunk, language))
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToList();

        if (chunks.Count == 0)
        {
            chunks.Add(normalized);
        }

        return chunks
            .Select(chunk => new ScreenTextSegment(chunk, TranslationTextNormalizer.CanonicalizeCacheText(chunk)))
            .Where(segment => segment.CanonicalText.Length > 0)
            .ToList();
    }

    public static bool ShouldSendToTranslation(ScreenTextSegment segment, OcrLanguage language)
    {
        if (segment.Text.Length > 300 || segment.CanonicalText.Length > 300)
        {
            return false;
        }

        if (!TranslationTextNormalizer.HasExpectedSourceScript(segment.Text, language))
        {
            return false;
        }

        return !TranslationTextNormalizer.IsMostlyNumericOrSymbolic(segment.Text, language);
    }

    private static bool UsesUnspacedScript(OcrLanguage language) =>
        language.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        || language.Tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitByHardBreaks(string text)
    {
        return SplitKeepingBreaks(text, IsHardBreak);
    }

    private static IEnumerable<string> SplitBySoftBreaks(string text)
    {
        return SplitKeepingBreaks(text, IsSoftBreak);
    }

    private static IEnumerable<string> SplitKeepingBreaks(string text, Func<char, bool> isBreak)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character);
            if (isBreak(character))
            {
                var chunk = builder.ToString().Trim();
                if (chunk.Length > 0)
                {
                    yield return chunk;
                }
                builder.Clear();
            }
        }

        var tail = builder.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private static bool IsHardBreak(char character) =>
        character is '.' or '!' or '?' or ';' or ':' or '\u3002' or '\uFF01' or '\uFF1F' or '\uFF1B' or '\uFF1A';

    private static bool IsSoftBreak(char character) =>
        character is ',' or '\uFF0C' or '\u3001';
}

public sealed record ScreenTextSegment(string Text, string CanonicalText);
