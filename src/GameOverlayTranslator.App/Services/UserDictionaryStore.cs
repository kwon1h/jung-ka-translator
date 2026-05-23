using System.IO;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class UserDictionaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string dictionaryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameOverlayTranslator",
        "user_dictionary.json");

    public List<UserDictEntry> Load()
    {
        try
        {
            if (File.Exists(dictionaryPath))
            {
                var entries = JsonSerializer.Deserialize<List<UserDictEntry>>(File.ReadAllText(dictionaryPath), JsonOptions);
                if (entries != null)
                {
                    return entries;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("UserDictionary load failed", ex);
        }
        return [];
    }

    public void Save(List<UserDictEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dictionaryPath)!);
            File.WriteAllText(dictionaryPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.Write("UserDictionary save failed", ex);
        }
    }
}
