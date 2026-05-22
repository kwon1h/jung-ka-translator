using System.IO;

namespace GameOverlayTranslator.App.Services;

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (Sync)
            {
                File.AppendAllText(CurrentLogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
