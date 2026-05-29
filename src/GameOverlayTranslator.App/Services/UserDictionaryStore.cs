using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class UserDictionaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string CsvHeader = "Source,Target,Category";
    private const string EmbeddedDefaultDictionaryResourceName = "GameOverlayTranslator.App.Assets.user_dictionary.csv";
    public const string QuickReplyCategory = "채팅 빠른 답장";
    public const string UiCategory = "게임 UI 고정어";
    public const string RaceCategory = "트랙/모드명";
    public const string ItemCategory = "아이템/차량 용어";
    public const string UserCategory = "사용자";

    public static readonly IReadOnlyList<UserDictEntry> DefaultDictionary = LoadDefaultDictionary();

    private readonly string dictionaryPath;
    private readonly string legacyJsonPath;

    public UserDictionaryStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameOverlayTranslator"))
    {
    }

    public UserDictionaryStore(string directoryPath)
    {
        dictionaryPath = Path.Combine(directoryPath, "user_dictionary.csv");
        legacyJsonPath = Path.Combine(directoryPath, "user_dictionary.json");
    }

    public string DictionaryPath => dictionaryPath;

    public List<UserDictEntry> Load()
    {
        try
        {
            var entries = LoadEntries();

            var changed = NormalizeCategories(entries);
            changed |= MergeDefaults(entries);
            if (!File.Exists(dictionaryPath) || changed)
            {
                Save(entries);
            }

            return entries;
        }
        catch (Exception ex)
        {
            AppLog.Write("UserDictionary load failed", ex);
            return [.. DefaultDictionary];
        }
    }

    public void Save(IEnumerable<UserDictEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dictionaryPath)!);
            File.WriteAllText(dictionaryPath, SerializeCsv(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            AppLog.Write("UserDictionary save failed", ex);
        }
    }

    private List<UserDictEntry> LoadEntries()
    {
        if (File.Exists(dictionaryPath))
        {
            return ParseCsv(File.ReadAllText(dictionaryPath, Encoding.UTF8));
        }

        if (File.Exists(legacyJsonPath))
        {
            return JsonSerializer.Deserialize<List<UserDictEntry>>(File.ReadAllText(legacyJsonPath), JsonOptions) ?? [];
        }

        return [];
    }

    private static string SerializeCsv(IEnumerable<UserDictEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CsvHeader);
        foreach (var entry in entries)
        {
            builder
                .Append(EscapeCsv(entry.Source))
                .Append(',')
                .Append(EscapeCsv(entry.Target))
                .Append(',')
                .AppendLine(EscapeCsv(string.IsNullOrWhiteSpace(entry.Category) ? UserCategory : entry.Category));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static List<UserDictEntry> ParseCsv(string csv)
    {
        var rows = ReadCsvRows(csv).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var startIndex = HasCsvHeader(rows[0]) ? 1 : 0;
        var entries = new List<UserDictEntry>();
        for (var index = startIndex; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]) || string.IsNullOrWhiteSpace(row[1]))
            {
                AppLog.Write($"Skipping invalid user dictionary CSV row {index + 1}.");
                continue;
            }

            var category = row.Count >= 3 && !string.IsNullOrWhiteSpace(row[2]) ? row[2].Trim() : UserCategory;
            entries.Add(new UserDictEntry(row[0].Trim(), row[1].Trim(), category));
        }

        return entries;
    }

    private static bool HasCsvHeader(IReadOnlyList<string> row) =>
        row.Count >= 2 &&
        string.Equals(TrimBom(row[0]).Trim(), "Source", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(row[1].Trim(), "Target", StringComparison.OrdinalIgnoreCase);

    private static string TrimBom(string value) =>
        value.Length > 0 && value[0] == '\uFEFF' ? value[1..] : value;

    private static IEnumerable<List<string>> ReadCsvRows(string csv)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var current = csv[index];
            if (inQuotes)
            {
                if (current == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (index + 1 < csv.Length && csv[index + 1] == '\n')
                    {
                        index++;
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = [];
                    break;
                default:
                    field.Append(current);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    private static bool MergeDefaults(List<UserDictEntry> entries)
    {
        var changed = false;
        foreach (var defaultEntry in DefaultDictionary)
        {
            if (entries.Any(entry => string.Equals(entry.Source, defaultEntry.Source, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            entries.Add(defaultEntry);
            changed = true;
        }

        return changed;
    }

    private static IReadOnlyList<UserDictEntry> LoadDefaultDictionary()
    {
        try
        {
            using var embeddedStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDefaultDictionaryResourceName);
            if (embeddedStream is not null)
            {
                using var reader = new StreamReader(embeddedStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return ParseCsv(reader.ReadToEnd());
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"Embedded default dictionary load failed. Resource={EmbeddedDefaultDictionaryResourceName}", ex);
        }

        foreach (var candidate in GetDevelopmentDefaultDictionaryCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                return ParseCsv(File.ReadAllText(candidate, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                AppLog.Write($"Development default dictionary load failed. Path={candidate}", ex);
            }
        }

        AppLog.Write("Default user dictionary source was not found.");
        return [];
    }

    private static IEnumerable<string> GetDevelopmentDefaultDictionaryCandidates()
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "user_dictionary.csv"));
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "user_dictionary.csv"));
    }

    private static bool NormalizeCategories(List<UserDictEntry> entries)
    {
        var changed = false;
        for (var index = 0; index < entries.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(entries[index].Category))
            {
                continue;
            }

            entries[index] = entries[index] with { Category = UserCategory };
            changed = true;
        }

        return changed;
    }
}
