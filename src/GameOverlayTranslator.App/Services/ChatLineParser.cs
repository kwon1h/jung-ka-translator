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
    private const int MinSpeakerLength = 2;
    private const int MaxSpeakerLength = 24;

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
        var markers = FindSpeakerMarkers(line);
        if (markers.Count > 0)
        {
            foreach (var chatLine in BuildLines(line, markers))
            {
                yield return chatLine;
            }

            yield break;
        }

        var fallback = ParseFirstSeparator(line);
        if (fallback is not null)
        {
            foreach (var chatLine in SplitEmbeddedSpeakerMarkers(fallback))
            {
                yield return chatLine;
            }
        }
    }

    private static IReadOnlyList<SpeakerMarker> FindSpeakerMarkers(string line, bool allowEmbeddedFirst = false)
    {
        var markers = new List<SpeakerMarker>();
        foreach (Match match in SpeakerMarkerPattern().Matches(line))
        {
            var speakerGroup = match.Groups["speaker"];
            var separatorGroup = match.Groups["separator"];
            var speaker = speakerGroup.Value.Trim();
            if (!IsValidSpeaker(speaker) || !HasFollowingMessage(line, separatorGroup.Index + separatorGroup.Length))
            {
                continue;
            }

            var marker = new SpeakerMarker(speakerGroup.Index, separatorGroup.Index + separatorGroup.Length, speaker);
            if (!CanAcceptMarker(line, markers, marker, allowEmbeddedFirst))
            {
                continue;
            }

            markers.Add(marker);
        }

        return markers;
    }

    private static bool CanAcceptMarker(
        string line,
        IReadOnlyList<SpeakerMarker> acceptedMarkers,
        SpeakerMarker marker,
        bool allowEmbeddedFirst)
    {
        if (acceptedMarkers.Count == 0)
        {
            if (marker.SpeakerStart == 0 || string.IsNullOrWhiteSpace(line[..marker.SpeakerStart]))
            {
                return true;
            }

            return allowEmbeddedFirst
                && !char.IsWhiteSpace(line[marker.SpeakerStart - 1])
                && !string.IsNullOrWhiteSpace(line[..marker.SpeakerStart]);
        }

        var previous = acceptedMarkers[^1];
        return !string.IsNullOrWhiteSpace(line[previous.MessageStart..marker.SpeakerStart])
            && !char.IsWhiteSpace(line[marker.SpeakerStart - 1]);
    }

    private static IEnumerable<ChatLine> BuildLines(string line, IReadOnlyList<SpeakerMarker> markers)
    {
        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index];
            var nextMarkerStart = index + 1 < markers.Count ? markers[index + 1].SpeakerStart : line.Length;
            var message = line[marker.MessageStart..nextMarkerStart].Trim();
            if (!string.IsNullOrWhiteSpace(message))
            {
                yield return new ChatLine(marker.Speaker, message);
            }
        }
    }

    private static IReadOnlyList<ChatLine> SplitEmbeddedSpeakerMarkers(ChatLine line)
    {
        var markers = FindSpeakerMarkers(line.Message, allowEmbeddedFirst: true);
        if (markers.Count == 0)
        {
            return [line];
        }

        var firstMessage = line.Message[..markers[0].SpeakerStart].Trim();
        if (string.IsNullOrWhiteSpace(firstMessage))
        {
            LogUncertainEmbeddedSplit(line);
            return [line];
        }

        var splitLines = new List<ChatLine> { new(line.Speaker, firstMessage) };
        var embeddedLines = BuildLines(line.Message, markers).ToList();
        if (embeddedLines.Count != markers.Count)
        {
            LogUncertainEmbeddedSplit(line);
            return [line];
        }

        splitLines.AddRange(embeddedLines);
        return splitLines;
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

    private static bool IsValidSpeaker(string value) =>
        value.Length is >= MinSpeakerLength and <= MaxSpeakerLength
        && value.Any(character => char.IsLetterOrDigit(character) || IsHan(character));

    private static bool HasFollowingMessage(string line, int messageStart) =>
        messageStart < line.Length && !string.IsNullOrWhiteSpace(line[messageStart..]);

    private static void LogUncertainEmbeddedSplit(ChatLine line) =>
        AppLog.Write($"ChatLineParser kept uncertain embedded speaker marker. Speaker={line.Speaker} MessageLength={line.Message.Length}");

    private static bool IsSpeakerCharacter(char value) =>
        char.IsLetterOrDigit(value) || IsHan(value) || value is '_' or '-' or '.' or '=' or '|' || char.IsWhiteSpace(value);

    private static bool IsHan(char value) => value is >= '\u3400' and <= '\u9FFF';

    private readonly record struct SpeakerMarker(int SpeakerStart, int MessageStart, string Speaker);

    [GeneratedRegex(@"(?<speaker>[A-Za-z0-9_.=\-|\u00C0-\u024F][A-Za-z0-9_.=\-| \p{L}\p{Nd}]{1,23})\s*(?<separator>[:\uFF1A])", RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerMarkerPattern();
}
