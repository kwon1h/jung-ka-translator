using System.IO;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed record AppSettings(
    string? LastWindowTitle = null,
    string? LastWindowProcessName = null,
    CaptureRegion? LastRegion = null,
    TranslationDisplayMode DisplayMode = TranslationDisplayMode.Window,
    string FontFamily = "Malgun Gothic",
    double FontSize = 20,
    string TextColor = "#FFFFFF",
    string OutlineColor = "#000000",
    double StrokeThickness = 0.3);

public enum TranslationDisplayMode
{
    Window,
    TransparentOverlay
}

public sealed class AppSettingsStore
{
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
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), JsonOptions);
                if (settings != null)
                {
                    // Normalize settings to ensure default values are used for missing/invalid properties
                    return settings with
                    {
                        FontFamily = string.IsNullOrWhiteSpace(settings.FontFamily) ? "Malgun Gothic" : settings.FontFamily,
                        FontSize = settings.FontSize < 12 || settings.FontSize > 48 ? 20 : settings.FontSize,
                        TextColor = string.IsNullOrWhiteSpace(settings.TextColor) ? "#FFFFFF" : settings.TextColor,
                        OutlineColor = string.IsNullOrWhiteSpace(settings.OutlineColor) ? "#000000" : settings.OutlineColor,
                        StrokeThickness = settings.StrokeThickness < 0 || settings.StrokeThickness > 8 ? 0.3 : settings.StrokeThickness
                    };
                }
            }
            return new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Write("Settings load failed", ex);
            return new AppSettings();
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
