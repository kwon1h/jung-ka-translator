using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Services;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Default dictionary has entries", TestDefaultDictionaryCategories),
    ("Chat parser splits speaker and message", TestChatParser),
    ("Chat parser supports mixed speaker names", TestChatParserMixedSpeakerNames),
    ("Chat parser splits concatenated speaker lines", TestChatParserSplitsConcatenatedSpeakerLines),
    ("Concatenated chat does not translate embedded speaker", TestConcatenatedChatDoesNotTranslateEmbeddedSpeaker),
    ("User dictionary CSV round trip", TestUserDictionaryCsvRoundTrip),
    ("Overlay defaults are readable", TestOverlayDefaults),
    ("App settings map persisted filters", TestAppSettingsMapsPersistedFilters),
    ("Dictionary exact chat skips API", TestExactDictionarySkipsTranslation),
    ("Dictionary screen line skips API", TestDictionaryOnlyScreenLineSkipsTranslation),
    ("Rejected chat does not poison duplicate cache", TestRejectedChatDoesNotPoisonExactDuplicateCache),
    ("Repeated screen OCR uses cache", TestRepeatedScreenLineUsesCachedTranslation),
    ("Repeated chat translation uses cache", TestRepeatedChatLineUsesCachedTranslation),
    ("Empty screen OCR keeps overlay items", TestEmptyScreenOcrDoesNotPublishEmptyOverlayItems),
    ("Screen translation publishes translated diagnostic", TestScreenTranslatedDiagnostic),
    ("Screen diagnostic source contains only translation requests", TestScreenDiagnosticSourceContainsOnlyTranslationRequests),
    ("Screen cache publishes skipped diagnostic", TestScreenCacheSkippedDiagnostic),
    ("Skipped diagnostics are not log entries", TestSkippedDiagnosticsAreNotLogEntries),
    ("Screen exclude region skips OCR lines", TestScreenExcludeRegionSkipsOcrLines),
    ("Chat exclude region skips OCR lines", TestChatExcludeRegionSkipsOcrLines),
    ("Chat small excluded edge overlap keeps OCR line", TestChatSmallExcludedEdgeOverlapKeepsOcrLine),
    ("Screen selected region applies window-relative exclude", TestScreenSelectedRegionAppliesWindowRelativeExclude),
    ("Exclude region outside selection does not skip OCR lines", TestExcludeRegionOutsideSelectionDoesNotSkipOcrLines),
    ("All excluded OCR lines skip translation", TestAllExcludedOcrLinesSkipTranslation),
    ("No OCR publishes no text skip", TestNoOcrPublishesSkip),
    ("Duplicate chat publishes skip", TestDuplicateChatPublishesSkip),
    ("Chat API usage is counted once", TestChatApiUsageCounted),
    ("Chat diagnostic source excludes speaker", TestChatDiagnosticSourceExcludesSpeaker),
    ("Screen API usage is counted once", TestScreenApiUsageCounted),
    ("Repeated screen segments are deduplicated", TestScreenSegmentDeduplicatesRepeatedSentences),
    ("Cache hit usage is zero", TestDirectCacheHitUsageIsZero),
    ("Provider usage is preserved", TestProviderUsageIsPreserved),
    ("Translation failure cooldown bypasses API", TestTranslationFailureCooldown),
    ("Screen segment splits by spaces", TestScreenSegmentSplitsBySpaces),
    ("Chinese ratio bypasses screen filter", TestChineseRatioBypassesScreenFilter),
    ("Chinese ratio bypasses chat filter", TestChineseRatioBypassesChatFilter),
    ("English-only screen lines are hidden", TestEnglishOnlyScreenLinesAreHidden),
    ("Multiple include regions filter OCR lines", TestMultipleIncludeRegionsFilterOcrLines),
    ("Chat translation keeps OCR position", TestChatTranslationKeepsOcrPosition)
};

var cachePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GameOverlayTranslator",
    "screen_translation_cache.json");

foreach (var test in tests)
{
    if (File.Exists(cachePath))
    {
        File.Delete(cachePath);
    }

    await test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static Task TestDefaultDictionaryCategories()
{
    Assert(UserDictionaryStore.DefaultDictionary.Count > 0, "Default dictionary is empty.");
    Assert(UserDictionaryStore.DefaultDictionary.All(entry => !string.IsNullOrWhiteSpace(entry.Category)), "Dictionary category must not be empty.");
    return Task.CompletedTask;
}

static Task TestChatParser()
{
    var lines = ChatLineParser.Parse("racer: hello");
    Assert(lines.Count == 1, "Expected one parsed chat line.");
    Assert(lines[0].Speaker == "racer", "Unexpected speaker.");
    Assert(lines[0].Message == "hello", "Unexpected message.");
    return Task.CompletedTask;
}

static Task TestChatParserMixedSpeakerNames()
{
    var lines = ChatLineParser.Parse("Ry\u66F9\u601D\u59AE: \u5E26\u4F60\u53BB");
    Assert(lines.Count == 1, "Expected one parsed mixed-name chat line.");
    Assert(lines[0].Speaker == "Ry\u66F9\u601D\u59AE", "Unexpected mixed speaker.");
    Assert(lines[0].Message == "\u5E26\u4F60\u53BB", "Unexpected mixed-speaker message.");

    lines = ChatLineParser.Parse("sToRy\u66F9\u601D\u59AE: \u6211\u8BF4\u6211\u773C\u79D1\u6709\u4EBA");
    Assert(lines.Count == 1, "Expected one parsed mixed-name chat line with Chinese message.");
    Assert(lines[0].Speaker == "sToRy\u66F9\u601D\u59AE", "Unexpected Chinese mixed speaker.");
    Assert(lines[0].Message == "\u6211\u8BF4\u6211\u773C\u79D1\u6709\u4EBA", "Unexpected Chinese mixed-speaker message.");
    return Task.CompletedTask;
}

static Task TestChatParserSplitsConcatenatedSpeakerLines()
{
    var speaker = "sToRy\u00E4\u601D\u59AE";
    var firstMessage = "\u6211\u8BF4\u6211\u773C\u79D1\u6709\u4EBA";
    var secondMessage = "\u5E26\u4F60\u53BB";
    var lines = ChatLineParser.Parse($"{speaker}: {firstMessage}{speaker}\uFF1A{secondMessage}");

    Assert(lines.Count == 2, $"Expected two parsed chat lines, got {lines.Count}.");
    Assert(lines[0].Speaker == speaker, "Unexpected first speaker.");
    Assert(lines[0].Message == firstMessage, "Unexpected first message.");
    Assert(lines[1].Speaker == speaker, "Unexpected second speaker.");
    Assert(lines[1].Message == secondMessage, "Unexpected second message.");
    return Task.CompletedTask;
}

static async Task TestConcatenatedChatDoesNotTranslateEmbeddedSpeaker()
{
    var speaker = "sToRy\u00E4\u601D\u59AE";
    var firstMessage = "\u6211\u8BF4\u6211\u773C\u79D1\u6709\u4EBA";
    var secondMessage = "\u5E26\u4F60\u53BB";
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(),
        new FakeOcrEngine(new OcrResult($"{speaker}: {firstMessage}{speaker}\uFF1A{secondMessage}", [])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    Assert(translation.SingleRequests == 2, $"Expected two chat translation requests, got {translation.SingleRequests}.");
    Assert(translation.LastSingleTexts.SequenceEqual([firstMessage, secondMessage]), "Translation requests should contain messages only.");
    Assert(translation.LastSingleTexts.All(text => !text.Contains("sToRy", StringComparison.Ordinal)), "Embedded speaker must not be sent for translation.");

    var translated = Diagnostics(updates)
        .Where(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated)
        .ToList();
    Assert(translated.Count == 2, $"Expected two translated chat updates, got {translated.Count}.");
    Assert(translated.All(update => update.Speaker == speaker), "Translated chat updates should keep the speaker column.");
}

static Task TestUserDictionaryCsvRoundTrip()
{
    var directory = Path.Combine(Path.GetTempPath(), "GameOverlayTranslatorTests", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new UserDictionaryStore(directory);
        var expected = new[]
        {
            new UserDictEntry("hello", "annyeong", UserDictionaryStore.UserCategory),
            new UserDictEntry("go", "start", UserDictionaryStore.QuickReplyCategory)
        };

        store.Save(expected);
        var loaded = store.Load();
        Assert(File.Exists(store.DictionaryPath), "Expected user_dictionary.csv to be written.");
        Assert(loaded.Any(entry => entry.Source == "hello" && entry.Target == "annyeong"), "CSV entry was not loaded.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    return Task.CompletedTask;
}

static Task TestOverlayDefaults()
{
    var settings = new AppSettings();
    Assert(settings.FontFamily == AppSettingsDefaults.PreferredFontFamily, "Default overlay font should be the preferred Kart Gothic font.");
    Assert(settings.FontSize == AppSettingsDefaults.DefaultFontSize, "Default overlay font size should be 25.");
    Assert(settings.TextColor == "#FFFFFF", "Default text color should be white.");
    Assert(settings.OutlineColor == "#000000", "Default outline should be black.");
    Assert(settings.StrokeThickness == AppSettingsDefaults.DefaultStrokeThickness, "Default outline thickness should be 0.5px.");
    Assert(settings.OverlayDurationSeconds == 4, "Default overlay duration should be 4 seconds.");
    return Task.CompletedTask;
}

static Task TestAppSettingsMapsPersistedFilters()
{
    var settings = new AppSettings(
        EnableLengthFilter: false,
        MinMessageLength: 5,
        MaxMessageLength: 48,
        EnableNoiseFilter: false,
        MaxNoiseTokenCount: 7,
        EnableSeparatorFilter: false,
        MaxSeparatorsCount: 2,
        EnableSimilarityFilter: false,
        SimilarityThreshold: 0.31,
        ReplacementSimilarityThreshold: 0.64,
        SimilarityCacheSeconds: 27);

    var filter = settings.ToFilterSettings();

    Assert(!filter.EnableLengthFilter, "Length filter setting was not mapped.");
    Assert(filter.MinMessageLength == 5, "Minimum message length was not mapped.");
    Assert(filter.MaxMessageLength == 48, "Maximum message length was not mapped.");
    Assert(!filter.EnableNoiseFilter, "Noise filter setting was not mapped.");
    Assert(filter.MaxNoiseTokenCount == 7, "Maximum noise token count was not mapped.");
    Assert(!filter.EnableSeparatorFilter, "Separator filter setting was not mapped.");
    Assert(filter.MaxSeparatorsCount == 2, "Maximum separator count was not mapped.");
    Assert(!filter.EnableSimilarityFilter, "Similarity filter setting was not mapped.");
    Assert(filter.SimilarityThreshold == 0.31, "Similarity threshold was not mapped.");
    Assert(filter.ReplacementSimilarityThreshold == 0.64, "Replacement similarity threshold was not mapped.");
    Assert(filter.SimilarityCacheSeconds == 27, "Similarity cache seconds was not mapped.");
    return Task.CompletedTask;
}

static async Task TestExactDictionarySkipsTranslation()
{
    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult("racer: hello", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat, [new UserDictEntry("hello", "annyeong", UserDictionaryStore.UserCategory)]));

    Assert(translation.SingleRequests == 0, "Dictionary chat should not call API.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule == "Dictionary"), "Expected dictionary skip diagnostic.");
}

static async Task TestDictionaryOnlyScreenLineSkipsTranslation()
{
    var translation = new CountingTranslationService();
    var ocr = new OcrResult("hello", [new OcrLineResult("hello", new Rect(0, 0, 120, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen, [new UserDictEntry("hello", "annyeong", UserDictionaryStore.UserCategory)]));

    Assert(translation.BatchRequests == 0, "Dictionary screen line should not call API.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped), "Expected skipped diagnostic.");
}

static async Task TestRejectedChatDoesNotPoisonExactDuplicateCache()
{
    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult("z: ", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    Assert(translation.SingleRequests == 0, "Rejected chat should not call API.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule is "NoText" or "QualityFilter"), "Expected quality or no-text skip.");
    Assert(updates.All(update => update.FilterRule != "Duplicate"), "Rejected chat must not become a duplicate skip.");
}

static async Task TestRepeatedScreenLineUsesCachedTranslation()
{
    var translation = new CountingTranslationService();
    var cached = new CachingTranslationService(translation, new ScreenTranslationCacheStore());
    var source = Chinese("7f13 5b58 6d4b 8bd5 6587 672c 7532 4e59 4e19");
    var ocr = new OcrResult(source, [new OcrLineResult(source, new Rect(12, 34, 160, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), cached);

    await RunSession(session, CreateOptions(TranslationMode.Screen), 110);

    Assert(translation.BatchRequests == 1, "Repeated screen OCR should call API once.");
}

static async Task TestRepeatedChatLineUsesCachedTranslation()
{
    var translation = new CountingTranslationService();
    var cached = new CachingTranslationService(translation, new ScreenTranslationCacheStore());
    var request = new TranslationRequest("hello", "ko", "en");
    var first = await cached.TranslateAsync(request, CancellationToken.None);
    var second = await cached.TranslateAsync(request, CancellationToken.None);

    Assert(translation.SingleRequests == 1, "Repeated chat request should call API once.");
    Assert(first.TranslatedText == second.TranslatedText, "Cached translation should match.");
    Assert((second.Usage?.OutboundRequestCount ?? -1) == 0, "Cache hit should report zero outbound requests.");
}

static async Task TestEmptyScreenOcrDoesNotPublishEmptyOverlayItems()
{
    var translation = new CountingTranslationService();
    var source = Chinese("7f13 5b58 6d4b 8bd5 6587 672c");
    var session = new TranslationSession(
        new FakeCaptureService(),
        new SequencedOcrEngine(
            new OcrResult(source, [new OcrLineResult(source, new Rect(12, 34, 160, 24))]),
            new OcrResult(string.Empty, [])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen), 110);

    Assert(updates.Any(update => update.ScreenItems is { Count: > 0 }), "Expected initial overlay items.");
    Assert(updates.All(update => update.ScreenItems is null || update.ScreenItems.Count > 0), "Empty OCR must not publish empty overlay items.");
}

static async Task TestScreenTranslatedDiagnostic()
{
    var translation = new CountingTranslationService();
    var source = Chinese("8bca 65ad 5c4f 5e55 6587 672c 9700 8981 7ffb 8bd1");
    var session = ScreenSession(source, translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    var diagnostics = Diagnostics(updates);
    Assert(diagnostics.Any(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated && update.FilterRule == "Translated"), "Expected translated diagnostic.");
    Assert(diagnostics.All(update => update.Status != "OCR detected" && update.FilterRule != "TranslationRequest"), "Intermediate diagnostics must not be published.");
    Assert(diagnostics.Sum(update => update.TranslationRequestCount) == 1, "Expected one outbound request counted.");
}

static async Task TestScreenDiagnosticSourceContainsOnlyTranslationRequests()
{
    var translation = new CountingTranslationService();
    var sent = Chinese("6bd4 8d5 65f6 95f4");
    var dictionaryOnly = Chinese("53ea 6709 7eff 8272 73a9 5bb6");
    var ocr = new OcrResult($"{sent}\n{dictionaryOnly}",
    [
        new OcrLineResult(sent, new Rect(0, 0, 160, 24)),
        new OcrLineResult(dictionaryOnly, new Rect(0, 30, 180, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Screen, [new UserDictEntry(dictionaryOnly, "dictionary target", UserDictionaryStore.UserCategory)]));

    var translated = Diagnostics(updates).Single(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.DiagnosticSourceText == sent, "Screen diagnostic source should contain only text sent to translation.");
    Assert(translated.OcrRawText?.Contains(dictionaryOnly, StringComparison.Ordinal) == true, "Raw OCR should remain available separately.");

    var logEntry = DiagnosticLogFormatter.Create(translated);
    Assert(logEntry?.Source == sent, "Diagnostic log entry should use DiagnosticSourceText.");
}

static async Task TestScreenCacheSkippedDiagnostic()
{
    var translation = new CountingTranslationService();
    var cached = new CachingTranslationService(translation, new ScreenTranslationCacheStore());
    var source = Chinese("7f13 5b58 8df3 8fc7 6d4b 8bd5 6587 672c");
    var session = ScreenSession(source, cached);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen), 110);

    Assert(translation.BatchRequests == 1, "Expected one initial API request.");
    Assert(Diagnostics(updates).Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.TranslationRequestCount == 0), "Expected cache skip diagnostic.");
}

static async Task TestSkippedDiagnosticsAreNotLogEntries()
{
    var skipped = new SessionUpdate(
        "Skipped",
        SourceText: "racer: hello",
        FilterRule: "Dictionary",
        DiagnosticKind: DiagnosticKind.OcrSkipped);
    Assert(DiagnosticLogFormatter.Create(skipped) is null, "Skipped diagnostics should not become log entries.");

    var translatedWithoutRequest = new SessionUpdate(
        "Translated",
        DiagnosticKind: DiagnosticKind.OcrTranslated);
    Assert(DiagnosticLogFormatter.Create(translatedWithoutRequest) is null, "Translated updates without outbound usage should not become log entries.");

    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult(string.Empty, [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule == "NoText"), "Expected no-text skip diagnostic.");
    Assert(updates.All(update => DiagnosticLogFormatter.Create(update) is null), "No-text polling should not add diagnostic log entries.");
}

static async Task TestScreenExcludeRegionSkipsOcrLines()
{
    var translation = new CountingTranslationService();
    var ignored = Chinese("7528 6237 540d");
    var translated = Chinese("6bd4 8d5 65f6 95f4");
    var ocr = new OcrResult($"{ignored}\n{translated}",
    [
        new OcrLineResult(ignored, new Rect(10, 430, 180, 24)),
        new OcrLineResult(translated, new Rect(850, 80, 120, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(1000, 800), new FakeOcrEngine(ocr), translation);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Screen) with
        {
            ExcludedRegions = [new CaptureRegion(0, 0.4, 0.25, 0.4)]
        });

    Assert(translation.BatchRequests == 1, "Expected one batch request for non-excluded text.");
    Assert(translation.LastBatchTexts.Count == 1, "Excluded username line should not be sent for translation.");
    Assert(translation.LastBatchTexts[0] == translated, "Only the non-excluded line should be translated.");
}

static async Task TestChatExcludeRegionSkipsOcrLines()
{
    var translation = new CountingTranslationService();
    var ignored = Chinese("5ffd 7565 6d88 606f");
    var translated = Chinese("9700 8981 7ffb 8bd1");
    var ignoredLine = $"bad1: {ignored}";
    var translatedLine = $"ok1: {translated}";
    var ocr = new OcrResult($"{ignoredLine}\n{translatedLine}",
    [
        new OcrLineResult(ignoredLine, new Rect(10, 430, 180, 24)),
        new OcrLineResult(translatedLine, new Rect(850, 80, 120, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(1000, 800), new FakeOcrEngine(ocr), translation);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Chat) with
        {
            ExcludedRegions = [new CaptureRegion(0, 0.4, 0.25, 0.4)]
        });

    Assert(translation.SingleRequests == 1, "Expected one chat request for non-excluded text.");
    Assert(translation.LastSingleTexts.Count == 1, "Excluded chat line should not be sent for translation.");
    Assert(translation.LastSingleTexts[0] == translated, "Only the non-excluded chat message should be translated.");
}

static async Task TestChatSmallExcludedEdgeOverlapKeepsOcrLine()
{
    var translation = new CountingTranslationService();
    var translated = Chinese("8fb9 7f18 91cd 53e0");
    var translatedLine = $"ok1: {translated}";
    var ocr = new OcrResult(translatedLine, [new OcrLineResult(translatedLine, new Rect(10, 80, 300, 24))]);
    var session = new TranslationSession(new FakeCaptureService(1000, 800), new FakeOcrEngine(ocr), translation);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Chat) with
        {
            ExcludedRegions = [new CaptureRegion(0, 0, 0.02, 1)]
        });

    Assert(translation.SingleRequests == 1, "A small edge overlap should not remove the chat OCR line.");
    Assert(translation.LastSingleTexts[0] == translated, "The chat message should still be translated.");
}

static async Task TestScreenSelectedRegionAppliesWindowRelativeExclude()
{
    var translation = new CountingTranslationService();
    var ignored = Chinese("9009 62e9 533a 57df 5185");
    var translated = Chinese("9009 62e9 533a 57df 5916");
    var ocr = new OcrResult($"{ignored}\n{translated}",
    [
        new OcrLineResult(ignored, new Rect(10, 80, 180, 24)),
        new OcrLineResult(translated, new Rect(300, 80, 180, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(500, 800), new FakeOcrEngine(ocr), translation);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Screen) with
        {
            Region = new CaptureRegion(0.5, 0, 0.5, 1),
            ExcludedRegions = [new CaptureRegion(0.5, 0, 0.25, 1)]
        });

    Assert(translation.BatchRequests == 1, "Expected one screen request for non-excluded selected-region text.");
    Assert(translation.LastBatchTexts.Count == 1, "Window-relative excluded region should map into the selected capture.");
    Assert(translation.LastBatchTexts[0] == translated, "Only text outside the mapped excluded region should be translated.");
}

static async Task TestExcludeRegionOutsideSelectionDoesNotSkipOcrLines()
{
    var translation = new CountingTranslationService();
    var first = Chinese("7b2c 4e00 884c");
    var second = Chinese("7b2c 4e8c 884c");
    var ocr = new OcrResult($"{first}\n{second}",
    [
        new OcrLineResult(first, new Rect(10, 80, 180, 24)),
        new OcrLineResult(second, new Rect(300, 80, 180, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(500, 800), new FakeOcrEngine(ocr), translation);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Screen) with
        {
            Region = new CaptureRegion(0.5, 0, 0.5, 1),
            ExcludedRegions = [new CaptureRegion(0, 0, 0.25, 1)]
        });

    Assert(translation.BatchRequests == 1, "Expected one batch request.");
    Assert(translation.LastBatchTexts.Count == 2, "Non-overlapping excluded region should not remove OCR lines.");
    Assert(translation.LastBatchTexts.Contains(first), "First line should still be translated.");
    Assert(translation.LastBatchTexts.Contains(second), "Second line should still be translated.");
}

static async Task TestAllExcludedOcrLinesSkipTranslation()
{
    var translation = new CountingTranslationService();
    var source = Chinese("5168 90e8 9664 5916");
    var ocr = new OcrResult(source, [new OcrLineResult(source, new Rect(10, 80, 180, 24))]);
    var session = new TranslationSession(new FakeCaptureService(1000, 800), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(
        session,
        CreateOptions(TranslationMode.Screen) with
        {
            ExcludedRegions = [new CaptureRegion(0, 0, 1, 1)]
        });

    Assert(translation.BatchRequests == 0, "All excluded OCR lines should not call the translation API.");
    Assert(Diagnostics(updates).Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule == "NoText"), "Expected NoText skip after all OCR lines are excluded.");
}

static async Task TestNoOcrPublishesSkip()
{
    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult(string.Empty, [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    Assert(Diagnostics(updates).Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule == "NoText"), "Expected NoText skip.");
    Assert(translation.BatchRequests == 0, "No OCR should not call API.");
}

static async Task TestDuplicateChatPublishesSkip()
{
    var translation = new CountingTranslationService();
    var message = Chinese("4f60 597d 4e16 754c");
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult($"racer: {message}", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    Assert(translation.SingleRequests == 1, "First chat line should be translated once.");
    Assert(Diagnostics(updates).Any(update => update.DiagnosticKind == DiagnosticKind.OcrSkipped && update.FilterRule == "Duplicate"), "Repeated chat should publish duplicate skip.");
}

static async Task TestChatApiUsageCounted()
{
    var translation = new CountingTranslationService();
    var message = Chinese("4f60 597d 4e16 754c");
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult($"racer: {message}", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = Diagnostics(updates).Single(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.TranslationRequestCount == 1, "Chat usage should count one request.");
    Assert(translated.TotalTranslationRequestCount == 1, "Chat total usage should be one request.");
}

static async Task TestChatDiagnosticSourceExcludesSpeaker()
{
    var translation = new CountingTranslationService();
    var message = Chinese("6211 60f3 89c1 5979 4e86");
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult($"racer: {message}", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = Diagnostics(updates).Single(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.SourceText == $"racer: {message}", "Chat source text should remain available for result windows.");
    Assert(translated.DiagnosticSourceText == message, "Chat diagnostic source should contain only the translated message.");
    Assert(translated.DiagnosticSourceText?.Contains("racer", StringComparison.OrdinalIgnoreCase) == false, "Diagnostic source should not contain the speaker.");

    var logEntry = DiagnosticLogFormatter.Create(translated);
    Assert(logEntry?.Source == message, "Diagnostic log entry should use speaker-free DiagnosticSourceText.");
}

static async Task TestScreenApiUsageCounted()
{
    var translation = new CountingTranslationService();
    var source = Chinese("4f7f 7528 91cf 7edf 8ba1 5c4f 5e55");
    var session = ScreenSession(source, translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    var translated = Diagnostics(updates).Single(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.TranslationRequestCount == 1, "Screen usage should count one request.");
    Assert(translated.TotalTranslationRequestCount == 1, "Screen total usage should be one request.");
}

static async Task TestScreenSegmentDeduplicatesRepeatedSentences()
{
    var translation = new CountingTranslationService();
    var sentence = Chinese("91cd 590d 53e5 5b50 9700 8981 7ffb 8bd1");
    var ocr = new OcrResult($"{sentence}\n{sentence}",
    [
        new OcrLineResult(sentence, new Rect(0, 0, 160, 24)),
        new OcrLineResult(sentence, new Rect(0, 30, 160, 24))
    ]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    Assert(translation.BatchRequests == 1, "Expected one batch request.");
    Assert(translation.LastBatchTexts.Count == 1, "Expected one unique segment in batch.");
    Assert(Diagnostics(updates).Sum(update => update.TranslationRequestCount) == 1, "Usage should count one outbound request.");
}

static async Task TestDirectCacheHitUsageIsZero()
{
    var translation = new CountingTranslationService();
    var cached = new CachingTranslationService(translation, new ScreenTranslationCacheStore());
    var request = new TranslationRequest("cache direct", "ko", "en");

    _ = await cached.TranslateAsync(request, CancellationToken.None);
    var second = await cached.TranslateAsync(request, CancellationToken.None);

    Assert(second.Usage?.OutboundRequestCount == 0, "Cache hit should not report outbound requests.");
    Assert(second.Usage?.OutboundCharacterCount == 0, "Cache hit should not report outbound characters.");
}

static async Task TestProviderUsageIsPreserved()
{
    var translation = new FixedUsageTranslationService(3, 42);
    var result = await translation.TranslateAsync(new TranslationRequest("hello", "ko", "en"), CancellationToken.None);

    Assert(result.Usage?.OutboundRequestCount == 3, "Provider request usage should be preserved.");
    Assert(result.Usage?.OutboundCharacterCount == 42, "Provider char usage should be preserved.");
}

static async Task TestTranslationFailureCooldown()
{
    var failing = new FailingTranslationService();
    var cached = new CachingTranslationService(failing, new ScreenTranslationCacheStore());
    var request = new TranslationRequest("failure", "ko", "en");

    try
    {
        await cached.TranslateAsync(request, CancellationToken.None);
        Assert(false, "Expected first failure.");
    }
    catch (InvalidOperationException)
    {
    }

    var bypassed = await cached.TranslateAsync(request, CancellationToken.None);
    Assert(bypassed.TranslatedText == "failure", "Failure cooldown should return source text.");
    Assert(failing.CallCount == 1, "Cooldown should avoid second provider call.");
}

static SessionOptions CreateOptions(TranslationMode mode, IReadOnlyList<UserDictEntry>? dictionary = null) =>
    new(
        new CaptureTarget(new CapturableWindow(1, "Test Window", "test")),
        new CaptureRegion(0, 0, 1, 1),
        new OcrLanguage("zh-Hans", "Chinese"),
        new TranslationLanguage("ko", "Korean"),
        TimeSpan.FromMilliseconds(20),
        new FilterSettings(),
        dictionary ?? Array.Empty<UserDictEntry>(),
        mode);

static async Task RunSession(TranslationSession session, SessionOptions options, int milliseconds = 80)
{
    using var cts = new CancellationTokenSource();
    await session.StartAsync(options, cts.Token);
    await Task.Delay(milliseconds);
    await session.StopAsync();
}

static List<SessionUpdate> Collect(TranslationSession session)
{
    var updates = new List<SessionUpdate>();
    session.Updated += (_, update) => updates.Add(update);
    return updates;
}

static List<SessionUpdate> Diagnostics(IEnumerable<SessionUpdate> updates) =>
    updates.Where(update => update.DiagnosticKind is DiagnosticKind.OcrTranslated or DiagnosticKind.OcrSkipped).ToList();

static TranslationSession ScreenSession(string source, ITranslationService translation)
{
    var ocr = new OcrResult(source, [new OcrLineResult(source, new Rect(0, 0, 260, 24))]);
    return new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
}

static string Chinese(string hexCodePoints) =>
    string.Concat(hexCodePoints.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(hex => char.ConvertFromUtf32(Convert.ToInt32(hex, 16))));

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Task TestScreenSegmentSplitsBySpaces()
{
    var segments = ScreenTranslationSegmenter.Split("hello world test", new OcrLanguage("en", "English"));
    Assert(segments.Count == 3, $"Expected 3 segments, got {segments.Count}");
    Assert(segments[0].Text == "hello", $"Expected 'hello', got '{segments[0].Text}'");
    Assert(segments[1].Text == "world", $"Expected 'world', got '{segments[1].Text}'");
    Assert(segments[2].Text == "test", $"Expected 'test', got '{segments[2].Text}'");
    return Task.CompletedTask;
}

static async Task TestChineseRatioBypassesScreenFilter()
{
    var translation = new CountingTranslationService();
    var source = Chinese("8bd1"); // "译"
    var session = ScreenSession(source, translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen));

    Assert(translation.BatchRequests == 1, "Should bypass filter and request translation for Chinese text.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated), "Expected translated diagnostic.");
}

static async Task TestChineseRatioBypassesChatFilter()
{
    var translation = new CountingTranslationService();
    var source = Chinese("8bd1"); // "译"
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(new OcrResult($"racer: {source}", [])), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    Assert(translation.SingleRequests == 1, "Should bypass quality filter and request translation for Chinese chat line.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated), "Expected translated diagnostic.");
}

static async Task TestEnglishOnlyScreenLinesAreHidden()
{
    var translation = new CountingTranslationService();
    var ocr = new OcrResult("RANKING", [new OcrLineResult("RANKING", new Rect(10, 10, 100, 20))]);
    var session = new TranslationSession(new FakeCaptureService(200, 100), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen) with { SuppressEnglishOnlyScreenLines = true });

    Assert(translation.BatchRequests == 0, "English-only screen text should not call translation.");
    Assert(!updates.Any(update => update.ScreenItems is { Count: > 0 }), "English-only screen text should not be shown.");
    Assert(updates.Any(update => update.FilterRule == "EnglishOnly" && update.ScreenItems is { Count: 0 }), "English-only OCR should clear an existing overlay.");
}

static async Task TestMultipleIncludeRegionsFilterOcrLines()
{
    var first = Chinese("8bd1");
    var second = Chinese("8f66");
    var ocr = new OcrResult($"{first}\n{second}",
    [
        new OcrLineResult(first, new Rect(10, 10, 40, 20)),
        new OcrLineResult(second, new Rect(150, 10, 40, 20))
    ]);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(200, 100), new FakeOcrEngine(ocr), translation);

    await RunSession(session, CreateOptions(TranslationMode.Screen) with
    {
        IncludedRegions = [new CaptureRegion(0, 0, 0.5, 1)]
    });

    Assert(translation.LastBatchTexts.SequenceEqual([first]), "Only OCR lines inside include regions should be translated.");
}

static async Task TestChatTranslationKeepsOcrPosition()
{
    var source = Chinese("8bd1");
    var expected = new Rect(12, 34, 160, 24);
    var ocr = new OcrResult($"racer: {source}", [new OcrLineResult($"racer: {source}", expected)]);
    var session = new TranslationSession(new FakeCaptureService(200, 100), new FakeOcrEngine(ocr), new CountingTranslationService());
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = updates.First(update => update.IsChatLine && update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.BoundingRect == expected, "Chat translation should retain its OCR bounding rectangle.");
}

sealed class FakeCaptureService(int width = 1, int height = 1) : ICaptureService
{
    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
    public IReadOnlyList<string> LastSingleTexts => singleTexts;
    public IReadOnlyList<string> LastBatchTexts { get; private set; } = Array.Empty<string>();

    private readonly List<string> singleTexts = [];

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        SingleRequests++;
        singleTexts.Add(request.Text);
        return Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", null));
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        LastBatchTexts = request.Texts.ToList();
        return Task.FromResult(new BatchTranslationResult(request.Texts.Select(text => $"translated:{text}").ToList()));
    }
}

sealed class FixedUsageTranslationService(int requests, int chars) : ITranslationService
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", null, TranslationUsage.Outbound(requests, chars)));

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new BatchTranslationResult(request.Texts.Select(text => $"translated:{text}").ToList(), TranslationUsage.Outbound(requests, chars)));
}

sealed class FailingTranslationService : ITranslationService
{
    public int CallCount { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        CallCount++;
        throw new InvalidOperationException("API Error Mock");
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        CallCount++;
        throw new InvalidOperationException("API Error Mock");
    }
}
