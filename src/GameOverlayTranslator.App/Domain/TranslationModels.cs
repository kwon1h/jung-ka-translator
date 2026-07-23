using System.Windows;
using System.Windows.Media.Imaging;

namespace GameOverlayTranslator.App.Domain;

public sealed record CapturableWindow(nint Handle, string Title, string ProcessName)
{
    public override string ToString() => $"{Title} ({ProcessName})";
}

public sealed record CaptureTarget(CapturableWindow Window);

public readonly record struct CaptureRegion(double X, double Y, double Width, double Height)
{
    public static CaptureRegion FromPixels(Rect region, Size windowSize) =>
        new(region.X / windowSize.Width, region.Y / windowSize.Height, region.Width / windowSize.Width, region.Height / windowSize.Height);

    public Int32Rect ToPixels(int width, int height)
    {
        var left = Math.Clamp((int)Math.Round(X * width), 0, Math.Max(0, width - 1));
        var top = Math.Clamp((int)Math.Round(Y * height), 0, Math.Max(0, height - 1));
        var regionWidth = Math.Clamp((int)Math.Round(Width * width), 1, Math.Max(1, width - left));
        var regionHeight = Math.Clamp((int)Math.Round(Height * height), 1, Math.Max(1, height - top));
        return new Int32Rect(left, top, regionWidth, regionHeight);
    }
}

public sealed record CapturedFrame(BitmapSource Bitmap)
{
    public IReadOnlyList<Rect> IncludedOcrRects { get; init; } = Array.Empty<Rect>();
    public IReadOnlyList<Rect> ExcludedOcrRects { get; init; } = Array.Empty<Rect>();
}

public sealed record OcrLanguage(string Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record TranslationLanguage(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public enum TranslationMode
{
    Chat,
    Screen
}

public sealed record OcrWordResult(string Text, Rect BoundingRect);

public sealed record OcrLineResult(string Text, Rect BoundingRect);

public sealed record OcrResult(string Text, IReadOnlyList<OcrLineResult> Lines)
{
    public IReadOnlyList<OcrWordResult> Words { get; init; } = Array.Empty<OcrWordResult>();
}

public sealed record TranslationRequest(string Text, string TargetLanguage, string? SourceLanguage = null);

public sealed record TranslationUsage(
    int OutboundRequestCount = 0,
    int OutboundCharacterCount = 0,
    int CacheHitCount = 0,
    int SkippedCount = 0)
{
    public static readonly TranslationUsage None = new();

    public static TranslationUsage Outbound(int requestCount, int characterCount) =>
        new(Math.Max(0, requestCount), Math.Max(0, characterCount));

    public TranslationUsage Add(TranslationUsage? other) =>
        other is null
            ? this
            : new TranslationUsage(
                OutboundRequestCount + other.OutboundRequestCount,
                OutboundCharacterCount + other.OutboundCharacterCount,
                CacheHitCount + other.CacheHitCount,
                SkippedCount + other.SkippedCount);
}

public sealed record TranslationResult(string SourceText, string TranslatedText, string? DetectedSourceLanguage, TranslationUsage? Usage = null);

public sealed record BatchTranslationRequest(IReadOnlyList<string> Texts, string TargetLanguage, string? SourceLanguage = null);

public sealed record BatchTranslationResult(IReadOnlyList<string> TranslatedTexts, TranslationUsage? Usage = null);

public enum DiagnosticKind
{
    Other,
    OcrTranslated,
    OcrSkipped
}

public sealed record FilterSettings(
    bool EnableLengthFilter = true,
    int MinMessageLength = 2,
    int MaxMessageLength = 72,
    bool EnableNoiseFilter = true,
    int MaxNoiseTokenCount = 4,
    bool EnableSeparatorFilter = true,
    int MaxSeparatorsCount = 0,
    bool EnableSimilarityFilter = true,
    double SimilarityThreshold = 0.72,
    double ReplacementSimilarityThreshold = 0.82,
    int SimilarityCacheSeconds = 12
);

public sealed record UserDictEntry(
    string Source,
    string Target,
    string Category = "사용자",
    string SourceLanguage = "zh-Hans",
    string TargetLanguage = "ko")
{
    public string LanguagePair => $"{SourceLanguage} → {TargetLanguage}";
}

public sealed record ScreenTranslationItem(string SourceText, string TranslatedText, Rect BoundingRect);

public sealed record ChatTranslationItem(
    string Id,
    string SourceText,
    string TranslatedText,
    string Speaker,
    Rect? BoundingRect);

public sealed record SessionOptions(
    CaptureTarget Target,
    CaptureRegion Region,
    OcrLanguage OcrLanguage,
    TranslationLanguage TargetLanguage,
    TimeSpan Interval,
    FilterSettings Filter,
    IReadOnlyList<UserDictEntry> UserDictionary,
    TranslationMode Mode = TranslationMode.Chat,
    IReadOnlyList<CaptureRegion>? ExcludedRegions = null,
    IReadOnlyList<CaptureRegion>? IncludedRegions = null,
    bool SuppressEnglishOnlyScreenLines = false);

public sealed record SessionUpdate(
    string Status,
    string? SourceText = null,
    string? TranslatedText = null,
    bool IsError = false,
    string? Speaker = null,
    bool IsChatLine = false,
    string? ChatLineId = null,
    bool ReplacesChatLine = false,
    string? OcrRawText = null,
    string? FilterReason = null,
    string? FilterRule = null,
    IReadOnlyList<ScreenTranslationItem>? ScreenItems = null,
    int TranslationRequestCount = 0,
    int TranslationCharacterCount = 0,
    int TotalTranslationRequestCount = 0,
    int TotalTranslationCharacterCount = 0,
    string? DiagnosticSourceText = null,
    DiagnosticKind DiagnosticKind = DiagnosticKind.Other,
    Rect? BoundingRect = null,
    IReadOnlyList<ChatTranslationItem>? ChatItems = null);
