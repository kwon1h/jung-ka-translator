using System.Globalization;
using System.Text;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class RecentChatFilter
{
    private readonly List<Entry> entries = [];

    internal RecentChatFilterState CaptureState() =>
        new(entries.Select(entry => new RecentChatFilterStateEntry(
            entry.Id,
            entry.Line,
            new HashSet<string>(entry.Tokens, StringComparer.Ordinal),
            entry.Score,
            entry.LastSeen)).ToList());

    internal void RestoreState(RecentChatFilterState state)
    {
        entries.Clear();
        entries.AddRange(state.Entries.Select(entry => new Entry(
            entry.Id,
            entry.Line,
            new HashSet<string>(entry.Tokens, StringComparer.Ordinal),
            entry.Score,
            entry.LastSeen)));
    }

    public ChatFilterDecision Evaluate(ChatLine line, FilterSettings filter)
    {
        var now = DateTimeOffset.UtcNow;
        entries.RemoveAll(entry => now - entry.LastSeen > TimeSpan.FromSeconds(filter.SimilarityCacheSeconds));

        var candidate = new Entry(Guid.NewGuid().ToString("N"), line, Tokenize(line.Message), Score(line), now);

        if (!filter.EnableSimilarityFilter)
        {
            entries.Add(candidate);
            return ChatFilterDecision.Translate(candidate.Id);
        }

        var match = entries
            .Where(entry => SameSpeaker(entry.Line.Speaker, line.Speaker))
            .Select(entry =>
            {
                var similarity = Similarity(entry.Tokens, candidate.Tokens);
                return new { Entry = entry, similarity.Score, similarity.Overlap, similarity.Union };
            })
            .OrderByDescending(match => match.Score)
            .FirstOrDefault();

        if (match is null || match.Score < filter.SimilarityThreshold)
        {
            entries.Add(candidate);
            LogDecision("translate", candidate, match?.Entry, match?.Score, match?.Overlap, match?.Union);
            return ChatFilterDecision.Translate(candidate.Id);
        }

        match.Entry.LastSeen = now;
        if (candidate.Score > match.Entry.Score + 2 && match.Score >= filter.ReplacementSimilarityThreshold)
        {
            match.Entry.Line = line;
            match.Entry.Tokens = candidate.Tokens;
            match.Entry.Score = candidate.Score;
            LogDecision("replace", candidate, match.Entry, match.Score, match.Overlap, match.Union);
            return ChatFilterDecision.Replace(match.Entry.Id, match.Score);
        }

        LogDecision("skip", candidate, match.Entry, match.Score, match.Overlap, match.Union);
        return ChatFilterDecision.Skip(match.Entry.Id, match.Score);
    }

    private static bool SameSpeaker(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static SimilarityResult Similarity(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return new SimilarityResult(0, 0, left.Count + right.Count);
        }

        var overlap = left.Intersect(right).Count();
        var union = left.Union(right).Count();
        return new SimilarityResult(overlap / (double)union, overlap, union);
    }

    private static int Score(ChatLine line) =>
        line.Message.Count(char.IsLetterOrDigit) * 2
        - line.Message.Count(character => character is '?' or '\uFFFD')
        - Math.Abs(line.Message.Length - line.Message.Trim().Length);

    private static HashSet<string> Tokenize(string value)
    {
        var normalized = Normalize(value);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            return words.ToHashSet(StringComparer.Ordinal);
        }

        var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        var grams = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < compact.Length - 1; index++)
        {
            grams.Add(compact.Substring(index, 2));
        }

        if (grams.Count == 0 && compact.Length > 0)
        {
            grams.Add(compact);
        }
        return grams;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            builder.Append(char.IsLetterOrDigit(character) || IsCjk(category) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsCjk(UnicodeCategory category) =>
        category is UnicodeCategory.OtherLetter;

    private static void LogDecision(string action, Entry candidate, Entry? match, double? similarity, int? overlap, int? union)
    {
        AppLog.Write(
            $"RecentChatFilter action={action} candidateSpeaker={Quoted(candidate.Line.Speaker)} candidateMessage={Quoted(candidate.Line.Message)} " +
            $"candidateTokens={candidate.Tokens.Count} candidateScore={candidate.Score} " +
            $"matchSpeaker={Quoted(match?.Line.Speaker)} matchMessage={Quoted(match?.Line.Message)} matchTokens={match?.Tokens.Count ?? 0} " +
            $"matchScore={match?.Score} similarity={similarity:0.000} overlap={overlap} union={union}");
    }

    private static string Quoted(string? value) => $"\"{value?.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private sealed class Entry(string id, ChatLine line, HashSet<string> tokens, int score, DateTimeOffset lastSeen)
    {
        public string Id { get; } = id;
        public ChatLine Line { get; set; } = line;
        public HashSet<string> Tokens { get; set; } = tokens;
        public int Score { get; set; } = score;
        public DateTimeOffset LastSeen { get; set; } = lastSeen;
    }

    private sealed record SimilarityResult(double Score, int Overlap, int Union);
}

internal sealed record RecentChatFilterState(IReadOnlyList<RecentChatFilterStateEntry> Entries);

internal sealed record RecentChatFilterStateEntry(
    string Id,
    ChatLine Line,
    HashSet<string> Tokens,
    int Score,
    DateTimeOffset LastSeen);

public sealed record ChatFilterDecision(string Id, ChatFilterAction Action, double SimilarityScore = 0)
{
    public static ChatFilterDecision Translate(string id) => new(id, ChatFilterAction.Translate, 0);
    public static ChatFilterDecision Replace(string id, double score) => new(id, ChatFilterAction.Replace, score);
    public static ChatFilterDecision Skip(string id, double score) => new(id, ChatFilterAction.Skip, score);
}

public enum ChatFilterAction
{
    Translate,
    Replace,
    Skip
}
