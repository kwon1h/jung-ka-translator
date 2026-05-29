using System.IO;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public enum TranslationServiceType
{
    DeepL,
    GoogleUnofficial,
    GoogleWebApp
}

public enum OcrEngineType
{
    Windows,
    PaddleOCR
}

public enum TranslationDisplayMode
{
    Window,
    TransparentOverlay
}

public sealed record AppSettings(
    string? LastWindowTitle = null,
    string? LastWindowProcessName = null,
    CaptureRegion? LastRegion = null,
    CaptureRegion? LastChatRegion = null,
    CaptureRegion? LastScreenRegion = null,
    CaptureRegion? LastExcludedRegion = null,
    CaptureRegion? LastScreenExcludedRegion = null,
    TranslationDisplayMode DisplayMode = TranslationDisplayMode.Window,
    string FontFamily = AppSettingsDefaults.PreferredFontFamily,
    double FontSize = AppSettingsDefaults.DefaultFontSize,
    string TextColor = "#FFFFFF",
    string OutlineColor = "#000000",
    double StrokeThickness = AppSettingsDefaults.DefaultStrokeThickness,
    double OverlayOpacity = 0.92,
    string OverlayPreset = "기본",
    string OverlayBackgroundColor = "#99000000",
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
    int SimilarityCacheSeconds = 12,
    string OcrLanguageTag = "zh-Hans",
    string TargetLanguageCode = "ko",
    TranslationMode TranslationMode = TranslationMode.Chat,
    TranslationServiceType TranslatorType = TranslationServiceType.DeepL,
    OcrEngineType OcrEngineType = OcrEngineType.Windows,
    string GoogleWebAppUrl = "",
    bool ShowOverlayInScreenShare = false,
    int CaptureGeometryVersion = 2)
{
    public FilterSettings ToFilterSettings() => new(
        EnableLengthFilter,
        MinMessageLength,
        MaxMessageLength,
        EnableNoiseFilter,
        MaxNoiseTokenCount,
        EnableSeparatorFilter,
        MaxSeparatorsCount,
        EnableSimilarityFilter,
        SimilarityThreshold,
        ReplacementSimilarityThreshold,
        SimilarityCacheSeconds);
}

public static class AppSettingsDefaults
{
    public const string PreferredFontFamily = "넥슨 카트 고딕 Kor Bold";
    public const string LegacyFontFamily = "Malgun Gothic";
    public const double DefaultFontSize = 25;
    public const double DefaultStrokeThickness = 0.5;
    public const double MaxStrokeThickness = 1.0;
}

public sealed class AppSettingsStore
{
    private const int CurrentCaptureGeometryVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var hasGeometryVersion = HasJsonProperty(json, nameof(AppSettings.CaptureGeometryVersion));
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    var legacyGeometry = !hasGeometryVersion || settings.CaptureGeometryVersion < CurrentCaptureGeometryVersion;
                    var restoredRegion = legacyGeometry ? null : settings.LastRegion;
                    var restoredChatRegion = legacyGeometry ? null : settings.LastChatRegion ?? settings.LastRegion;
                    var restoredScreenRegion = legacyGeometry ? null : settings.LastScreenRegion;
                    var restoredExcludedRegion = legacyGeometry ? null : settings.LastExcludedRegion ?? settings.LastScreenExcludedRegion;
                    // Normalize settings to ensure default values are used for missing/invalid properties
                    return settings with
                    {
                        LastRegion = restoredRegion,
                        LastChatRegion = restoredChatRegion,
                        LastScreenRegion = restoredScreenRegion,
                        LastExcludedRegion = restoredExcludedRegion,
                        LastScreenExcludedRegion = null,
                        FontFamily = string.IsNullOrWhiteSpace(settings.FontFamily) ? AppSettingsDefaults.PreferredFontFamily : settings.FontFamily,
                        FontSize = settings.FontSize < 12 || settings.FontSize > 48 ? AppSettingsDefaults.DefaultFontSize : settings.FontSize,
                        TextColor = string.IsNullOrWhiteSpace(settings.TextColor) ? "#FFFFFF" : settings.TextColor,
                        OutlineColor = string.IsNullOrWhiteSpace(settings.OutlineColor) ? "#000000" : settings.OutlineColor,
                        StrokeThickness = settings.StrokeThickness < 0 || settings.StrokeThickness > AppSettingsDefaults.MaxStrokeThickness ? AppSettingsDefaults.DefaultStrokeThickness : settings.StrokeThickness,
                        OverlayOpacity = settings.OverlayOpacity < 0.25 || settings.OverlayOpacity > 1 ? 0.92 : settings.OverlayOpacity,
                        OverlayPreset = string.IsNullOrWhiteSpace(settings.OverlayPreset) ? "기본" : settings.OverlayPreset,
                        OverlayBackgroundColor = string.IsNullOrWhiteSpace(settings.OverlayBackgroundColor) ? "#99000000" : settings.OverlayBackgroundColor,
                        MinMessageLength = settings.MinMessageLength < 1 ? 2 : settings.MinMessageLength,
                        MaxMessageLength = settings.MaxMessageLength < 1 ? 72 : settings.MaxMessageLength,
                        MaxNoiseTokenCount = settings.MaxNoiseTokenCount < 1 ? 4 : settings.MaxNoiseTokenCount,
                        MaxSeparatorsCount = settings.MaxSeparatorsCount < 0 ? 0 : settings.MaxSeparatorsCount,
                        SimilarityThreshold = settings.SimilarityThreshold < 0 || settings.SimilarityThreshold > 1 ? 0.72 : settings.SimilarityThreshold,
                        ReplacementSimilarityThreshold = settings.ReplacementSimilarityThreshold < 0 || settings.ReplacementSimilarityThreshold > 1 ? 0.82 : settings.ReplacementSimilarityThreshold,
                        SimilarityCacheSeconds = settings.SimilarityCacheSeconds < 1 ? 12 : settings.SimilarityCacheSeconds,
                        OcrLanguageTag = string.IsNullOrWhiteSpace(settings.OcrLanguageTag) ? "zh-Hans" : settings.OcrLanguageTag,
                        TargetLanguageCode = string.IsNullOrWhiteSpace(settings.TargetLanguageCode) ? "ko" : settings.TargetLanguageCode,
                        OcrEngineType = settings.OcrEngineType,
                        GoogleWebAppUrl = settings.GoogleWebAppUrl ?? string.Empty,
                        ShowOverlayInScreenShare = settings.ShowOverlayInScreenShare,
                        CaptureGeometryVersion = CurrentCaptureGeometryVersion
                    };
                }
            }
            return new AppSettings(CaptureGeometryVersion: CurrentCaptureGeometryVersion);
        }
        catch (Exception ex)
        {
            AppLog.Write("Settings load failed", ex);
            return new AppSettings(CaptureGeometryVersion: CurrentCaptureGeometryVersion);
        }
    }

    private static bool HasJsonProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out _);
        }
        catch
        {
            return false;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Write("Settings save failed", ex);
        }
    }
}
