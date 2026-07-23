using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameOverlayTranslator.App.Services;

public sealed class ScreenTranslationCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly string cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "screen_translation_cache.json");

    public Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(cachePath))
            {
                var json = File.ReadAllText(cachePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                return dict ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Screen cache load failed", ex);
        }
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public void Save(IReadOnlyDictionary<string, string> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            var temporaryPath = $"{cachePath}.tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, cachePath, true);
        }
        catch (Exception ex)
        {
            AppLog.Write("Screen cache save failed", ex);
        }
    }
}
