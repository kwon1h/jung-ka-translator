using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal static class LanguageCatalog
{
    public static IReadOnlyList<OcrLanguage> OcrLanguages { get; } =
    [
        new("zh-Hans", "중국어(간체)"),
        new("en", "영어"),
        new("ja", "일본어"),
        new("ko", "한국어"),
        new("ar", "아랍어"),
        new("hi", "힌디어"),
        new("ta", "타밀어"),
        new("te", "텔루구어"),
        new("kn", "칸나다어")
    ];

    public static IReadOnlyList<TranslationLanguage> TargetLanguages { get; } =
    [
        new("ko", "한국어"),
        new("en-US", "영어(미국)"),
        new("zh-Hans", "중국어(간체)"),
        new("zh-Hant", "중국어(번체)"),
        new("ja", "일본어"),
        new("de", "독일어"),
        new("fr", "프랑스어"),
        new("es", "스페인어"),
        new("pt-BR", "포르투갈어(브라질)"),
        new("it", "이탈리아어"),
        new("ru", "러시아어"),
        new("ar", "아랍어"),
        new("vi", "베트남어"),
        new("th", "태국어"),
        new("id", "인도네시아어"),
        new("tr", "튀르키예어")
    ];

    public static bool UsesChineseKoreanDictionary(
        OcrLanguage sourceLanguage,
        TranslationLanguage targetLanguage) =>
        sourceLanguage.Tag.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
        && targetLanguage.Code.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
}
