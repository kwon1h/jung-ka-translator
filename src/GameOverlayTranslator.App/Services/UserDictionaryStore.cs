using System.IO;
using System.Text;
using System.Text.Json;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class UserDictionaryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string CsvHeader = "Source,Target,Category";
    public const string QuickReplyCategory = "채팅 빠른 답장";
    public const string UiCategory = "게임 UI 고정어";
    public const string RaceCategory = "트랙/모드명";
    public const string ItemCategory = "아이템/차량 용어";
    public const string UserCategory = "사용자";

    public static readonly IReadOnlyList<UserDictEntry> DefaultDictionary =
    [
        // Chat quick replies.
        new("对不起>_<", "미안해 >_<", QuickReplyCategory),
        new("没关系~", "괜찮아~", QuickReplyCategory),
        new("小心~!!", "조심~!!", QuickReplyCategory),
        new("快帮帮我!!", "빨리 도와줘!!", QuickReplyCategory),
        new("我先走一步了哦~", "나 먼저 갈게~", QuickReplyCategory),
        new("我没救了。。", "난 이제 끝이야..", QuickReplyCategory),
        new("玩得不错!!", "정말 재미있네요!!", QuickReplyCategory),
        new("快使用天使!", "빨리 천사를 써!", QuickReplyCategory),
        new("快使用道具锁!", "빨리 아이템 잠금 장치를 써!", QuickReplyCategory),
        new("快使用**!", "빨리 ** 써!", QuickReplyCategory),
        new("对方使用了天使!", "상대가 천사를 사용했다!", QuickReplyCategory),
        new("我要用定时水炸弹了!", "타이머 물폭탄 쓸게!", QuickReplyCategory),
        new("小心地面!", "바닥 조심!", QuickReplyCategory),
        new("小心水炸弹!", "물 폭탄 조심!", QuickReplyCategory),
        new("帮忙夺第一名!", "1등을 차지하게 도와줘!", QuickReplyCategory),
        new("帮忙使用下道具锁!", "아이템 잠금 장치 좀 써줘!", QuickReplyCategory),
        new("帮忙使用下必杀技!", "필살기 좀 써줘!", QuickReplyCategory),

        // Lobby and menu terms from the provided PopKart screenshots.
        new("我的物品", "내 물품", UiCategory),
        new("道具组合", "아이템 조합", UiCategory),
        new("成就", "업적", UiCategory),
        new("道具图鉴", "아이템 도감", UiCategory),
        new("我的徽章", "내 배지", UiCategory),
        new("管理", "관리", UiCategory),
        new("寻找小屋", "집 찾기", UiCategory),
        new("随机进入", "랜덤 입장", UiCategory),
        new("多人游戏", "멀티플레이", UiCategory),
        new("单人游戏", "싱글플레이", UiCategory),
        new("小屋", "마이룸", UiCategory),
        new("商店", "상점", UiCategory),
        new("车库", "차고", UiCategory),
        new("俱乐部", "클럽", UiCategory),
        new("跑跑通行证", "카트 패스", UiCategory),
        new("设置", "설정", UiCategory),
        new("部件", "부품", UiCategory),
        new("分解", "분해", UiCategory),
        new("升级", "강화", UiCategory),
        new("合成", "합성", UiCategory),
        new("车辆改装", "차량 개조", UiCategory),
        new("成长型车辆", "성장형 차량", UiCategory),
        new("材料车辆", "재료 차량", UiCategory),
        new("持有车辆", "보유 차량", UiCategory),
        new("车辆信息", "차량 정보", UiCategory),
        new("车辆性能", "차량 성능", UiCategory),
        new("装备部件", "장착 부품", UiCategory),
        new("车辆功能", "차량 기능", UiCategory),
        new("加速度", "가속도", UiCategory),
        new("弯道", "코너링", UiCategory),
        new("漂移", "드리프트", UiCategory),
        new("加速时间", "가속 시간", UiCategory),
        new("集气速度", "게이지 충전 속도", UiCategory),
        new("开始升级", "강화 시작", UiCategory),
        new("开始合成", "합성 시작", UiCategory),
        new("开始分解", "분해 시작", UiCategory),
        new("帮助", "도움말", UiCategory),
        new("部件强化", "부품 강화", UiCategory),
        new("部件初始化", "부품 초기화", UiCategory),
        new("变形预览", "변형 미리보기", UiCategory),
        new("兑换部件碎片", "부품 조각 교환", UiCategory),
        new("分解结果", "분해 결과", UiCategory),
        new("普通", "일반", UiCategory),
        new("高级", "고급", UiCategory),
        new("稀有", "희귀", UiCategory),
        new("终极", "최종", UiCategory),
        new("精英", "엘리트", UiCategory),
        new("无限制", "제한 없음", UiCategory),
        new("获得车辆", "획득 차량", UiCategory),
        new("幸运点等级", "행운 포인트 등급", UiCategory),
        new("手续费", "수수료", UiCategory),
        new("金币", "코인", UiCategory),
        new("酷币", "쿨코인", UiCategory),

        // Race, room, and track selection.
        new("计时赛", "타임어택", RaceCategory),
        new("练习计时赛", "연습 타임어택", RaceCategory),
        new("排位计时赛", "랭킹 타임어택", RaceCategory),
        new("竞争排位赛", "경쟁 랭킹전", RaceCategory),
        new("道具赛", "아이템전", RaceCategory),
        new("竞速赛", "스피드전", RaceCategory),
        new("组队道具赛", "팀 아이템전", RaceCategory),
        new("组队竞速赛", "팀 스피드전", RaceCategory),
        new("个人道具赛", "개인 아이템전", RaceCategory),
        new("个人竞速赛", "개인 스피드전", RaceCategory),
        new("无限加速", "무한 부스터", RaceCategory),
        new("快速开始", "빠른 시작", RaceCategory),
        new("创建房间", "방 만들기", RaceCategory),
        new("房间名称", "방 이름", RaceCategory),
        new("赛道名称", "트랙 이름", RaceCategory),
        new("房间人数", "인원", RaceCategory),
        new("准备", "준비", RaceCategory),
        new("准备完毕", "준비 완료", RaceCategory),
        new("游戏正在准备中", "게임 준비 중", RaceCategory),
        new("退出", "나가기", RaceCategory),
        new("未加密", "비밀번호 없음", RaceCategory),
        new("城镇 运河", "빌리지 운하", RaceCategory),
        new("城镇 高速公路", "빌리지 고속도로", RaceCategory),
        new("矿山 采矿区捷径", "광산 채굴장 지름길", RaceCategory),
        new("矿山 宝石开采场", "광산 보석 채굴장", RaceCategory),
        new("矿山 岩浆洞穴", "광산 용암 동굴", RaceCategory),
        new("矿山 瀑布", "광산 폭포", RaceCategory),
        new("矿山 熔岩小径", "광산 용암길", RaceCategory),
        new("人气随机", "인기랜덤", RaceCategory),
        new("随机", "랜덤", RaceCategory),
        new("困难", "어려움", RaceCategory),
        new("冰河 滑雪场", "빙하 스키장", RaceCategory),
        new("沙漠 被遗忘的记忆", "사막 잊혀진 기억", RaceCategory),
        new("环游世界 纽约狂飙", "월드 뉴욕 대질주", RaceCategory),

        // Profile, settings, and club.
        new("车手资料", "라이더 정보", UiCategory),
        new("经验", "경험치", UiCategory),
        new("自我介绍", "자기소개", UiCategory),
        new("访问小屋", "마이룸 방문", UiCategory),
        new("申请好友", "친구 신청", UiCategory),
        new("申请情侣", "커플 신청", UiCategory),
        new("VIP等级", "VIP 등급", UiCategory),
        new("快速回复", "빠른 답장", UiCategory),
        new("聊天设置", "채팅 설정", UiCategory),
        new("查看行驶中聊天内容", "주행 중 채팅 보기", UiCategory),
        new("查看房间内聊天内容", "방 안 채팅 보기", UiCategory),
        new("输入快速回复的内容", "빠른 답장 내용 입력", UiCategory),
        new("全体", "전체", UiCategory),
        new("队伍", "팀", UiCategory),
        new("俱乐部目录", "클럽 목록", UiCategory),
        new("创建俱乐部", "클럽 만들기", UiCategory),
        new("注册俱乐部目录", "클럽 목록 등록", UiCategory),
        new("俱乐部名称", "클럽 이름", UiCategory),
        new("俱乐部会长名", "클럽장 이름", UiCategory),
        new("俱乐部等级", "클럽 레벨", UiCategory),
        new("俱乐部会长", "클럽장", UiCategory),
        new("俱乐部会员数", "클럽원 수", UiCategory),
        new("俱乐部创建日", "클럽 생성일", UiCategory),
        new("周活跃度", "주간 활동도", UiCategory),
        new("俱乐部简介", "클럽 소개", UiCategory),
        new("加入俱乐部", "클럽 가입", UiCategory),
        new("我的俱乐部信息", "내 클럽 정보", UiCategory),
        new("搜索", "검색", UiCategory),
        new("确认", "확인", UiCategory),
        new("取消", "취소", UiCategory),

        // Shop and inventory.
        new("充值", "충전", ItemCategory),
        new("输入兑换券", "교환권 입력", ItemCategory),
        new("精品道具全服记录", "프리미엄 아이템 전체 기록", ItemCategory),
        new("推荐", "추천", ItemCategory),
        new("新商品", "신상품", ItemCategory),
        new("热门商品", "인기 상품", ItemCategory),
        new("活动", "이벤트", ItemCategory),
        new("卡丁车", "카트", ItemCategory),
        new("角色", "캐릭터", ItemCategory),
        new("礼包", "패키지", ItemCategory),
        new("装备", "장비", ItemCategory),
        new("使用", "사용", ItemCategory),
        new("装饰", "꾸미기", ItemCategory),
        new("星标道具", "즐겨찾기 아이템", ItemCategory),
        new("锁定", "잠금", ItemCategory),
        new("预设", "프리셋", ItemCategory),
        new("使用中", "사용 중", ItemCategory),
        new("黄色染色剂", "노란색 염색약", ItemCategory),
        new("确定", "확인", ItemCategory)
    ];

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
        foreach (var defaultEntry in LoadDefaultDictionary())
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
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "user_dictionary.csv"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "user_dictionary.csv")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "user_dictionary.csv"))
        };

        foreach (var candidate in candidates)
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
                AppLog.Write($"Default user dictionary load failed. Path={candidate}", ex);
            }
        }

        return DefaultDictionary;
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
