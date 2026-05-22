using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public static class ChatQualityFilter
{
    public static ChatQualityDecision Check(ChatLine line, OcrLanguage language)
    {
        if (!HasPlausibleSpeaker(line.Speaker))
        {
            return ChatQualityDecision.Reject("유저명 품질 낮음");
        }

        if (line.Message.Length is < 2 or > 72)
        {
            return ChatQualityDecision.Reject("메시지 길이 비정상");
        }

        if (line.Message.Count(character => character is ':' or '\uFF1A') > 0 && line.Message.Length > 28)
        {
            return ChatQualityDecision.Reject("여러 채팅 조각 혼합");
        }

        if (HasFragmentedLatinNoise(line.Message))
        {
            return ChatQualityDecision.Reject("OCR 조각 노이즈");
        }

        if (!HasExpectedSourceScript(line.Message, language))
        {
            return ChatQualityDecision.ShowSource("원문 표시");
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

    private static bool HasFragmentedLatinNoise(string message)
    {
        var fragments = message
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Length == 1 && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'));
        return fragments >= 4;
    }

    private static bool HasExpectedSourceScript(string message, OcrLanguage language)
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

    private static bool IsHan(char character) => character is >= '\u3400' and <= '\u9FFF';
}

public sealed record ChatQualityDecision(ChatQualityAction Action, string? Reason)
{
    public bool Accepted => Action is not ChatQualityAction.Reject;
    public bool TranslateWithService => Action is ChatQualityAction.Translate;

    public static ChatQualityDecision Translate() => new(ChatQualityAction.Translate, null);
    public static ChatQualityDecision ShowSource(string reason) => new(ChatQualityAction.ShowSource, reason);
    public static ChatQualityDecision Reject(string reason) => new(ChatQualityAction.Reject, reason);
}

public enum ChatQualityAction
{
    Translate,
    ShowSource,
    Reject
}
