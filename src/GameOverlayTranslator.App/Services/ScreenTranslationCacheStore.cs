using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameOverlayTranslator.App.Services;

public interface ITranslationCacheStore
{
    Dictionary<string, string> Load();
    bool Save(IReadOnlyDictionary<string, string> cache);
}

public sealed class ScreenTranslationCacheStore : ITranslationCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly string cachePath;

    public ScreenTranslationCacheStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameOverlayTranslator",
            "screen_translation_cache.json"))
    {
    }

    internal ScreenTranslationCacheStore(string cachePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        this.cachePath = cachePath;
    }

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

    public bool Save(IReadOnlyDictionary<string, string> cache)
    {
        var temporaryPath = $"{cachePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, cachePath, true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("Screen cache save failed", ex);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }
}
