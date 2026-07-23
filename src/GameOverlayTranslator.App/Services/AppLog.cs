using System.IO;
using System.Text;

namespace GameOverlayTranslator.App.Services;

public static class AppLog
{
    private const long MaxLogBytes = 4 * 1024 * 1024;
    private const int LogRetentionDays = 14;
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "logs");
    private static readonly Dictionary<string, DateTimeOffset> LastThrottledWrites = new(StringComparer.Ordinal);
    private static DateOnly lastMaintenanceDate;

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (Sync)
            {
                var now = DateTime.Now;
                var entry = $"{now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}";
                var logPath = Path.Combine(LogDirectory, $"{now:yyyy-MM-dd}.log");
                RotateIfNeeded(logPath, Encoding.UTF8.GetByteCount(entry), MaxLogBytes);
                File.AppendAllText(logPath, entry);

                var today = DateOnly.FromDateTime(now);
                if (today != lastMaintenanceDate)
                {
                    DeleteExpiredLogs(LogDirectory, DateTime.UtcNow.AddDays(-LogRetentionDays));
                    lastMaintenanceDate = today;
                }
            }
        }
        catch
        {
        }
    }

    public static void WriteThrottled(string key, string message, TimeSpan minimumInterval)
    {
        if (ShouldWriteThrottled(key, DateTimeOffset.UtcNow, minimumInterval))
        {
            Write(message);
        }
    }

    internal static bool ShouldWriteThrottled(string key, DateTimeOffset now, TimeSpan minimumInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (minimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumInterval));
        }

        lock (Sync)
        {
            if (LastThrottledWrites.TryGetValue(key, out var previous)
                && now - previous < minimumInterval)
            {
                return false;
            }

            LastThrottledWrites[key] = now;
            if (LastThrottledWrites.Count > 128)
            {
                foreach (var staleKey in LastThrottledWrites
                             .OrderBy(entry => entry.Value)
                             .Take(LastThrottledWrites.Count - 128)
                             .Select(entry => entry.Key)
                             .ToArray())
                {
                    LastThrottledWrites.Remove(staleKey);
                }
            }
            return true;
        }
    }

    internal static void RotateIfNeeded(string logPath, long incomingBytes, long maxBytes)
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length + incomingBytes <= maxBytes)
        {
            return;
        }

        var archivePath = Path.Combine(
            Path.GetDirectoryName(logPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(logPath)}.previous{Path.GetExtension(logPath)}");
        File.Move(logPath, archivePath, true);
    }

    internal static void DeleteExpiredLogs(string logDirectory, DateTime cutoffUtc)
    {
        if (!Directory.Exists(logDirectory))
        {
            return;
        }

        foreach (var logPath in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(logPath) < cutoffUtc)
            {
                File.Delete(logPath);
            }
        }
    }
}
