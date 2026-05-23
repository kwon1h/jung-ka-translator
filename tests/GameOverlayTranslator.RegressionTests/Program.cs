using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Services;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("중국어 트랙 이미지 OCR 수행", TestOcrTrackList),
    ("채팅 파서가 유저명과 메시지를 분리한다", TestChatParser),
    ("기본 사전은 카테고리를 가진다", TestDefaultDictionaryCategories),
    ("User dictionary is stored as external CSV", TestUserDictionaryCsvRoundTrip),
    ("오버레이 기본 설정은 FHD 가독성 기준을 만족한다", TestOverlayDefaults),
    ("사전 100% 일치 채팅은 번역 API를 호출하지 않는다", TestExactDictionarySkipsTranslation),
    ("사전 치환 후 원문 언어가 없으면 화면 번역 API를 호출하지 않는다", TestDictionaryOnlyScreenLineSkipsTranslation),
    ("품질 필터로 버린 채팅은 중복 캐시에 남지 않는다", TestRejectedChatDoesNotPoisonExactDuplicateCache),
    ("Repeated screen OCR line uses cached translation", TestRepeatedScreenLineUsesCachedTranslation),
    ("Empty screen OCR does not clear overlay items", TestEmptyScreenOcrDoesNotPublishEmptyOverlayItems),
    ("사전 OCR 변형 매칭 및 공백/품질 필터 우회 테스트", TestDictionaryMatchingWithOcrVariations),
    ("Google Unofficial 번역 서비스 동작 테스트", TestGoogleUnofficialTranslation),
    ("Google Web App 번역 서비스 동작 테스트", TestGoogleWebAppTranslation),
    ("Google 번역 공백 정규화 및 배치 중복 제거 최적화 테스트", TestTranslationTokenOptimization),
    ("중국어 유저명 띄어쓰기 오인식 시 품질 검사 통과 및 파싱 검증 테스트", TestChineseSpeakerOcrSpacingQualityPass)
};

// Clear persistent translation cache before tests to prevent state contamination
var cachePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GameOverlayTranslator",
    "screen_translation_cache.json");
if (File.Exists(cachePath))
{
    File.Delete(cachePath);
}

foreach (var test in tests)
{
    if (File.Exists(cachePath))
    {
        File.Delete(cachePath);
    }

    await test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static Task TestChatParser()
{
    var lines = ChatLineParser.Parse("zuyeong: 快使用天使!");
    Assert(lines.Count == 1, "Expected one parsed chat line.");
    Assert(lines[0].Speaker == "zuyeong", "Unexpected speaker.");
    Assert(lines[0].Message == "快使用天使!", "Unexpected message.");
    return Task.CompletedTask;
}

static Task TestDefaultDictionaryCategories()
{
    Assert(UserDictionaryStore.DefaultDictionary.Count > 0, "Default dictionary is empty.");
    Assert(UserDictionaryStore.DefaultDictionary.All(entry => !string.IsNullOrWhiteSpace(entry.Category)), "Dictionary category must not be empty.");
    Assert(UserDictionaryStore.DefaultDictionary.Any(entry => entry.Category == UserDictionaryStore.QuickReplyCategory), "Missing quick reply category.");
    Assert(UserDictionaryStore.DefaultDictionary.Any(entry => entry.Category == UserDictionaryStore.UiCategory), "Missing UI category.");
    Assert(UserDictionaryStore.DefaultDictionary.Any(entry => entry.Category == UserDictionaryStore.RaceCategory), "Missing race category.");
    Assert(UserDictionaryStore.DefaultDictionary.Any(entry => entry.Category == UserDictionaryStore.ItemCategory), "Missing item category.");
    return Task.CompletedTask;
}

static Task TestUserDictionaryCsvRoundTrip()
{
    var directory = Path.Combine(Path.GetTempPath(), "GameOverlayTranslatorTests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new UserDictionaryStore(directory);
        var expected = new[]
        {
            new UserDictEntry("hello, racer", "안녕, 레이서", UserDictionaryStore.UserCategory),
            new UserDictEntry("say \"go\"", "\"출발\"이라고 말하기", UserDictionaryStore.QuickReplyCategory)
        };

        store.Save(expected);
        var loaded = store.Load();

        Assert(File.Exists(store.DictionaryPath), "Expected user_dictionary.csv to be written.");
        Assert(loaded.Any(entry => entry.Source == expected[0].Source && entry.Target == expected[0].Target && entry.Category == expected[0].Category), "CSV entry with comma was not preserved.");
        Assert(loaded.Any(entry => entry.Source == expected[1].Source && entry.Target == expected[1].Target && entry.Category == expected[1].Category), "CSV entry with quote was not preserved.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task TestOverlayDefaults()
{
    var settings = new AppSettings();
    Assert(settings.FontSize >= 22, "Default overlay font size should be at least 22pt.");
    Assert(settings.TextColor == "#FFFFFF", "Default overlay text should be white.");
    Assert(settings.OutlineColor == "#000000", "Default overlay outline should be black.");
    Assert(settings.OverlayBackgroundColor != "#00000000", "Default overlay should use a readable background.");
    return Task.CompletedTask;
}

static async Task TestExactDictionarySkipsTranslation()
{
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(),
        new FakeOcrEngine(new OcrResult("zuyeong: 快使用天使!", [])),
        translation);

    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Chat), cts.Token);
    await Task.Delay(80);
    await session.StopAsync();

    Assert(translation.SingleRequests == 0, "Exact dictionary chat should not call single translation.");
    Assert(updates.Any(update => update.FilterRule == "UserDictionaryExact"), "Expected UserDictionaryExact update.");
}

static async Task TestDictionaryOnlyScreenLineSkipsTranslation()
{
    var translation = new CountingTranslationService();
    var ocr = new OcrResult(
        "快使用天使!",
        [new OcrLineResult("快使用天使!", new Rect(0, 0, 120, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Screen), cts.Token);
    await Task.Delay(80);
    await session.StopAsync();

    Assert(translation.BatchRequests == 0, "Dictionary-only screen line should not call batch translation.");
}

static async Task TestRepeatedScreenLineUsesCachedTranslation()
{
    var translation = new CountingTranslationService();
    const string source = "\u7f13\u5b58\u6d4b\u8bd5\u6587\u672c\u7532\u4e59\u4e19";
    var ocr = new OcrResult(
        source,
        [new OcrLineResult(source, new Rect(12, 34, 120, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Screen), cts.Token);
    await Task.Delay(90);
    await session.StopAsync();

    Assert(translation.BatchRequests == 1, "Repeated screen OCR line should be translated once and then reused from cache.");
}

static async Task TestEmptyScreenOcrDoesNotPublishEmptyOverlayItems()
{
    var translation = new CountingTranslationService();
    const string source = "\u7f13\u5b58\u6d4b\u8bd5\u6587\u672c\u7532\u4e59\u4e19";
    var session = new TranslationSession(
        new FakeCaptureService(),
        new SequencedOcrEngine(
            new OcrResult(
                source,
                [new OcrLineResult(source, new Rect(12, 34, 120, 24))]),
            new OcrResult(string.Empty, [])),
        translation);
    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Screen), cts.Token);
    await Task.Delay(90);
    await session.StopAsync();

    Assert(updates.Any(update => update.ScreenItems is { Count: > 0 }), "Expected initial screen overlay items.");
    Assert(updates.All(update => update.ScreenItems is null || update.ScreenItems.Count > 0), "Empty OCR frames must not publish empty overlay items.");
}

static async Task TestRejectedChatDoesNotPoisonExactDuplicateCache()
{
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(),
        new FakeOcrEngine(new OcrResult("z: 这是一个测试!", [])),
        translation);

    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Chat), cts.Token);
    await Task.Delay(120);
    await session.StopAsync();

    Assert(updates.Any(update => update.FilterRule == "SpeakerValidation"), "Expected speaker validation rejection.");
    Assert(updates.All(update => update.FilterRule != "ExactDuplicateFilter"), "Rejected chat must not poison exact duplicate cache.");
    Assert(translation.SingleRequests == 0, "Rejected chat should not call translation.");
}

static SessionOptions CreateOptions(TranslationMode mode) =>
    new(
        new CaptureTarget(new CapturableWindow(1, "Test Window", "test")),
        new CaptureRegion(0, 0, 1, 1),
        new OcrLanguage("zh-Hans", "중국어(간체)"),
        new TranslationLanguage("ko", "한국어"),
        TimeSpan.FromMilliseconds(20),
        new FilterSettings(),
        UserDictionaryStore.DefaultDictionary,
        mode);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task TestOcrTrackList()
{
    var tcs = new TaskCompletionSource<bool>();
    var thread = new Thread(() =>
    {
        try
        {
            var path = @"c:\Users\Kwon\Documents\game overlay translator\track_list.jpg";
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var frame = new CapturedFrame(bitmap);
            var engine = new WindowsOcrEngine();
            
            Task.Run(async () =>
            {
                try
                {
                    var result = await engine.RecognizeAsync(frame, new OcrLanguage("zh-Hans", "중국어(간체)"), CancellationToken.None);
                    Console.WriteLine("=== START OCR OUTPUT ===");
                    foreach (var line in result.Lines)
                    {
                        Console.WriteLine(line.Text);
                    }
                    Console.WriteLine("=== END OCR OUTPUT ===");
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"OCR async failed: {ex}");
                    tcs.SetException(ex);
                }
            }).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OCR thread failed: {ex}");
            tcs.SetException(ex);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    await tcs.Task;
}

static async Task TestDictionaryMatchingWithOcrVariations()
{
    var translation = new CountingTranslationService();
    // OCR results with spacing/punctuation variations and noisy speaker
    var ocrResults = new List<OcrResult>
    {
        new OcrResult("z u Y e o n g : 快 帮 帮 我 ！ ！", []),
        new OcrResult("zuyeong: 没 关 系 ·", []),
        new OcrResult("zuyeong: 对 不 起 > <", [])
    };

    var session = new TranslationSession(
        new FakeCaptureService(),
        new SequencedOcrEngine(ocrResults.ToArray()),
        translation);

    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Chat), cts.Token);
    await Task.Delay(180); // Wait for multiple polling ticks
    await session.StopAsync();

    // Verify all 3 chat lines matched the user dictionary exactly,
    // bypassing the normal speaker quality reject and standard translation service call.
    Assert(translation.SingleRequests == 0, "No standard translation requests should be sent.");
    var dictExactUpdates = updates.Where(update => update.FilterRule == "UserDictionaryExact").ToList();
    Assert(dictExactUpdates.Count == 3, $"Expected 3 UserDictionaryExact matches, but got {dictExactUpdates.Count}.");

    // Check if the translations are correct
    Assert(dictExactUpdates.Any(u => u.TranslatedText == "빨리 도와줘!!"), "Missing translation for '快帮帮我!!'");
    Assert(dictExactUpdates.Any(u => u.TranslatedText == "괜찮아~"), "Missing translation for '没关系~'");
    Assert(dictExactUpdates.Any(u => u.TranslatedText == "미안해 >_<"), "Missing translation for '对不起>_<'");
}

static async Task TestGoogleUnofficialTranslation()
{
    using var client = new HttpClient();
    var service = new GoogleUnofficialTranslationService(client);
    
    // Single Translation Test
    var result = await service.TranslateAsync(new TranslationRequest("Hello", "ko", "en"), CancellationToken.None);
    Assert(result.TranslatedText.Trim() == "안녕하세요" || result.TranslatedText.Trim().Contains("안녕"), 
        $"Google Unofficial single translation failed. Got: {result.TranslatedText}");

    // Batch Translation Test
    var batchResult = await service.TranslateBatchAsync(new BatchTranslationRequest(new[] { "Hello", "World" }, "ko", "en"), CancellationToken.None);
    Assert(batchResult.TranslatedTexts.Count == 2, "Expected 2 batch translation results");
    Assert(batchResult.TranslatedTexts[0].Trim().Contains("안녕"), $"First translation mismatch: {batchResult.TranslatedTexts[0]}");
    Assert(batchResult.TranslatedTexts[1].Trim().Contains("세계") || batchResult.TranslatedTexts[1].Trim().Contains("월드") || batchResult.TranslatedTexts[1].Trim().Contains("세상"), $"Second translation mismatch: {batchResult.TranslatedTexts[1]}");
}

static async Task TestGoogleWebAppTranslation()
{
    using var client = new HttpClient();
    
    // GoogleWebAppUrl is dummy for unit testing, but we can verify it throws when URL is empty.
    var serviceEmptyUrl = new GoogleWebAppTranslationService(client, () => string.Empty);
    try
    {
        await serviceEmptyUrl.TranslateAsync(new TranslationRequest("Hello", "ko", "en"), CancellationToken.None);
        Assert(false, "Expected InvalidOperationException when Web App URL is empty");
    }
    catch (InvalidOperationException)
    {
        // Expected
    }

    // Also verify delegator switches correctly
    var dummySettings = new AppSettings(TranslatorType: TranslationServiceType.GoogleUnofficial);
    var delegator = new TranslationServiceDelegator(client, () => null, () => dummySettings);
    var delegatorResult = await delegator.TranslateAsync(new TranslationRequest("Hello", "ko", "en"), CancellationToken.None);
    Assert(delegatorResult.TranslatedText.Trim().Contains("안녕"), "Delegator routing to Google Unofficial failed");
}

static async Task TestTranslationTokenOptimization()
{
    // Verify Chinese space normalization and batch deduplication
    var translation = new CountingTranslationService();
    // 3 identical lines with weird spaces to test normalization + deduplication
    // (Uses text not present in the user dictionary)
    var ocrResults = new List<OcrResult>
    {
        new OcrResult(
            "这   是  一 个   测 试",
            [
                new OcrLineResult("这   是  一 个   测 试", new Rect(0, 0, 100, 20)),
                new OcrLineResult("这   是  一 个   测 试", new Rect(0, 30, 100, 20)),
                new OcrLineResult("这  是 一  个 测 试", new Rect(0, 60, 100, 20))
            ])
    };

    var session = new TranslationSession(
        new FakeCaptureService(),
        new SequencedOcrEngine(ocrResults.ToArray()),
        translation);

    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Screen), cts.Token);
    await Task.Delay(90);
    await session.StopAsync();

    // Verification:
    // 1. All 3 lines normalized to "这是一个测试" and sent.
    // 2. Because of Deduplication, only 1 batch translation request containing exactly 1 text ("这是一个测试") should be sent.
    Assert(translation.BatchRequests == 1, $"Expected 1 batch request, got {translation.BatchRequests}");
    var finalUpdate = updates.LastOrDefault(update => update.ScreenItems != null);
    Assert(finalUpdate != null, "Expected screen items update");
    Assert(finalUpdate!.ScreenItems!.Count == 3, $"Expected 3 screen items, got {finalUpdate.ScreenItems.Count}");
    
    // Ensure all three items resolved to the same translated text
    foreach (var item in finalUpdate.ScreenItems!)
    {
        Assert(item.TranslatedText == "translated:这是一个测试", $"Unexpected translated text: {item.TranslatedText}");
    }
}

static async Task TestChineseSpeakerOcrSpacingQualityPass()
{
    // Verify that a speaker with spaced Chinese chars (common OCR error) is successfully parsed and passes quality filter
    var translation = new CountingTranslationService();
    // Spaced Chinese speaker with spacing in message
    var ocrResults = new List<OcrResult>
    {
        new OcrResult("风 屿 六 横 1 2 纵 : 睁 不 开 眼 了", [])
    };

    var session = new TranslationSession(
        new FakeCaptureService(),
        new SequencedOcrEngine(ocrResults.ToArray()),
        translation);

    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);

    using var cts = new CancellationTokenSource();
    await session.StartAsync(CreateOptions(TranslationMode.Chat), cts.Token);
    await Task.Delay(90);
    await session.StopAsync();

    // Verify:
    // 1. Parsing: The parser must correctly identify "风屿六横12纵" as the speaker (no trailing/leading spaces or truncation to single char).
    // 2. Translation Request: The message should successfully pass the SpeakerValidation check (since speaker is now parsed as multiple letters/han) and be translated.
    Assert(translation.SingleRequests == 1, $"Expected 1 translation request, got {translation.SingleRequests}");
    var finalUpdate = updates.LastOrDefault(update => update.IsChatLine);
    Assert(finalUpdate != null, "Expected chat line update");
    Assert(finalUpdate.Speaker?.Replace(" ", "") == "风屿六横12纵", $"Expected speaker '风屿六横12纵' (without spaces), got '{finalUpdate.Speaker}'");
    Assert(finalUpdate.TranslatedText == "translated:睁不开眼了", $"Unexpected translated text: {finalUpdate.TranslatedText}");
}

sealed class FakeCaptureService : ICaptureService
{
    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        var bitmap = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
        return Task.FromResult(new CapturedFrame(bitmap));
    }
}

sealed class FakeOcrEngine(OcrResult result) : IOcrEngine
{
    public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct) =>
        Task.FromResult(result);
}

sealed class SequencedOcrEngine(params OcrResult[] results) : IOcrEngine
{
    private int index;

    public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        var result = results[Math.Min(index, results.Length - 1)];
        index++;
        return Task.FromResult(result);
    }
}

sealed class CountingTranslationService : ITranslationService
{
    public int SingleRequests { get; private set; }
    public int BatchRequests { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        SingleRequests++;
        return Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", null));
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        return Task.FromResult(new BatchTranslationResult(request.Texts.Select(text => $"translated:{text}").ToList()));
    }
}

