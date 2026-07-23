using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal static class LanguageCatalog
{
    public static IReadOnlyList<OcrModelPackage> OcrModelPackages { get; } =
    [
        new("zh-Hans", "중국어(간체)"),
        new("zh-Hant", "중국어(번체)"),
        new("en", "영어"),
        new("ja", "일본어"),
        new("ko", "한국어"),
        new("latin", "유럽·라틴 문자 언어 10개"),
        new("cyrillic", "러시아어·키릴 문자 언어 3개"),
        new("ar", "아랍 문자 언어 3개"),
        new("hi", "힌디어·데바나가리 문자 3개"),
        new("ta", "타밀어"),
        new("te", "텔루구어"),
        new("kn", "칸나다어")
    ];

    public static IReadOnlyList<OcrLanguage> OcrLanguages { get; } =
    [
        new("zh-Hans", "중국어(간체)"),
        new("zh-Hant", "중국어(번체)"),
        new("en", "영어"),
        new("ja", "일본어"),
        new("ko", "한국어"),
        new("de", "독일어"),
        new("fr", "프랑스어"),
        new("es", "스페인어"),
        new("pt", "포르투갈어"),
        new("it", "이탈리아어"),
        new("nl", "네덜란드어"),
        new("pl", "폴란드어"),
        new("tr", "튀르키예어"),
        new("vi", "베트남어"),
        new("id", "인도네시아어"),
        new("ru", "러시아어"),
        new("uk", "우크라이나어"),
        new("bg", "불가리아어"),
        new("ar", "아랍어"),
        new("fa", "페르시아어"),
        new("ur", "우르두어"),
        new("hi", "힌디어"),
        new("mr", "마라티어"),
        new("ne", "네팔어"),
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

    public static bool DictionaryEntryMatches(
        UserDictEntry entry,
        OcrLanguage sourceLanguage,
        TranslationLanguage targetLanguage) =>
        string.Equals(entry.SourceLanguage, sourceLanguage.Tag, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.TargetLanguage, targetLanguage.Code, StringComparison.OrdinalIgnoreCase);
}

internal sealed record OcrModelPackage(string Key, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record OcrModelInstallOption(OcrModelPackage Package, bool IsInstalled)
{
    public string Key => Package.Key;
    public string DisplayName => Package.DisplayName;

    public override string ToString() =>
        $"{DisplayName}  ·  {(IsInstalled ? "설치됨" : "다운로드 가능")}";
}
