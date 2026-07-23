using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public static class ChatQualityFilter
{
    public static ChatQualityDecision Check(ChatLine line, OcrLanguage language, FilterSettings filter)
    {
        if (!HasPlausibleSpeaker(line.Speaker))
        {
            return ChatQualityDecision.Reject("유저명 품질 낮음", "SpeakerValidation");
        }

        if (filter.EnableLengthFilter)
        {
            if (line.Message.Length < filter.MinMessageLength || line.Message.Length > filter.MaxMessageLength)
            {
                return ChatQualityDecision.Reject($"메시지 길이 비정상 (글자 수: {line.Message.Length})", "LengthFilter");
            }
        }

        if (filter.EnableSeparatorFilter)
        {
            int colonCount = line.Message.Count(character => character is ':' or '\uFF1A');
            if (colonCount > filter.MaxSeparatorsCount && line.Message.Length > 28)
            {
                return ChatQualityDecision.Reject($"여러 채팅 조각 혼합 (구분자 수: {colonCount})", "SeparatorFilter");
            }
        }

        if (filter.EnableNoiseFilter)
        {
            if (HasFragmentedLatinNoise(line.Message, filter.MaxNoiseTokenCount))
            {
                return ChatQualityDecision.Reject("OCR 조각 노이즈", "NoiseFilter");
            }
        }

        if (!TranslationTextNormalizer.HasExpectedSourceScript(line.Message, language))
        {
            return ChatQualityDecision.ShowSource("원문 표시", "ScriptFilter");
        }

        return ChatQualityDecision.Translate();
    }

    private static bool HasPlausibleSpeaker(string speaker)
    {
        var compact = new string(speaker.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (compact.Length is < 2 or > 24)
        {
            return false;
        }

        return compact.Count(character => char.IsLetterOrDigit(character) || IsHan(character)) >= 2;
    }

    private static bool HasFragmentedLatinNoise(string message, int maxNoiseCount)
    {
        var fragments = message
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Length == 1 && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'));
        return fragments >= maxNoiseCount;
    }

    private static bool IsHan(char character) => character is >= '\u3400' and <= '\u9FFF';
}

public sealed record ChatQualityDecision(ChatQualityAction Action, string? Reason, string? Rule)
{
    public bool Accepted => Action is not ChatQualityAction.Reject;
    public bool TranslateWithService => Action is ChatQualityAction.Translate;

    public static ChatQualityDecision Translate() => new(ChatQualityAction.Translate, null, null);
    public static ChatQualityDecision ShowSource(string reason, string rule) => new(ChatQualityAction.ShowSource, reason, rule);
    public static ChatQualityDecision Reject(string reason, string rule) => new(ChatQualityAction.Reject, reason, rule);
}

public enum ChatQualityAction
{
    Translate,
    ShowSource,
    Reject
}
