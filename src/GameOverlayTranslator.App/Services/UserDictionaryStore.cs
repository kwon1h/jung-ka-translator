using System.IO;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class UserDictionaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    public static readonly IReadOnlyList<UserDictEntry> DefaultDictionary =
    [
        new("对不起>_<", "죄송해요>_<"),
        new("没关系~", "괜찮아요~"),
        new("小心~！！", "조심~!!"),
        new("快帮帮我！！", "도와주세요!!"),
        new("我先走一步了哦~", "저 먼저 갈게요~"),
        new("我没救了。。", "저 틀렸어요.."),
        new("玩得不错！！", "잘하시네요!!"),
        new("快使用天使!", "빨리 천사 쓰세요!"),
        new("快使用道具锁!", "빨리 자물쇠 쓰세요!"),
        new("快使用**!", "빨리 ** 쓰세요!"),
        new("对方使用了天使!", "상대 천사 썼어요!"),
        new("我要用定时水炸弹了!", "타이머 물폭탄 씁니다!"),
        new("小心地面!", "바닥 조심!"),
        new("小心水炸弹!", "물폭탄 조심!"),
        new("帮忙狙击第一名!", "1등 저격 지원 좀!"),
        new("帮忙使用下道具锁!", "자물쇠 좀 써주세요!"),
        new("帮忙사용下必杀技!", "필살기 좀 써주세요!"), // Handles possible OCR variation
        new("帮忙使用下必杀技!", "필살기 좀 써주세요!")
    ];

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
            else
            {
                Save(DefaultDictionary);
                return [.. DefaultDictionary];
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("UserDictionary load failed", ex);
        }
        return [];
    }

    public void Save(IEnumerable<UserDictEntry> entries)
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
