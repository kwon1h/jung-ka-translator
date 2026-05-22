using System.IO;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed record AppSettings(
    string? LastWindowTitle = null,
    string? LastWindowProcessName = null,
    CaptureRegion? LastRegion = null,
    TranslationDisplayMode DisplayMode = TranslationDisplayMode.Window);

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
            return File.Exists(settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
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
