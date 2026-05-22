using System.Text.RegularExpressions;

namespace GameOverlayTranslator.App.Services;

public sealed record ChatLine(string Speaker, string Message)
{
    public string SourceLine => $"{Speaker}: {Message}";

    public string DeduplicationKey => $"{NormalizeKey(Speaker)}:{NormalizeKey(Message)}";

    private static string NormalizeKey(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}

public static partial class ChatLineParser
{
    private static readonly char[] Separators = [':', '\uFF1A'];

    public static IReadOnlyList<ChatLine> Parse(string ocrText)
    {
        return ocrText
            .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(ParsePhysicalLine)
            .ToList();
    }

    private static IEnumerable<ChatLine> ParsePhysicalLine(string line)
    {
        var userMatches = LatinSpeakerPattern()
            .Matches(line)
            .Where(match => match.Index == 0 || !IsSpeakerCharacter(line[match.Index - 1]))
            .ToList();
        if (userMatches.Count > 0)
        {
            for (var index = 0; index < userMatches.Count; index++)
            {
                var match = userMatches[index];
                var nextMatchStart = index + 1 < userMatches.Count ? userMatches[index + 1].Index : line.Length;
                var speaker = match.Groups["speaker"].Value.Trim();
                var messageStart = match.Index + match.Length;
                var message = line[messageStart..nextMatchStart].Trim();
                if (!string.IsNullOrWhiteSpace(speaker) && !string.IsNullOrWhiteSpace(message))
                {
                    yield return new ChatLine(speaker, message);
                }
            }
            yield break;
        }

        var fallback = ParseFirstSeparator(line);
        if (fallback is not null)
        {
            yield return fallback;
        }
    }

    private static ChatLine? ParseFirstSeparator(string line)
    {
        var separator = line.IndexOfAny(Separators);
        if (separator <= 0 || separator >= line.Length - 1)
        {
            return null;
        }

        var speaker = ExtractSpeaker(line[..separator]);
        var message = line[(separator + 1)..].Trim();
        return string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(message)
            ? null
            : new ChatLine(speaker, message);
    }

    private static string ExtractSpeaker(string prefix)
    {
        var trimmed = prefix.Trim().TrimEnd(']', '\u3011', '\u300B', '>');
        var start = trimmed.Length - 1;
        while (start >= 0 && IsSpeakerCharacter(trimmed[start]))
        {
            start--;
        }

        var speaker = trimmed[(start + 1)..].Trim();
        return string.IsNullOrWhiteSpace(speaker) ? trimmed : speaker;
    }

    private static bool IsSpeakerCharacter(char value) =>
        char.IsLetterOrDigit(value) || IsHan(value) || value is '_' or '-' or '.';

    private static bool IsHan(char value) => value is >= '\u3400' and <= '\u9FFF';

    [GeneratedRegex(@"(?<speaker>[A-Za-z0-9_.-]{2,24})\s*[:\uFF1A]", RegexOptions.CultureInvariant)]
    private static partial Regex LatinSpeakerPattern();
}
