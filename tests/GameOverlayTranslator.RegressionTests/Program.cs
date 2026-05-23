using System.IO;
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
    ("Empty screen OCR does not clear overlay items", TestEmptyScreenOcrDoesNotPublishEmptyOverlayItems)
};

foreach (var test in tests)
{
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
        new FakeOcrEngine(new OcrResult("z: 快使用天使!", [])),
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
