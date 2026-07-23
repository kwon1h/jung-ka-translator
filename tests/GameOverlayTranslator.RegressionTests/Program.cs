using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;
using GameOverlayTranslator.App.Services;
using CvMatType = OpenCvSharp.MatType;
using CvVec3b = OpenCvSharp.Vec3b;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Default dictionary has entries", TestDefaultDictionaryCategories),
    ("Chat parser splits speaker and message", TestChatParser),
    ("Chat parser supports mixed speaker names", TestChatParserMixedSpeakerNames),
    ("Chat parser splits concatenated speaker lines", TestChatParserSplitsConcatenatedSpeakerLines),
    ("Concatenated chat does not translate embedded speaker", TestConcatenatedChatDoesNotTranslateEmbeddedSpeaker),
    ("User dictionary CSV round trip", TestUserDictionaryCsvRoundTrip),
    ("Legacy user dictionary CSV gets default language pair", TestLegacyUserDictionaryCsvMigration),
    ("Overlay defaults are readable", TestOverlayDefaults),
    ("PaddleOCR is the only OCR engine", TestPaddleOcrIsOnlyEngine),
    ("OCR model catalog includes downloadable languages", TestOcrModelCatalog),
    ("Installed OCR languages require their cached model", TestInstalledOcrLanguageDetection),
    ("Translation target catalog includes major languages", TestTranslationTargetCatalog),
    ("Quick-chat copy names the selected game language", TestResultWindowUsesSelectedGameLanguage),
    ("PaddleOCR bitmap conversion preserves BGR pixels", TestPaddleOcrBitmapConversion),
    ("PaddleOCR reuses only identical frames", TestPaddleOcrFrameCache),
    ("OCR masks modify the pooled pixel buffer", TestPaddleOcrPixelMasks),
    ("Translation session runs OCR off caller context", TestTranslationSessionRunsOcrOffCallerContext),
    ("Slow OCR polling yields CPU time back to the game", TestSlowOcrPollingYieldsToGame),
    ("Session status ignores routine skips and reports recovery", TestSessionStatusTracksRecovery),
    ("Session updates coalesce into one UI dispatch", TestSessionUpdateBuffer),
    ("App settings map persisted filters", TestAppSettingsMapsPersistedFilters),
    ("App settings flush the latest debounced value", TestAppSettingsFlushesLatestValue),
    ("Dictionary exact chat skips API", TestExactDictionarySkipsTranslation),
    ("Dictionary screen line skips API", TestDictionaryOnlyScreenLineSkipsTranslation),
    ("Chinese-Korean dictionary is not used for other targets", TestDictionaryIsScopedToChineseKorean),
    ("User dictionary supports additional language pairs", TestDictionarySupportsAdditionalLanguagePairs),
    ("Rejected chat does not poison duplicate cache", TestRejectedChatDoesNotPoisonExactDuplicateCache),
    ("Repeated screen OCR uses cache", TestRepeatedScreenLineUsesCachedTranslation),
    ("Repeated chat translation uses cache", TestRepeatedChatLineUsesCachedTranslation),
    ("Translation cache is isolated by effective provider", TestTranslationCacheIsolatedByEffectiveProvider),
    ("Translation circuit is isolated by effective provider", TestTranslationCircuitIsolatedByEffectiveProvider),
    ("Translation cache evicts oldest entries", TestTranslationCacheEvictsOldestEntries),
    ("Translation cache flushes deferred entries", TestTranslationCacheFlushesDeferredEntries),
    ("Translation cache persists off the live path", TestTranslationCachePersistsOffLivePath),
    ("Application logs rotate and expire", TestApplicationLogMaintenance),
    ("High-frequency application logs are throttled", TestApplicationLogThrottling),
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
    ("Translation cooldown does not publish source as translated", TestTranslationCooldownDoesNotPublishSource),
    ("Chat retries after a transient batch failure", TestChatRetriesAfterTransientFailure),
    ("Malformed batch response is not cached", TestMalformedBatchResponseIsNotCached),
    ("Cancellation does not trip translation circuit", TestCancellationDoesNotTripTranslationCircuit),
    ("Stopping screen translation does not publish canceled text", TestScreenStopDoesNotPublishCanceledText),
    ("Translation HTTP timeout is suitable for real-time use", TestTranslationHttpTimeout),
    ("Translation connection test uses a cross-language probe", TestTranslationConnectionProbe),
    ("Translation connection test rejects empty results", TestTranslationConnectionRejectsEmptyResult),
    ("DeepL selects Free and Pro endpoints from the key", TestDeepLEndpointSelection),
    ("DeepL normalizes source variants and omits unsupported sources", TestDeepLSourceLanguageHandling),
    ("DeepL unsupported OCR languages fall back to Google", TestDeepLUnsupportedSourceFallback),
    ("Google batch translation posts long text once", TestGoogleBatchTranslationUsesPost),
    ("Google Web App fallback is bounded and ordered", TestGoogleWebAppFallbackConcurrency),
    ("Spaced-language screen segment keeps phrases", TestSpacedLanguageScreenSegmentKeepsPhrases),
    ("CJK screen segment separates OCR chunks", TestCjkScreenSegmentSeparatesOcrChunks),
    ("Supported OCR scripts pass screen filter", TestSupportedOcrScriptsPassScreenFilter),
    ("Chinese ratio bypasses screen filter", TestChineseRatioBypassesScreenFilter),
    ("Chinese ratio bypasses chat filter", TestChineseRatioBypassesChatFilter),
    ("English-only screen lines are hidden", TestEnglishOnlyScreenLinesAreHidden),
    ("English game text is translated", TestEnglishGameTextIsTranslated),
    ("Screen translation forwards game language", TestScreenTranslationForwardsGameLanguage),
    ("Chat translation forwards game language", TestChatTranslationForwardsGameLanguage),
    ("Same translation language is rejected", TestSameTranslationLanguageIsRejected),
    ("Screen overlay keeps individual OCR positions", TestScreenOverlayKeepsIndividualOcrPositions),
    ("Screen overlay reuses unchanged visual tree", TestScreenOverlayReusesUnchangedVisualTree),
    ("Overlay expiration uses one shared schedule", TestOverlayExpirationSchedule),
    ("Multiple include regions filter OCR lines", TestMultipleIncludeRegionsFilterOcrLines),
    ("Foreground capture accepts only target window", TestForegroundCaptureAcceptsOnlyTargetWindow),
    ("Capture reads the target window device context", TestCaptureUsesTargetWindowDeviceContext),
    ("Capture buffer reuse requires the same source and size", TestCaptureBufferReuseRequirements),
    ("Overlay capture never hides the overlay", TestOverlayCaptureNeverHidesOverlay),
    ("Overlay topmost promotion is non-activating", TestOverlayTopmostPromotionIsNonActivating),
    ("Overlay tracks target z-order changes", TestOverlayTracksTargetZOrderChanges),
    ("Broadcast option maps only capture affinity", TestBroadcastOptionMapsCaptureAffinity),
    ("Deferred capture resumes without error", TestDeferredCaptureResumesWithoutError),
    ("Chat translation keeps OCR position", TestChatTranslationKeepsOcrPosition),
    ("Chat snapshot replays translation at current position", TestChatSnapshotReplaysTranslationAtCurrentPosition),
    ("Similar OCR chat reuses translation at moved position", TestSimilarOcrChatReusesTranslationAtMovedPosition),
    ("Similar aliases keep both snapshot occurrences", TestSimilarAliasesKeepBothSnapshotOccurrences),
    ("Chat replacement preserves snapshot identity", TestChatReplacementPreservesSnapshotIdentity),
    ("Chat batch failure keeps cached snapshot rows", TestChatBatchFailureKeepsCachedSnapshotRows),
    ("Duplicate chat positions remain in snapshot", TestDuplicateChatPositionsRemainInSnapshot),
    ("Concatenated chat divides OCR position", TestConcatenatedChatDividesOcrPosition),
    ("Chat translation ignores duplicate names outside chat rows", TestChatTranslationIgnoresDuplicateNamesOutsideChatRows),
    ("Chat translation does not use a speaker-only fallback position", TestChatTranslationDoesNotUseSpeakerOnlyFallbackPosition),
    ("Chat overlay keeps OCR row positions", TestChatOverlayKeepsOcrRowPositions),
    ("Chat overlay does not move dense rows", TestChatOverlayDoesNotMoveDenseRows),
    ("Overlapping chat translations keep OCR positions", TestOverlappingChatTranslationsKeepOcrPositions),
    ("Overlapping backgrounds keep one opacity", TestOverlappingBackgroundsKeepOneOpacity)
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

    Assert(translation.BatchRequests == 1, $"Expected one batched chat translation request, got {translation.BatchRequests}.");
    Assert(translation.LastBatchTexts.SequenceEqual([firstMessage, secondMessage]), "Translation requests should contain messages only.");
    Assert(translation.LastBatchTexts.All(text => !text.Contains("sToRy", StringComparison.Ordinal)), "Embedded speaker must not be sent for translation.");

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
            new UserDictEntry("hello", "こんにちは", UserDictionaryStore.UserCategory, "en", "ja"),
            new UserDictEntry("go", "start", UserDictionaryStore.QuickReplyCategory)
        };

        store.Save(expected);
        var loaded = store.Load();
        Assert(File.Exists(store.DictionaryPath), "Expected user_dictionary.csv to be written.");
        Assert(
            loaded.Any(entry =>
                entry.Source == "hello"
                && entry.Target == "こんにちは"
                && entry.SourceLanguage == "en"
                && entry.TargetLanguage == "ja"),
            "CSV language-pair entry was not loaded.");
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

    Assert(translation.BatchRequests == 0, "Dictionary chat should not call API.");
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

    Assert(translation.BatchRequests == 0, "Rejected chat should not call API.");
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

static async Task TestTranslationCacheIsolatedByEffectiveProvider()
{
    var directory = Path.Combine(Path.GetTempPath(), $"game-overlay-translator-provider-cache-{Guid.NewGuid():N}");
    var cachePath = Path.Combine(directory, "cache.json");
    Directory.CreateDirectory(directory);

    try
    {
        var request = new TranslationRequest("provider-specific-result", "ko", "en");
        var legacySource = new CountingTranslationService();
        var legacyCache = new CachingTranslationService(
            legacySource,
            new ScreenTranslationCacheStore(cachePath),
            cacheSaveInterval: TimeSpan.Zero);
        await legacyCache.TranslateAsync(request, CancellationToken.None);
        legacyCache.FlushCache();

        var provider = "DeepL";
        var source = new CountingTranslationService();
        var cached = new CachingTranslationService(
            source,
            new ScreenTranslationCacheStore(cachePath),
            cacheSaveInterval: TimeSpan.Zero,
            cacheNamespaceProvider: (_, _) => provider);

        await cached.TranslateAsync(request, CancellationToken.None);
        await cached.TranslateAsync(request, CancellationToken.None);
        Assert(source.SingleRequests == 1, "The same provider should reuse its own cache entry.");

        provider = "GoogleUnofficial";
        await cached.TranslateAsync(request, CancellationToken.None);
        Assert(source.SingleRequests == 2, "Changing the effective provider must bypass the previous provider's cache.");

        provider = "DeepL";
        await cached.TranslateAsync(request, CancellationToken.None);
        Assert(source.SingleRequests == 2, "Switching back should reuse the original provider's scoped cache.");
        cached.FlushCache();
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestTranslationCircuitIsolatedByEffectiveProvider()
{
    var directory = Path.Combine(Path.GetTempPath(), $"game-overlay-translator-provider-circuit-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var provider = "DeepL";
        var source = new SwitchableTranslationService { ShouldFail = true };
        var cached = new CachingTranslationService(
            source,
            new ScreenTranslationCacheStore(Path.Combine(directory, "cache.json")),
            cacheNamespaceProvider: (_, _) => provider);

        for (var index = 0; index < 3; index++)
        {
            try
            {
                await cached.TranslateAsync(
                    new TranslationRequest($"provider-failure-{index}-{Guid.NewGuid():N}", "ko", "en"),
                    CancellationToken.None);
                Assert(false, "The failing provider should throw.");
            }
            catch (InvalidOperationException)
            {
            }
        }

        provider = "GoogleUnofficial";
        source.ShouldFail = false;
        var result = await cached.TranslateAsync(
            new TranslationRequest($"provider-recovery-{Guid.NewGuid():N}", "ko", "en"),
            CancellationToken.None);

        Assert(
            result.TranslatedText.StartsWith("translated:", StringComparison.Ordinal),
            "A healthy provider should work immediately after another provider opens its circuit.");
        cached.FlushCache();
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestDictionaryIsScopedToChineseKorean()
{
    var translation = new CountingTranslationService();
    var source = Chinese("4f60 597d 4e16 754c");
    var ocr = new OcrResult(source, [new OcrLineResult(source, new Rect(0, 0, 120, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
    var options = CreateOptions(
        TranslationMode.Screen,
        [new UserDictEntry(source, "한국어 사전 결과", UserDictionaryStore.UserCategory)]) with
    {
        TargetLanguage = new TranslationLanguage("en-US", "English")
    };

    await RunSession(session, options);

    Assert(translation.BatchRequests == 1, "A Chinese-Korean dictionary entry must not bypass an English translation request.");
    Assert(translation.LastTargetLanguage == "en-US", "The selected non-Korean target language should reach the provider.");
}

static async Task TestDictionarySupportsAdditionalLanguagePairs()
{
    var translation = new CountingTranslationService();
    var source = "hello world";
    var ocr = new OcrResult(source, [new OcrLineResult(source, new Rect(0, 0, 120, 24))]);
    var session = new TranslationSession(new FakeCaptureService(), new FakeOcrEngine(ocr), translation);
    var options = CreateOptions(
        TranslationMode.Screen,
        [new UserDictEntry(source, "こんにちは世界", UserDictionaryStore.UserCategory, "en", "ja")]) with
    {
        OcrLanguage = new OcrLanguage("en", "English"),
        TargetLanguage = new TranslationLanguage("ja", "Japanese")
    };

    await RunSession(session, options);

    Assert(translation.BatchRequests == 0, "A matching English-Japanese dictionary entry should bypass the provider.");
}

static Task TestResultWindowUsesSelectedGameLanguage()
{
    Assert(
        ResultWindow.CreateChatSendHint("일본어").Contains("일본어", StringComparison.Ordinal),
        "The quick-chat hint should name the selected game language.");
    Assert(
        ResultWindow.CreateChatSendProgress("영어").Contains("영어", StringComparison.Ordinal),
        "The quick-chat progress should name the selected game language.");
    return Task.CompletedTask;
}

static Task TestAppSettingsFlushesLatestValue()
{
    var directory = Path.Combine(Path.GetTempPath(), "GameOverlayTranslatorTests", Guid.NewGuid().ToString("N"));
    var settingsPath = Path.Combine(directory, "settings.json");
    try
    {
        using (var store = new AppSettingsStore(settingsPath, TimeSpan.FromHours(1)))
        {
            store.Save(new AppSettings(FontSize: 18));
            store.Save(new AppSettings(FontSize: 31, OverlayDurationSeconds: 2.7));
            Assert(!File.Exists(settingsPath), "Debounced settings should wait for the save window or explicit flush.");
            store.Flush();
        }

        using var reloadedStore = new AppSettingsStore(settingsPath, TimeSpan.Zero);
        var reloaded = reloadedStore.Load();
        Assert(reloaded.FontSize == 31, "Flush should persist the latest font size.");
        Assert(reloaded.OverlayDurationSeconds == 2.7, "Flush should persist the latest overlay duration.");
        Assert(!File.Exists($"{settingsPath}.tmp"), "Atomic settings save should not leave a temporary file.");
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

static Task TestLegacyUserDictionaryCsvMigration()
{
    var directory = Path.Combine(Path.GetTempPath(), "GameOverlayTranslatorTests", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "user_dictionary.csv"),
            "Source,Target,Category\nlegacy,레거시,사용자\n");

        var loaded = new UserDictionaryStore(directory).Load();
        var legacy = loaded.Single(entry => entry.Source == "legacy");
        Assert(legacy.SourceLanguage == "zh-Hans", "Legacy CSV source language should migrate to Simplified Chinese.");
        Assert(legacy.TargetLanguage == "ko", "Legacy CSV target language should migrate to Korean.");
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

static Task TestTranslationTargetCatalog()
{
    var codes = LanguageCatalog.TargetLanguages
        .Select(language => language.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(codes.Contains("ko"), "Korean target language is missing.");
    Assert(codes.Contains("en-US"), "English target language is missing.");
    Assert(codes.Contains("zh-Hans"), "Simplified Chinese target language is missing.");
    Assert(codes.Contains("zh-Hant"), "Traditional Chinese target language is missing.");
    Assert(codes.Contains("ja"), "Japanese target language is missing.");
    Assert(codes.Count == LanguageCatalog.TargetLanguages.Count, "Target language codes must be unique.");
    Assert(
        !TranslationTextNormalizer.AreSameLanguage(
            new OcrLanguage("zh-Hans", "Simplified Chinese"),
            new TranslationLanguage("zh-Hant", "Traditional Chinese")),
        "Simplified-to-Traditional Chinese should remain selectable.");
    return Task.CompletedTask;
}

static Task TestOcrModelCatalog()
{
    var tags = LanguageCatalog.OcrLanguages
        .Select(language => language.Tag)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(tags.Contains("zh-Hans"), "Simplified Chinese OCR model is missing.");
    Assert(tags.Contains("en"), "English OCR model is missing.");
    Assert(tags.Contains("ja"), "Japanese OCR model is missing.");
    Assert(tags.Contains("ko"), "Korean OCR model is missing.");
    Assert(tags.Contains("zh-Hant"), "Traditional Chinese OCR model is missing.");
    Assert(tags.Contains("de"), "Latin-script OCR languages are missing.");
    Assert(tags.Contains("ru"), "Cyrillic OCR languages are missing.");
    Assert(tags.Count >= 20, "OCR language catalog should expose the available multilingual models.");
    Assert(tags.Count == LanguageCatalog.OcrLanguages.Count, "OCR model language tags must be unique.");

    var modelKeys = LanguageCatalog.OcrModelPackages
        .Select(model => model.Key)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(modelKeys.Contains("latin"), "The Latin-script download option is missing.");
    Assert(modelKeys.Contains("cyrillic"), "The Cyrillic download option is missing.");
    Assert(modelKeys.Count == LanguageCatalog.OcrModelPackages.Count, "OCR download model keys must be unique.");
    Assert(
        LanguageCatalog.OcrLanguages.All(language => modelKeys.Contains(PaddleOcrEngine.GetModelKey(language.Tag))),
        "Every game language must map to a downloadable OCR model package.");

    var installedOption = new OcrModelInstallOption(LanguageCatalog.OcrModelPackages[0], true);
    var downloadableOption = new OcrModelInstallOption(LanguageCatalog.OcrModelPackages[1], false);
    Assert(installedOption.ToString().Contains("설치됨"), "Installed model options must identify their state.");
    Assert(downloadableOption.ToString().Contains("다운로드 가능"), "Missing model options must identify their state.");
    return Task.CompletedTask;
}

static Task TestInstalledOcrLanguageDetection()
{
    var directory = Path.Combine(Path.GetTempPath(), "GameOverlayTranslatorTests", Guid.NewGuid().ToString("N"));
    try
    {
        foreach (var modelDirectory in new[] { "ch_PP-OCRv4_det", "ch_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls" })
        {
            var modelPath = Path.Combine(directory, modelDirectory);
            Directory.CreateDirectory(modelPath);
            File.WriteAllText(Path.Combine(modelPath, "inference.pdmodel"), "model");
            File.WriteAllText(Path.Combine(modelPath, "inference.pdiparams"), "parameters");
        }

        Assert(
            PaddleOcrEngine.IsModelAvailable(new OcrLanguage("zh-Hans", "Simplified Chinese"), directory),
            "A complete cached OCR model should appear in the installed game-language list.");
        Assert(
            !PaddleOcrEngine.IsModelAvailable(new OcrLanguage("ja", "Japanese"), directory),
            "A language without its recognition model must not appear as installed.");

        foreach (var modelDirectory in new[] { "ml_PP-OCRv3_det", "latin_PP-OCRv3_rec" })
        {
            var modelPath = Path.Combine(directory, modelDirectory);
            Directory.CreateDirectory(modelPath);
            File.WriteAllText(Path.Combine(modelPath, "inference.pdmodel"), "model");
            File.WriteAllText(Path.Combine(modelPath, "inference.pdiparams"), "parameters");
        }

        Assert(
            PaddleOcrEngine.IsModelAvailable(new OcrLanguage("de", "German"), directory),
            "A downloaded Latin model should install German.");
        Assert(
            PaddleOcrEngine.IsModelAvailable(new OcrLanguage("fr", "French"), directory),
            "One Latin model should expose every supported Latin-script game language.");
        Assert(
            !PaddleOcrEngine.IsModelAvailable(new OcrLanguage("ru", "Russian"), directory),
            "The Latin model must not mark the Cyrillic model as installed.");
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

static async Task TestTranslationCacheEvictsOldestEntries()
{
    var translation = new CountingTranslationService();
    var cached = new CachingTranslationService(translation, new ScreenTranslationCacheStore(), maxCacheEntries: 2);

    await cached.TranslateAsync(new TranslationRequest("cache-first", "ko", "en"), CancellationToken.None);
    await cached.TranslateAsync(new TranslationRequest("cache-second", "ko", "en"), CancellationToken.None);
    await cached.TranslateAsync(new TranslationRequest("cache-third", "ko", "en"), CancellationToken.None);
    await cached.TranslateAsync(new TranslationRequest("cache-first", "ko", "en"), CancellationToken.None);

    Assert(translation.SingleRequests == 4, "The oldest entry should be evicted after the cache reaches its limit.");
}

static async Task TestTranslationCacheFlushesDeferredEntries()
{
    var directory = Path.Combine(Path.GetTempPath(), $"game-overlay-translator-cache-{Guid.NewGuid():N}");
    var cachePath = Path.Combine(directory, "cache.json");
    Directory.CreateDirectory(directory);

    try
    {
        var source = new CountingTranslationService();
        var cached = new CachingTranslationService(
            source,
            new ScreenTranslationCacheStore(cachePath),
            cacheSaveInterval: TimeSpan.FromHours(1));

        await cached.TranslateAsync(new TranslationRequest("persist-first", "ko", "en"), CancellationToken.None);
        await cached.TranslateAsync(new TranslationRequest("persist-second", "ko", "en"), CancellationToken.None);

        var beforeFlush = new ScreenTranslationCacheStore(cachePath).Load();
        Assert(
            !beforeFlush.Values.Contains("translated:persist-second", StringComparer.Ordinal),
            "The deferred entry should not be persisted before flush.");

        cached.FlushCache();

        var afterFlush = new ScreenTranslationCacheStore(cachePath).Load();
        Assert(
            afterFlush.Values.Contains("translated:persist-second", StringComparer.Ordinal),
            "Flush should persist the latest deferred cache entry.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task TestTranslationCachePersistsOffLivePath()
{
    var directory = Path.Combine(Path.GetTempPath(), $"game-overlay-translator-async-cache-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    using var store = new BlockingCacheStore(Path.Combine(directory, "cache.json"));
    var cached = new CachingTranslationService(
        new CountingTranslationService(),
        store,
        cacheSaveInterval: TimeSpan.Zero);
    Task<TranslationResult>? translationTask = null;

    try
    {
        translationTask = Task.Run(() => cached.TranslateAsync(
            new TranslationRequest($"async-cache-{Guid.NewGuid():N}", "ko", "en"),
            CancellationToken.None));

        Assert(store.SaveStarted.Wait(TimeSpan.FromSeconds(1)), "The background cache save should start.");
        var completed = await Task.WhenAny(translationTask, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert(
            ReferenceEquals(completed, translationTask),
            "A blocked disk write must not delay the live translation result.");
    }
    finally
    {
        store.AllowSave.Set();
        if (translationTask is not null)
        {
            await translationTask;
        }
        cached.FlushCache();
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestApplicationLogMaintenance()
{
    var directory = Path.Combine(Path.GetTempPath(), $"game-overlay-translator-log-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);

    try
    {
        var currentLog = Path.Combine(directory, "current.log");
        File.WriteAllText(currentLog, "1234567890");
        AppLog.RotateIfNeeded(currentLog, incomingBytes: 1, maxBytes: 10);

        var rotatedLog = Path.Combine(directory, "current.previous.log");
        Assert(!File.Exists(currentLog), "The full current log should be rotated.");
        Assert(File.Exists(rotatedLog), "The rotated log should be retained.");

        var expiredLog = Path.Combine(directory, "expired.log");
        var recentLog = Path.Combine(directory, "recent.log");
        File.WriteAllText(expiredLog, "expired");
        File.WriteAllText(recentLog, "recent");
        File.SetLastWriteTimeUtc(expiredLog, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(recentLog, DateTime.UtcNow);

        AppLog.DeleteExpiredLogs(directory, DateTime.UtcNow.AddDays(-14));

        Assert(!File.Exists(expiredLog), "Expired logs should be deleted.");
        Assert(File.Exists(recentLog), "Recent logs should be retained.");
        return Task.CompletedTask;
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static Task TestApplicationLogThrottling()
{
    var key = $"regression-{Guid.NewGuid():N}";
    var start = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
    Assert(
        AppLog.ShouldWriteThrottled(key, start, TimeSpan.FromSeconds(30)),
        "The first high-frequency diagnostic should be written.");
    Assert(
        !AppLog.ShouldWriteThrottled(key, start.AddSeconds(29), TimeSpan.FromSeconds(30)),
        "Repeated diagnostics inside the throttle interval should not touch the log file.");
    Assert(
        AppLog.ShouldWriteThrottled(key, start.AddSeconds(30), TimeSpan.FromSeconds(30)),
        "A diagnostic should be writable again after the throttle interval.");
    return Task.CompletedTask;
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

    Assert(translation.BatchRequests == 1, "Expected one chat request for non-excluded text.");
    Assert(translation.LastBatchTexts.Count == 1, "Excluded chat line should not be sent for translation.");
    Assert(translation.LastBatchTexts[0] == translated, "Only the non-excluded chat message should be translated.");
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

    Assert(translation.BatchRequests == 1, "A small edge overlap should not remove the chat OCR line.");
    Assert(translation.LastBatchTexts[0] == translated, "The chat message should still be translated.");
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

    Assert(translation.BatchRequests == 1, "First chat line should be translated once.");
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

    try
    {
        await cached.TranslateAsync(request, CancellationToken.None);
        Assert(false, "Failure cooldown should defer instead of returning source text as a translation.");
    }
    catch (TranslationTemporarilyUnavailableException)
    {
    }
    Assert(failing.CallCount == 1, "Cooldown should avoid second provider call.");
}

static async Task TestTranslationCooldownDoesNotPublishSource()
{
    var source = Chinese("4e34 65f6 7f51 7edc 5931 8d25");
    var failing = new FailingTranslationService();
    var cached = new CachingTranslationService(failing, new ScreenTranslationCacheStore());
    var session = ScreenSession(source, cached);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen), 110);

    Assert(failing.CallCount == 1, "The cooldown should suppress repeated provider calls.");
    Assert(
        updates.All(update => update.ScreenItems is not { Count: > 0 }),
        "A failed translation must not be published as a raw source overlay.");
    Assert(
        updates.Any(update => update.FilterRule == "TranslationCooldown" && !update.IsError),
        "Cooldown polls should report an automatic non-error retry state.");
}

static async Task TestChatRetriesAfterTransientFailure()
{
    var message = Chinese("4e00 6b21 5931 8d25 540e 91cd 8bd5");
    var sourceLine = $"racer: {message}";
    var translation = new FailOnceThenSucceedBatchTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(),
        new FakeOcrEngine(new OcrResult(sourceLine, [new OcrLineResult(sourceLine, new Rect(0, 0, 180, 24))])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    Assert(translation.BatchRequests >= 2, "A transiently failed chat line should be sent again.");
    Assert(
        updates.Any(update => update.IsChatLine
                              && update.DiagnosticKind == DiagnosticKind.OcrTranslated
                              && update.TranslatedText == $"translated:{message}"),
        "The retried chat line should eventually publish its translation.");
}

static async Task TestMalformedBatchResponseIsNotCached()
{
    var malformed = new MalformedBatchTranslationService();
    var cached = new CachingTranslationService(malformed, new ScreenTranslationCacheStore());
    var uniquePrefix = $"malformed-{Guid.NewGuid():N}";
    var request = new BatchTranslationRequest(
        [$"{uniquePrefix}-first", $"{uniquePrefix}-second"],
        "ko",
        "en");

    try
    {
        await cached.TranslateBatchAsync(request, CancellationToken.None);
        Assert(false, "A partial batch response must fail instead of caching source text.");
    }
    catch (InvalidOperationException)
    {
    }

    Assert(malformed.BatchRequests == 1, "The malformed provider should be called once.");

    var succeeding = new CountingTranslationService();
    var reloaded = new CachingTranslationService(succeeding, new ScreenTranslationCacheStore());
    var result = await reloaded.TranslateBatchAsync(request, CancellationToken.None);
    Assert(succeeding.BatchRequests == 1, "A malformed response must not be persisted as a cache hit.");
    Assert(
        result.TranslatedTexts.All(text => text.StartsWith("translated:", StringComparison.Ordinal)),
        "The retry should return real translations.");
}

static async Task TestCancellationDoesNotTripTranslationCircuit()
{
    var service = new CancellationAwareTranslationService();
    var cached = new CachingTranslationService(service, new ScreenTranslationCacheStore());

    for (var index = 0; index < 3; index++)
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await cached.TranslateAsync(
                new TranslationRequest($"cancel-{Guid.NewGuid():N}", "ko", "en"),
                canceled.Token);
            Assert(false, "Canceled translation should propagate cancellation.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    var source = $"after-cancel-{Guid.NewGuid():N}";
    var result = await cached.TranslateAsync(
        new TranslationRequest(source, "ko", "en"),
        CancellationToken.None);
    Assert(service.SuccessfulRequests == 1, "Canceled requests must not open the translation circuit.");
    Assert(result.TranslatedText == $"translated:{source}", "Translation should resume immediately after cancellation.");
}

static async Task TestScreenStopDoesNotPublishCanceledText()
{
    var source = Chinese("505c 6b62 540e 4e0d 5e94 663e 793a");
    var translation = new BlockingBatchTranslationService();
    var session = ScreenSession(source, translation);
    var updates = Collect(session);

    await session.StartAsync(CreateOptions(TranslationMode.Screen), CancellationToken.None);
    await translation.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await session.StopAsync();

    Assert(
        !updates.Any(update => update.ScreenItems is { Count: > 0 }),
        "Stopping during an API request must not publish raw or stale screen text.");
}

static Task TestTranslationHttpTimeout()
{
    var field = typeof(MainWindow).GetField(
        "httpClient",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    var client = field?.GetValue(null) as HttpClient;

    Assert(client is not null, "The shared translation HTTP client should exist.");
    Assert(client!.Timeout <= TimeSpan.FromSeconds(15), "Translation requests should not freeze a live game overlay for the default 100-second timeout.");
    Assert(client.Timeout >= TimeSpan.FromSeconds(5), "Translation timeout should still allow normal network latency.");
    return Task.CompletedTask;
}

static async Task TestTranslationConnectionProbe()
{
    var service = new CountingTranslationService();
    var result = await TranslationConnectionTester.TestAsync(
        service,
        new TranslationLanguage("en-US", "English"),
        CancellationToken.None);

    Assert(service.LastSingleTexts.SequenceEqual(["연결 테스트"]), "An English target should use a Korean source probe.");
    Assert(service.LastTargetLanguage == "en-US", "The connection probe should use the selected target language.");
    Assert(result.TranslatedText.StartsWith("translated:", StringComparison.Ordinal), "The connection test should return the provider result.");
}

static async Task TestTranslationConnectionRejectsEmptyResult()
{
    try
    {
        await TranslationConnectionTester.TestAsync(
            new EmptyTranslationService(),
            new TranslationLanguage("ko", "Korean"),
            CancellationToken.None);
        Assert(false, "An empty provider response should fail the connection test.");
    }
    catch (InvalidOperationException ex)
    {
        Assert(ex.Message.Contains("빈 결과", StringComparison.Ordinal), "The empty-result error should explain the provider response.");
    }
}

static async Task TestDeepLEndpointSelection()
{
    var handler = new DeepLEndpointHandler();
    using var client = new HttpClient(handler);
    var authKey = " free-key:fx ";
    var service = new DeepLTranslationService(client, () => authKey);

    await service.TranslateAsync(
        new TranslationRequest("hello", "ko", "en"),
        CancellationToken.None);
    authKey = "pro-key";
    await service.TranslateBatchAsync(
        new BatchTranslationRequest(["world"], "ko", "en"),
        CancellationToken.None);

    Assert(handler.RequestUris.Count == 2, "Both DeepL requests should reach the HTTP handler.");
    Assert(handler.RequestUris[0].Host == "api-free.deepl.com", "A :fx key should use the DeepL Free endpoint.");
    Assert(handler.RequestUris[1].Host == "api.deepl.com", "A non-:fx key should use the DeepL Pro endpoint.");
    Assert(
        handler.AuthorizationParameters.SequenceEqual(["free-key:fx", "pro-key"]),
        "DeepL authentication keys should be trimmed and sent in the authorization header.");
    Assert(
        handler.RequestBodies.All(body => body.Contains("source_lang=EN", StringComparison.Ordinal)),
        "Supported source languages should be sent explicitly to DeepL.");
}

static async Task TestDeepLSourceLanguageHandling()
{
    var handler = new DeepLEndpointHandler();
    using var client = new HttpClient(handler);
    var service = new DeepLTranslationService(client, () => "test-key");

    await service.TranslateAsync(
        new TranslationRequest("नमस्ते", "ko", "hi"),
        CancellationToken.None);
    await service.TranslateBatchAsync(
        new BatchTranslationRequest(["測試"], "ko", "zh-Hant"),
        CancellationToken.None);

    Assert(
        !handler.RequestBodies[0].Contains("source_lang=", StringComparison.Ordinal),
        "An unsupported DeepL source language should be omitted instead of causing a bad request.");
    Assert(
        handler.RequestBodies[1].Contains("source_lang=ZH", StringComparison.Ordinal),
        "Chinese script variants should use DeepL's supported ZH source code.");
}

static async Task TestDeepLUnsupportedSourceFallback()
{
    Assert(
        TranslationServiceDelegator.ResolveEffectiveTranslator(TranslationServiceType.DeepL, "en", "ko")
        == TranslationServiceType.DeepL,
        "A DeepL-supported source language should keep the selected provider.");
    Assert(
        TranslationServiceDelegator.ResolveEffectiveTranslator(TranslationServiceType.DeepL, "hi", "ko")
        == TranslationServiceType.GoogleUnofficial,
        "A DeepL-unsupported OCR language should automatically use the broad-language fallback.");
    Assert(
        TranslationServiceDelegator.ResolveEffectiveTranslator(TranslationServiceType.DeepL, "ko", "hi")
        == TranslationServiceType.GoogleUnofficial,
        "Reverse chat translation to a DeepL-unsupported game language should use the fallback.");
    Assert(
        TranslationServiceDelegator.ResolveEffectiveTranslator(TranslationServiceType.DeepL, "ko", "en")
        == TranslationServiceType.DeepL,
        "Generic English game chat should map to DeepL's default English target variant.");
    Assert(
        TranslationServiceDelegator.ResolveEffectiveTranslator(TranslationServiceType.GoogleWebApp, "hi", "ko")
        == TranslationServiceType.GoogleWebApp,
        "Explicitly selected non-DeepL providers must not be changed.");

    var handler = new ProviderRoutingHandler();
    using var client = new HttpClient(handler);
    var settings = new AppSettings(TranslatorType: TranslationServiceType.DeepL);
    var service = new TranslationServiceDelegator(client, () => "test-key", () => settings);

    await service.TranslateAsync(
        new TranslationRequest("नमस्ते", "ko", "hi"),
        CancellationToken.None);
    await service.TranslateAsync(
        new TranslationRequest("안녕하세요", "hi", "ko"),
        CancellationToken.None);
    await service.TranslateAsync(
        new TranslationRequest("안녕하세요", "en", "ko"),
        CancellationToken.None);
    await service.TranslateBatchAsync(
        new BatchTranslationRequest(["hello"], "ko", "en"),
        CancellationToken.None);

    Assert(
        handler.RequestHosts.SequenceEqual(
            ["translate.googleapis.com", "translate.googleapis.com", "api.deepl.com", "api.deepl.com"]),
        "Provider routing should fall back for unsupported source or target languages and retain DeepL otherwise.");
}

static async Task TestGoogleBatchTranslationUsesPost()
{
    var handler = new EchoGoogleTranslationHandler();
    using var client = new HttpClient(handler);
    var service = new GoogleUnofficialTranslationService(client);
    var texts = new[] { "hello world", new string('x', 12_000) };

    var result = await service.TranslateBatchAsync(
        new BatchTranslationRequest(texts, "ko", "en"),
        CancellationToken.None);

    Assert(handler.RequestCount == 1, "A delimiter-preserving Google batch should use one outbound request.");
    Assert(handler.LastMethod == HttpMethod.Post, "Long game-screen text should be sent in a POST body instead of the URL.");
    Assert(string.IsNullOrEmpty(handler.LastRequestUri?.Query), "The translation text must not be placed in the request URL.");
    Assert(handler.LastBody?.Length > texts[1].Length, "The POST body should contain the complete long translation batch.");
    Assert(result.TranslatedTexts.SequenceEqual(texts), "The packed Google batch should split back into its original rows.");
}

static async Task TestGoogleWebAppFallbackConcurrency()
{
    var handler = new LegacyGoogleWebAppHandler();
    using var client = new HttpClient(handler);
    var service = new GoogleWebAppTranslationService(client, () => "https://example.test/translate");
    var texts = Enumerable.Range(0, 8).Select(index => $"line-{index}").ToArray();

    var result = await service.TranslateBatchAsync(
        new BatchTranslationRequest(texts, "ko", "en"),
        CancellationToken.None);

    Assert(handler.BatchRequests == 1, "The Web App batch endpoint should be attempted once.");
    Assert(handler.SingleRequests == texts.Length, "A legacy Web App should receive one fallback request per text.");
    Assert(handler.MaxConcurrentSingles > 1, "Legacy fallback requests should overlap network waits.");
    Assert(handler.MaxConcurrentSingles <= 4, "Legacy fallback concurrency must remain bounded.");
    Assert(
        result.TranslatedTexts.SequenceEqual(texts.Select(text => $"translated:{text}")),
        "Concurrent fallback results must preserve the original OCR row order.");
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

static Task TestPaddleOcrBitmapConversion()
{
    var pixels = new byte[]
    {
        10, 20, 30, 255,
        40, 50, 60, 255
    };
    var bitmap = BitmapSource.Create(
        2,
        1,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        pixels,
        8);

    using var mat = PaddleOcrEngine.BitmapSourceToMat(bitmap);
    Assert(mat.Width == 2 && mat.Height == 1, "Converted OCR image dimensions changed.");
    Assert(mat.Type() == CvMatType.CV_8UC3, $"Expected a 3-channel BGR matrix, got {mat.Type()}.");

    var first = mat.At<CvVec3b>(0, 0);
    var second = mat.At<CvVec3b>(0, 1);
    Assert(first.Item0 == 10 && first.Item1 == 20 && first.Item2 == 30, "First BGR pixel changed during conversion.");
    Assert(second.Item0 == 40 && second.Item1 == 50 && second.Item2 == 60, "Second BGR pixel changed during conversion.");

    var bgr32Bitmap = BitmapSource.Create(
        2,
        1,
        96,
        96,
        PixelFormats.Bgr32,
        null,
        pixels,
        8);
    using var bgr32Mat = PaddleOcrEngine.BitmapSourceToMat(bgr32Bitmap);
    Assert(
        bgr32Mat.At<CvVec3b>(0, 0).Equals(first) && bgr32Mat.At<CvVec3b>(0, 1).Equals(second),
        "A native GDI Bgr32 capture should preserve colors without an intermediate format conversion.");
    return Task.CompletedTask;
}

static Task TestPaddleOcrFrameCache()
{
    using var cache = new OcrFrameCache();
    var originalBitmap = BitmapSource.Create(
        2,
        2,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        new byte[16],
        8);
    using var original = PaddleOcrEngine.CaptureBitmapPixels(originalBitmap);
    var expected = new OcrResult("cached", []);

    Assert(!cache.TryGet(original, "en", out _), "An empty frame cache must miss.");
    cache.Store(original, "en", expected);

    using var identical = PaddleOcrEngine.CaptureBitmapPixels(originalBitmap);
    Assert(cache.TryGet(identical, "en", out var reused), "An identical frame should reuse its OCR result.");
    Assert(ReferenceEquals(reused, expected), "The cached OCR result instance should be returned unchanged.");

    var changedBitmap = BitmapSource.Create(
        2,
        2,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        8);
    using var changed = PaddleOcrEngine.CaptureBitmapPixels(changedBitmap);
    Assert(!cache.TryGet(changed, "en", out _), "Any pixel change must invalidate the OCR result.");
    Assert(!cache.TryGet(identical, "ja", out _), "A language change must invalidate the OCR result.");
    return Task.CompletedTask;
}

static Task TestPaddleOcrPixelMasks()
{
    var pixels = Enumerable.Repeat(new byte[] { 10, 20, 30, 255 }, 8)
        .SelectMany(pixel => pixel)
        .ToArray();
    var bitmap = BitmapSource.Create(
        4,
        2,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        pixels,
        16);
    using var frame = PaddleOcrEngine.CaptureBitmapPixels(bitmap);
    frame.ApplyMasks(
        [new Rect(1, 0, 2, 2)],
        [new Rect(2, 1, 1, 1)]);

    static bool IsBlack(OcrFramePixels frame, int x, int y)
    {
        var offset = y * frame.Stride + x * 4;
        return frame.Buffer.AsSpan(offset, 4).SequenceEqual(stackalloc byte[4]);
    }

    Assert(IsBlack(frame, 0, 0) && IsBlack(frame, 3, 1), "Pixels outside include regions must be black.");
    Assert(!IsBlack(frame, 1, 0) && !IsBlack(frame, 2, 0), "Pixels inside include regions must be preserved.");
    Assert(IsBlack(frame, 2, 1), "Excluded regions must override included regions.");
    return Task.CompletedTask;
}

static Task TestSlowOcrPollingYieldsToGame()
{
    var normalDelay = TranslationSession.CalculateNextPollDelay(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(240));
    Assert(
        normalDelay == TimeSpan.FromMilliseconds(760),
        "A fast OCR pass should preserve the configured start-to-start polling interval.");

    var overrunDelay = TranslationSession.CalculateNextPollDelay(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(1250));
    Assert(
        overrunDelay >= TimeSpan.FromMilliseconds(100),
        "An OCR pass that exceeds its interval must yield CPU time before the next capture.");
    return Task.CompletedTask;
}

static Task TestSessionStatusTracksRecovery()
{
    var tracker = new SessionStatusTracker();
    var started = tracker.Observe(new SessionUpdate(TranslationSession.RunningStatus));
    Assert(started?.Text == TranslationSession.RunningStatus, "Starting a session should show a stable running status.");

    var routineSkip = tracker.Observe(new SessionUpdate(
        "스킵",
        FilterRule: "NoText",
        DiagnosticKind: DiagnosticKind.OcrSkipped));
    Assert(routineSkip is null, "Routine OCR skips must not replace the global running status.");

    var cooldown = tracker.Observe(new SessionUpdate(
        "최근 번역 실패로 잠시 대기 중입니다. 자동으로 다시 시도합니다.",
        FilterRule: "TranslationCooldown",
        DiagnosticKind: DiagnosticKind.OcrSkipped));
    Assert(cooldown?.Text.Contains("자동", StringComparison.Ordinal) == true, "A retry cooldown should remain visible.");

    var recovered = tracker.Observe(new SessionUpdate(
        "스킵",
        FilterRule: "NoText",
        DiagnosticKind: DiagnosticKind.OcrSkipped));
    Assert(recovered?.Text == TranslationSession.RunningStatus, "The first successful poll after a transient state should report recovery.");
    Assert(
        tracker.Observe(new SessionUpdate("스킵", FilterRule: "NoText", DiagnosticKind: DiagnosticKind.OcrSkipped)) is null,
        "Later routine skips should stay silent after recovery.");

    var error = tracker.Observe(new SessionUpdate("API 오류", IsError: true));
    Assert(error is { IsError: true }, "A real session error must remain visible with error styling.");
    var translated = tracker.Observe(new SessionUpdate("번역", DiagnosticKind: DiagnosticKind.OcrTranslated));
    Assert(translated?.Text == TranslationSession.RunningStatus, "A translated poll should clear the previous error state.");
    return Task.CompletedTask;
}

static Task TestSessionUpdateBuffer()
{
    var buffer = new SessionUpdateBuffer();
    var first = new SessionUpdate("first");
    var second = new SessionUpdate("second");

    Assert(buffer.Enqueue(first), "The first pending update should schedule one UI dispatch.");
    Assert(!buffer.Enqueue(second), "Additional pending updates should reuse the scheduled UI dispatch.");
    var drained = buffer.Drain();
    Assert(drained.SequenceEqual([first, second]), "Buffered updates must preserve publication order.");
    Assert(buffer.Enqueue(new SessionUpdate("third")), "A new batch should schedule another UI dispatch after draining.");
    Assert(buffer.Drain().Single().Status == "third", "The next update batch should remain independent.");
    Assert(buffer.Drain().Count == 0, "Draining an empty buffer should be harmless.");

    var chatLine = new SessionUpdate("line", IsChatLine: true, TranslatedText: "translated");
    var firstSnapshot = new SessionUpdate("snapshot-1", ChatItems: []);
    var latestSnapshot = new SessionUpdate("snapshot-2", ChatItems: []);
    Assert(
        ReferenceEquals(
            SessionUpdateBuffer.SelectLatestOverlayUpdate(
                [chatLine, firstSnapshot, latestSnapshot],
                TranslationMode.Chat),
            latestSnapshot),
        "A chat overlay batch should render only its latest complete snapshot.");

    var firstScreen = new SessionUpdate("screen-1", ScreenItems: []);
    var latestScreen = new SessionUpdate("screen-2", ScreenItems: []);
    Assert(
        ReferenceEquals(
            SessionUpdateBuffer.SelectLatestOverlayUpdate(
                [firstScreen, new SessionUpdate("skip"), latestScreen],
                TranslationMode.Screen),
            latestScreen),
        "A screen overlay batch should render only its latest screen state.");
    return Task.CompletedTask;
}

static async Task TestTranslationSessionRunsOcrOffCallerContext()
{
    var previousContext = SynchronizationContext.Current;
    var callerContext = new SynchronizationContext();
    var ocr = new ContextRecordingOcrEngine();
    var session = new TranslationSession(
        new FakeCaptureService(),
        ocr,
        new CountingTranslationService());

    try
    {
        SynchronizationContext.SetSynchronizationContext(callerContext);
        _ = session.StartAsync(CreateOptions(TranslationMode.Screen), CancellationToken.None);
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    try
    {
        await ocr.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    finally
    {
        await session.StopAsync();
    }

    Assert(
        !ReferenceEquals(ocr.ObservedContext, callerContext),
        "OCR inference must not run on the UI caller synchronization context.");
}

static Task TestSpacedLanguageScreenSegmentKeepsPhrases()
{
    var segments = ScreenTranslationSegmenter.Split("hello world test", new OcrLanguage("en", "English"));
    Assert(segments.Count == 1, $"Expected one phrase segment, got {segments.Count}");
    Assert(segments[0].Text == "hello world test", $"Expected the full English phrase, got '{segments[0].Text}'");
    return Task.CompletedTask;
}

static Task TestCjkScreenSegmentSeparatesOcrChunks()
{
    var segments = ScreenTranslationSegmenter.Split(
        "こんにちは 世界",
        new OcrLanguage("ja", "Japanese"));
    Assert(segments.Count == 2, $"Expected two CJK OCR chunks, got {segments.Count}");
    return Task.CompletedTask;
}

static Task TestSupportedOcrScriptsPassScreenFilter()
{
    var samples = new[]
    {
        (new OcrLanguage("en", "English"), "START GAME"),
        (new OcrLanguage("ko", "Korean"), "게임 시작"),
        (new OcrLanguage("ar", "Arabic"), "ابدأ اللعبة"),
        (new OcrLanguage("hi", "Hindi"), "खेल शुरू"),
        (new OcrLanguage("ta", "Tamil"), "விளையாட்டு தொடங்கு"),
        (new OcrLanguage("te", "Telugu"), "ఆట ప్రారంభించండి"),
        (new OcrLanguage("kn", "Kannada"), "ಆಟ ಪ್ರಾರಂಭಿಸಿ")
    };

    foreach (var (language, text) in samples)
    {
        var segment = ScreenTranslationSegmenter.Split(text, language).Single();
        Assert(ScreenTranslationSegmenter.ShouldSendToTranslation(segment, language), $"{language.DisplayName} text should pass the screen translation filter.");
    }
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

    Assert(translation.BatchRequests == 1, "Should bypass quality filter and request translation for Chinese chat line.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated), "Expected translated diagnostic.");
}

static Task TestPaddleOcrIsOnlyEngine()
{
    Assert(Enum.GetValues<OcrEngineType>().SequenceEqual([OcrEngineType.PaddleOCR]), "PaddleOCR must be the only selectable OCR engine.");
    Assert(new AppSettings().OcrEngineType == OcrEngineType.PaddleOCR, "New settings must default to PaddleOCR.");
    Assert(typeof(AppSettingsStore).Assembly.GetType("GameOverlayTranslator.App.Services.WindowsOcrEngine") is null, "Windows OCR implementation must not ship.");
    return Task.CompletedTask;
}

static async Task TestEnglishOnlyScreenLinesAreHidden()
{
    var translation = new CountingTranslationService();
    var ocr = new OcrResult("RANKING\n123",
    [
        new OcrLineResult("RANKING", new Rect(10, 10, 100, 20)),
        new OcrLineResult("123", new Rect(10, 32, 40, 20))
    ]);
    var session = new TranslationSession(new FakeCaptureService(200, 100), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen) with { SuppressEnglishOnlyScreenLines = true });

    Assert(translation.BatchRequests == 0, "English-only screen text should not call translation.");
    Assert(!updates.Any(update => update.ScreenItems is { Count: > 0 }), "English-only screen text should not be shown.");
    Assert(updates.Any(update => update.FilterRule == "EnglishOnly" && update.ScreenItems is { Count: 0 }), "English-only OCR should clear an existing overlay.");
}

static async Task TestEnglishGameTextIsTranslated()
{
    var translation = new CountingTranslationService();
    var session = ScreenSession("START GAME", translation);
    var updates = Collect(session);
    var options = CreateOptions(TranslationMode.Screen) with
    {
        OcrLanguage = new OcrLanguage("en", "English"),
        SuppressEnglishOnlyScreenLines = true
    };

    await RunSession(session, options);

    Assert(translation.BatchRequests == 1, "English source language must not be suppressed as English-only UI noise.");
    Assert(updates.Any(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated), "Expected an English screen translation update.");
}

static async Task TestScreenTranslationForwardsGameLanguage()
{
    var translation = new CountingTranslationService();
    var session = ScreenSession("START GAME", translation);
    var options = CreateOptions(TranslationMode.Screen) with
    {
        OcrLanguage = new OcrLanguage("en", "English")
    };

    await RunSession(session, options);

    Assert(translation.LastBatchSourceLanguage == "en", "Screen translation must send the selected game language to the provider.");
}

static async Task TestChatTranslationForwardsGameLanguage()
{
    var translation = new CountingTranslationService();
    var source = "こんにちは世界";
    var session = new TranslationSession(
        new FakeCaptureService(),
        new FakeOcrEngine(new OcrResult($"racer: {source}", [])),
        translation);
    var options = CreateOptions(TranslationMode.Chat) with
    {
        OcrLanguage = new OcrLanguage("ja", "Japanese")
    };

    await RunSession(session, options);

    Assert(translation.LastBatchSourceLanguage == "ja", "Chat translation must send the selected game language to the provider.");
}

static Task TestSameTranslationLanguageIsRejected()
{
    Assert(
        TranslationTextNormalizer.AreSameLanguage(
            new OcrLanguage("ko", "Korean"),
            new TranslationLanguage("ko-KR", "Korean")),
        "Language variants with the same primary language should be treated as identical.");
    Assert(
        !TranslationTextNormalizer.AreSameLanguage(
            new OcrLanguage("ja", "Japanese"),
            new TranslationLanguage("ko", "Korean")),
        "Different source and target languages should remain valid.");
    return Task.CompletedTask;
}

static Task TestScreenOverlayKeepsIndividualOcrPositions()
{
    var firstBounds = new Rect(12, 40, 160, 24);
    var secondBounds = new Rect(18, 44, 150, 24);
    var rendered = OverlayWindow.BuildScreenRenderItems(
    [
        new ScreenTranslationItem("first", "첫 번째", firstBounds),
        new ScreenTranslationItem("second", "두 번째", secondBounds)
    ],
    dpiScale: 1);

    Assert(rendered.Count == 2, "Nearby or overlapping OCR lines must not be merged into one overlay box.");
    Assert(rendered[0].Bounds == firstBounds, "The first screen translation must use its own OCR rectangle.");
    Assert(rendered[1].Bounds == secondBounds, "The second screen translation must use its own OCR rectangle.");
    return Task.CompletedTask;
}

static Task TestScreenOverlayReusesUnchangedVisualTree()
{
    var items = new[]
    {
        new OverlayWindow.ScreenRenderItem("first", new Rect(10, 20, 100, 24)),
        new OverlayWindow.ScreenRenderItem("second", new Rect(10, 50, 120, 24))
    };
    var sameItems = items.Select(item => item with { }).ToArray();
    var style = new OverlayWindow.ScreenRenderStyle(
        "Test Font",
        25,
        "#FFFFFFFF",
        "#FF000000",
        0.5,
        "#99000000",
        1280,
        720);

    Assert(
        OverlayWindow.CanReuseScreenRender(items, sameItems, style, style with { }),
        "Identical screen items and style should reuse the existing visual tree.");
    Assert(
        !OverlayWindow.CanReuseScreenRender(
            items,
            sameItems,
            style,
            style with { FontSize = 26 }),
        "A style change must force a screen visual rebuild.");
    Assert(
        !OverlayWindow.CanReuseScreenRender(
            items,
            [sameItems[0], sameItems[1] with { Bounds = new Rect(11, 50, 120, 24) }],
            style,
            style),
        "A position change must force a screen visual rebuild.");
    return Task.CompletedTask;
}

static Task TestOverlayExpirationSchedule()
{
    var now = new DateTimeOffset(2026, 7, 24, 0, 0, 10, TimeSpan.Zero);
    IReadOnlyDictionary<string, DateTimeOffset> expirations = new Dictionary<string, DateTimeOffset>
    {
        ["expired"] = now.AddMilliseconds(-1),
        ["boundary"] = now,
        ["active"] = now.AddMilliseconds(100)
    };
    var expired = new List<string>();
    OverlayWindow.CollectExpiredChatIds(expirations, now, expired);
    Assert(
        expired.Order().SequenceEqual(["boundary", "expired"]),
        "The shared overlay timer should remove only rows whose display duration has elapsed.");

    var instanceFields = typeof(OverlayWindow).GetFields(
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert(
        instanceFields.Count(field => field.FieldType == typeof(System.Windows.Threading.DispatcherTimer)) == 1,
        "Chat and screen overlay expiration should share one dispatcher timer.");
    Assert(
        instanceFields.All(field => field.FieldType != typeof(CancellationTokenSource)),
        "Overlay rows should not allocate one cancellation timer per OCR refresh.");
    return Task.CompletedTask;
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

static Task TestForegroundCaptureAcceptsOnlyTargetWindow()
{
    Assert(WindowCaptureService.IsTargetForeground((nint)42, (nint)42), "The selected game should be capturable while it is foreground.");
    Assert(!WindowCaptureService.IsTargetForeground((nint)42, (nint)84), "A covering foreground window must defer desktop capture.");
    Assert(!WindowCaptureService.IsTargetForeground(nint.Zero, nint.Zero), "A missing target window must never be considered foreground.");
    return Task.CompletedTask;
}

static Task TestCaptureUsesTargetWindowDeviceContext()
{
    var targetHandle = (nint)42;
    Assert(
        WindowCaptureService.ResolveCaptureSourceHandle(targetHandle) == targetHandle,
        "OCR capture must read the selected game HWND rather than the desktop HWND.");
    Assert(
        WindowCaptureService.ResolveCaptureSourceHandle(targetHandle) != nint.Zero,
        "OCR capture must not fall back to the desktop device context.");
    Assert(
        WindowCaptureService.IsGdiSelectionFailure(nint.Zero),
        "A null SelectObject result must stop capture cleanly.");
    Assert(
        WindowCaptureService.IsGdiSelectionFailure(NativeMethods.HgdiError),
        "HGDI_ERROR from SelectObject must stop capture cleanly.");
    Assert(
        !WindowCaptureService.IsGdiSelectionFailure((nint)1),
        "A valid previous GDI object must allow capture to continue.");
    return Task.CompletedTask;
}

static Task TestCaptureBufferReuseRequirements()
{
    var source = new nint(101);
    Assert(
        WindowCaptureService.CanReuseCaptureBuffer(source, 1280, 720, source, 1280, 720),
        "An unchanged target and crop size should reuse the GDI capture buffer.");
    Assert(
        !WindowCaptureService.CanReuseCaptureBuffer(source, 1280, 720, source, 1024, 768),
        "A capture size change must recreate the GDI bitmap.");
    Assert(
        !WindowCaptureService.CanReuseCaptureBuffer(source, 1280, 720, new nint(202), 1280, 720),
        "A target window change must recreate the compatible bitmap.");
    Assert(
        !WindowCaptureService.CanReuseCaptureBuffer(nint.Zero, 1280, 720, source, 1280, 720),
        "An uninitialized capture buffer must not be treated as reusable.");

    var disposed = new WindowCaptureService();
    disposed.Dispose();
    disposed.Dispose();
    try
    {
        _ = disposed.CaptureAsync(
            new CaptureTarget(new CapturableWindow(source, "Disposed", "Test")),
            new CaptureRegion(0, 0, 1, 1),
            CancellationToken.None);
        Assert(false, "A disposed capture service should reject further captures.");
    }
    catch (ObjectDisposedException)
    {
    }

    return Task.CompletedTask;
}

static Task TestOverlayCaptureNeverHidesOverlay()
{
    Assert(
        typeof(TranslationSession).GetProperty("BeforeCaptureAsync") is null,
        "Translation capture must not expose a pre-capture overlay hiding callback.");
    Assert(
        typeof(TranslationSession).GetProperty("AfterCaptureAsync") is null,
        "Translation capture must not expose a post-capture overlay showing callback.");
    Assert(
        typeof(OverlayWindow).GetMethod("SetCaptureVisibility") is null,
        "The overlay must remain visible throughout every OCR capture.");
    return Task.CompletedTask;
}

static Task TestOverlayTopmostPromotionIsNonActivating()
{
    const uint showWindow = 0x0040;
    const uint hideWindow = 0x0080;
    var flags = OverlayWindow.StableTopmostFlags;

    Assert(
        OverlayWindow.StableTopmostInsertAfter == NativeMethods.HwndTopmost,
        "Overlay promotion must use the topmost z-order band.");
    Assert((flags & NativeMethods.SwpNoMove) != 0, "Topmost promotion must not move the overlay.");
    Assert((flags & NativeMethods.SwpNoSize) != 0, "Topmost promotion must not resize the overlay.");
    Assert((flags & NativeMethods.SwpNoActivate) != 0, "Topmost promotion must not steal game focus.");
    Assert((flags & (showWindow | hideWindow)) == 0, "Topmost promotion must not change overlay visibility.");
    return Task.CompletedTask;
}

static Task TestOverlayTracksTargetZOrderChanges()
{
    var target = (nint)42;
    Assert(
        OverlayWindow.HasCompleteTargetTracking((nint)1, (nint)2),
        "Both foreground and reorder hooks are required for complete z-order tracking.");
    Assert(
        !OverlayWindow.HasCompleteTargetTracking((nint)1, nint.Zero),
        "A partial hook installation must be retried rather than treated as complete.");
    Assert(
        OverlayWindow.ReorderHookProcessId == 0,
        "Top-level z-order changes must be observed globally because the parent window raises reorder events.");
    Assert(
        OverlayWindow.ShouldPromoteForTrackedEvent(
            NativeMethods.EventSystemForeground,
            target,
            target,
            target,
            overlayIsAboveTarget: true),
        "The overlay must be promoted as soon as the game becomes foreground.");
    Assert(
        !OverlayWindow.ShouldPromoteForTrackedEvent(
            NativeMethods.EventSystemForeground,
            target,
            target,
            (nint)99,
            overlayIsAboveTarget: false),
        "A stale foreground callback must not raise the overlay above a newer foreground app.");
    Assert(
        OverlayWindow.ShouldPromoteForTrackedEvent(
            NativeMethods.EventObjectReorder,
            (nint)84,
            target,
            target,
            overlayIsAboveTarget: false),
        "A parent/global reorder event must restore the overlay when the foreground game moved above it.");
    Assert(
        !OverlayWindow.ShouldPromoteForTrackedEvent(
            NativeMethods.EventObjectReorder,
            (nint)84,
            target,
            target,
            overlayIsAboveTarget: true),
        "A reorder event must not loop when the overlay is already above the game.");
    Assert(
        !OverlayWindow.ShouldPromoteForTrackedEvent(
            NativeMethods.EventObjectReorder,
            (nint)84,
            target,
            (nint)99,
            overlayIsAboveTarget: false),
        "A reorder event must not raise the overlay while another app is foreground.");
    Assert(
        !OverlayWindow.ShouldPromoteForTrackedEvent(
            0,
            target,
            target,
            target,
            overlayIsAboveTarget: false),
        "Unrelated game events must not trigger overlay promotion.");
    return Task.CompletedTask;
}

static Task TestBroadcastOptionMapsCaptureAffinity()
{
    Assert(
        OverlayWindow.CaptureAffinityFor(excludeFromCapture: false) == 0,
        "Broadcast-visible overlays must use WDA_NONE.");
    Assert(
        OverlayWindow.CaptureAffinityFor(excludeFromCapture: true) == NativeMethods.WDA_EXCLUDEFROMCAPTURE,
        "Private overlays must use WDA_EXCLUDEFROMCAPTURE.");
    return Task.CompletedTask;
}

static async Task TestDeferredCaptureResumesWithoutError()
{
    var source = Chinese("8bd1");
    var capture = new DeferredThenCaptureService();
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        capture,
        new FakeOcrEngine(new OcrResult(source, [new OcrLineResult(source, new Rect(0, 0, 20, 20))])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Screen), 100);

    var deferredIndex = updates.FindIndex(update => update.FilterRule == "CaptureDeferred");
    var translatedIndex = updates.FindIndex(update => update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(deferredIndex >= 0, "A covered game should publish a deferred capture status.");
    Assert(!updates[deferredIndex].IsError, "Deferred capture must not put the UI into an error state.");
    Assert(translatedIndex > deferredIndex, "Translation should resume automatically after the game becomes foreground.");
    Assert(capture.CallCount >= 2, "The session should retry capture on the next polling interval.");
    Assert(translation.BatchRequests == 1, "Only the successful capture should reach translation.");
}

static async Task TestChatSnapshotReplaysTranslationAtCurrentPosition()
{
    var message = Chinese("5feb 4f7f 7528 5929 4f7f");
    var sourceLine = $"racer: {message}";
    var firstPosition = new Rect(12, 34, 160, 24);
    var currentPosition = new Rect(16, 94, 168, 24);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(240, 160),
        new SequencedOcrEngine(
            new OcrResult(sourceLine, [new OcrLineResult(sourceLine, firstPosition)]),
            new OcrResult(sourceLine, [new OcrLineResult(sourceLine, currentPosition)])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    var snapshots = updates
        .Where(update => update.ChatItems is { Count: 1 })
        .Select(update => update.ChatItems![0])
        .ToList();
    Assert(translation.BatchRequests == 1, "A visible repeated chat line should call the API only once.");
    Assert(snapshots.Any(item => item.BoundingRect == firstPosition), "The first chat snapshot should use the first OCR position.");
    Assert(snapshots.Any(item => item.BoundingRect == currentPosition), "A replayed chat snapshot should use the current OCR position.");
    Assert(snapshots.All(item => item.TranslatedText == $"translated:{message}"), "Replayed snapshots should retain the translated text.");
}

static async Task TestSimilarOcrChatReusesTranslationAtMovedPosition()
{
    var message = Chinese("5feb 4f7f 7528 5929 4f7f");
    var jitteredMessage = $"{message}\uFF01";
    var firstSourceLine = $"racer: {message}";
    var jitteredSourceLine = $"racer: {jitteredMessage}";
    var firstPosition = new Rect(12, 30, 180, 24);
    var movedPosition = new Rect(18, 104, 184, 24);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(240, 180),
        new SequencedOcrEngine(
            new OcrResult(firstSourceLine, [new OcrLineResult(firstSourceLine, firstPosition)]),
            new OcrResult(jitteredSourceLine, [new OcrLineResult(jitteredSourceLine, movedPosition)])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    var snapshots = updates
        .Where(update => update.ChatItems is { Count: 1 })
        .Select(update => update.ChatItems![0])
        .ToList();
    Assert(translation.BatchRequests == 1, "A similar OCR form should reuse the existing translation without another API request.");
    Assert(snapshots.Any(item => item.BoundingRect == firstPosition), "The original OCR form should publish its first position.");
    Assert(snapshots.Any(item => item.BoundingRect == movedPosition), "The similar OCR form should refresh the snapshot at its moved position.");
    Assert(snapshots.Where(item => item.BoundingRect == movedPosition)
        .All(item => item.TranslatedText == $"translated:{message}"), "The moved similar form should reuse the remembered translation.");
    Assert(
        snapshots.First(item => item.BoundingRect == firstPosition).Id
        == snapshots.First(item => item.BoundingRect == movedPosition).Id,
        "OCR jitter should retain the stable logical snapshot id while moving the overlay.");
}

static async Task TestSimilarAliasesKeepBothSnapshotOccurrences()
{
    var message = Chinese("5feb 4f7f 7528 5929 4f7f");
    var jitteredMessage = $"{message}\uFF01";
    var sourceLine = $"racer: {message}";
    var jitteredSourceLine = $"racer: {jitteredMessage}";
    var firstPosition = new Rect(12, 30, 180, 24);
    var secondPosition = new Rect(12, 94, 184, 24);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(240, 180),
        new SequencedOcrEngine(
            new OcrResult(sourceLine, [new OcrLineResult(sourceLine, firstPosition)]),
            new OcrResult($"{sourceLine}\n{jitteredSourceLine}",
            [
                new OcrLineResult(sourceLine, firstPosition),
                new OcrLineResult(jitteredSourceLine, secondPosition)
            ])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    var snapshot = updates.Last(update => update.ChatItems is { Count: 2 }).ChatItems!;
    Assert(translation.BatchRequests == 1, "Similar OCR aliases should reuse the original translation request.");
    Assert(snapshot.Select(item => item.BoundingRect).SequenceEqual([firstPosition, secondPosition]), "Both similar OCR occurrences should retain their current positions.");
    Assert(snapshot[1].Id == $"{snapshot[0].Id}:1", "Aliases sharing one logical snapshot must receive unique base and base:1 ids.");
}

static async Task TestChatReplacementPreservesSnapshotIdentity()
{
    var originalMessage = Chinese("4f60 597d 4e16 754c 6d4b 8bd5 6587 672c 7532 4e59 4e19");
    var correctedMessage = originalMessage + Chinese("5929 4f7f");
    var originalLine = $"racer: {originalMessage}";
    var correctedLine = $"racer: {correctedMessage}";
    var firstPosition = new Rect(10, 24, 220, 24);
    var correctedPosition = new Rect(10, 84, 230, 24);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(280, 160),
        new SequencedOcrEngine(
            new OcrResult(originalLine, [new OcrLineResult(originalLine, firstPosition)]),
            new OcrResult(correctedLine, [new OcrLineResult(correctedLine, correctedPosition)])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    var snapshots = updates
        .Where(update => update.ChatItems is { Count: 1 })
        .Select(update => update.ChatItems![0])
        .ToList();
    var original = snapshots.First(item => item.BoundingRect == firstPosition);
    var replacement = snapshots.First(item => item.BoundingRect == correctedPosition);
    Assert(translation.BatchRequests == 2, "A longer corrected chat should be translated as a replacement.");
    Assert(original.Id == replacement.Id, "A replacement must preserve the logical snapshot id so the old overlay is updated in place.");
    Assert(updates.Any(update => update.IsChatLine && update.ReplacesChatLine), "The corrected chat should publish a replacement update.");
}

static async Task TestChatBatchFailureKeepsCachedSnapshotRows()
{
    var cachedMessage = Chinese("5feb 4f7f 7528 5929 4f7f");
    var newMessage = Chinese("4f60 597d 4e16 754c");
    var cachedLine = $"alpha: {cachedMessage}";
    var newLine = $"beta: {newMessage}";
    var firstPosition = new Rect(10, 20, 180, 24);
    var movedPosition = new Rect(10, 70, 180, 24);
    var newPosition = new Rect(10, 110, 180, 24);
    var translation = new SucceedOnceThenFailBatchTranslationService();
    var session = new TranslationSession(
        new FakeCaptureService(240, 180),
        new SequencedOcrEngine(
            new OcrResult(cachedLine, [new OcrLineResult(cachedLine, firstPosition)]),
            new OcrResult($"{cachedLine}\n{newLine}",
            [
                new OcrLineResult(cachedLine, movedPosition),
                new OcrLineResult(newLine, newPosition)
            ])),
        translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat), 110);

    Assert(translation.BatchRequests >= 2, "The second poll should exercise the failing batch path.");
    Assert(updates.Any(update => update.IsError), "The batch failure should still be surfaced as an error update.");
    Assert(
        updates.Any(update => update.ChatItems is { Count: 1 }
                              && update.ChatItems[0].BoundingRect == movedPosition
                              && update.ChatItems[0].TranslatedText == $"translated:{cachedMessage}"),
        "A failed batch must publish the previously cached visible row at its current OCR position.");
}

static async Task TestDuplicateChatPositionsRemainInSnapshot()
{
    var message = Chinese("4f60 597d 4e16 754c");
    var sourceLine = $"racer: {message}";
    var firstPosition = new Rect(10, 20, 180, 24);
    var secondPosition = new Rect(10, 120, 180, 24);
    var ocr = new OcrResult($"{sourceLine}\n{sourceLine}",
    [
        new OcrLineResult(sourceLine, firstPosition),
        new OcrLineResult(sourceLine, secondPosition)
    ]);
    var translation = new CountingTranslationService();
    var session = new TranslationSession(new FakeCaptureService(240, 180), new FakeOcrEngine(ocr), translation);
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var snapshot = updates.Last(update => update.ChatItems is { Count: 2 }).ChatItems!;
    Assert(translation.BatchRequests == 1, "Duplicate visible occurrences should share one translation request.");
    Assert(snapshot.Select(item => item.BoundingRect).SequenceEqual([firstPosition, secondPosition]), "Both OCR occurrences should remain in the poll snapshot.");
    Assert(snapshot.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 2, "Each visible occurrence should have a distinct snapshot id.");
}

static async Task TestConcatenatedChatDividesOcrPosition()
{
    var firstMessage = Chinese("4f60 597d 4e16 754c");
    var secondMessage = Chinese("5feb 4f7f 7528 5929 4f7f");
    var sourceLine = $"alpha: {firstMessage}beta: {secondMessage}";
    var sourcePosition = new Rect(10, 100, 240, 40);
    var session = new TranslationSession(
        new FakeCaptureService(300, 200),
        new FakeOcrEngine(new OcrResult(sourceLine, [new OcrLineResult(sourceLine, sourcePosition)])),
        new CountingTranslationService());
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = updates
        .Where(update => update.IsChatLine && update.DiagnosticKind == DiagnosticKind.OcrTranslated)
        .ToList();
    Assert(translated.Count == 2, "The concatenated OCR row should produce two translated chat lines.");
    Assert(translated[0].BoundingRect == new Rect(10, 100, 240, 20), "The first parsed chat should use the upper half of the OCR row.");
    Assert(translated[1].BoundingRect == new Rect(10, 120, 240, 20), "The second parsed chat should use the lower half of the OCR row.");
}

static async Task TestChatTranslationIgnoresDuplicateNamesOutsideChatRows()
{
    var message = Chinese("5feb 4f7f 7528 5929 4f7f");
    var expected = new Rect(24, 312, 260, 24);
    var ocr = new OcrResult($"zuyeong\n{message}\n[\u961f\u4f0d]zuyeong: {message}",
    [
        new OcrLineResult("zuyeong", new Rect(160, 38, 80, 20)),
        new OcrLineResult(message, new Rect(900, 270, 140, 24)),
        new OcrLineResult($"[\u961f\u4f0d]zuyeong: {message}", expected)
    ]);
    var session = new TranslationSession(new FakeCaptureService(1200, 700), new FakeOcrEngine(ocr), new CountingTranslationService());
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = updates.First(update => update.IsChatLine && update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.BoundingRect == expected, "Chat translation should use the parsed chat row, not a matching player name or speech bubble.");
}

static async Task TestChatTranslationDoesNotUseSpeakerOnlyFallbackPosition()
{
    var message = Chinese("5feb 4f7f 7528 5929 4f7f");
    var ocr = new OcrResult($"zuyeong: {message}",
    [
        new OcrLineResult("zuyeong", new Rect(160, 38, 80, 20))
    ]);
    var session = new TranslationSession(new FakeCaptureService(1200, 700), new FakeOcrEngine(ocr), new CountingTranslationService());
    var updates = Collect(session);

    await RunSession(session, CreateOptions(TranslationMode.Chat));

    var translated = updates.First(update => update.IsChatLine && update.DiagnosticKind == DiagnosticKind.OcrTranslated);
    Assert(translated.BoundingRect is null, "A speaker-only OCR row must not be used as a fallback translation position.");
}

static Task TestChatOverlayKeepsOcrRowPositions()
{
    var placed = OverlayLayout.PlaceChatAtOcrRows(
    [
        new OverlayChatItem("first", "first", 10, 80, 80, 480, 80, 30),
        new OverlayChatItem("second", "second", 160, 80, 80, 330, 80, 30)
    ]);

    Assert(placed[0].Top == 80, "First chat translation should keep its OCR top.");
    Assert(placed[1].Top == 80, "Horizontally separate rendered chat boxes should keep the same OCR top.");
    return Task.CompletedTask;
}

static Task TestChatOverlayDoesNotMoveDenseRows()
{
    var placed = OverlayLayout.PlaceChatAtOcrRows(
    [
        new OverlayChatItem("first", "first", 10, 80, 80, 480, 100, 30),
        new OverlayChatItem("second", "second", 60, 80, 80, 430, 100, 30)
    ]);

    Assert(placed[0].Top == 80 && placed[1].Top == 80, "Dense chat translations must stay on their OCR rows instead of being shifted.");
    return Task.CompletedTask;
}

static Task TestOverlappingChatTranslationsKeepOcrPositions()
{
    var placed = OverlayLayout.PlaceChatAtOcrRows(
    [
        new OverlayChatItem("first", "first", 10, 20, 80, 180, 160, 28),
        new OverlayChatItem("second", "second", 18, 24, 80, 180, 160, 28)
    ]);
    Assert(placed[0].Top == 20 && placed[1].Top == 24, "Overlapping chat translations must retain their individual OCR rows.");
    return Task.CompletedTask;
}

static Task TestOverlappingBackgroundsKeepOneOpacity()
{
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.DrawGeometry(
            new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            null,
            OverlayBackgroundGeometry.Create(
            [
                new Rect(2, 2, 20, 20),
                new Rect(12, 2, 20, 20)
            ],
            3));
    }

    var bitmap = new RenderTargetBitmap(40, 30, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var pixels = new byte[40 * 30 * 4];
    bitmap.CopyPixels(pixels, 40 * 4, 0);
    var singleAlpha = pixels[(10 * 40 + 6) * 4 + 3];
    var overlapAlpha = pixels[(10 * 40 + 16) * 4 + 3];
    Assert(singleAlpha > 0, "The background geometry should render its configured opacity.");
    Assert(Math.Abs(singleAlpha - overlapAlpha) <= 1, "An overlapping background must be composited once instead of becoming darker.");
    return Task.CompletedTask;
}

sealed class FakeCaptureService(int width = 1, int height = 1) : ICaptureService
{
    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        return Task.FromResult(new CapturedFrame(bitmap));
    }
}

sealed class DeferredThenCaptureService : ICaptureService
{
    public int CallCount { get; private set; }

    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        CallCount++;
        if (CallCount == 1)
        {
            throw new CaptureDeferredException("Waiting for the game window");
        }

        var bitmap = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
        return Task.FromResult(new CapturedFrame(bitmap));
    }
}

sealed class FakeOcrEngine(OcrResult result) : IOcrEngine
{
    public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct) =>
        Task.FromResult(result);
}

sealed class ContextRecordingOcrEngine : IOcrEngine
{
    public TaskCompletionSource<bool> Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SynchronizationContext? ObservedContext { get; private set; }

    public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        ObservedContext = SynchronizationContext.Current;
        Started.TrySetResult(true);
        return Task.FromResult(new OcrResult(string.Empty, []));
    }
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
    public string? LastBatchSourceLanguage { get; private set; }
    public string? LastTargetLanguage { get; private set; }

    private readonly List<string> singleTexts = [];

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        SingleRequests++;
        singleTexts.Add(request.Text);
        LastTargetLanguage = request.TargetLanguage;
        return Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", null));
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        LastBatchTexts = request.Texts.ToList();
        LastBatchSourceLanguage = request.SourceLanguage;
        LastTargetLanguage = request.TargetLanguage;
        return Task.FromResult(new BatchTranslationResult(request.Texts.Select(text => $"translated:{text}").ToList()));
    }
}

sealed class SucceedOnceThenFailBatchTranslationService : ITranslationService
{
    public int BatchRequests { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", null));

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        if (BatchRequests > 1)
        {
            throw new InvalidOperationException("Batch API Error Mock");
        }

        return Task.FromResult(new BatchTranslationResult(
            request.Texts.Select(text => $"translated:{text}").ToList()));
    }
}

sealed class FailOnceThenSucceedBatchTranslationService : ITranslationService
{
    public int BatchRequests { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", request.SourceLanguage));

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        if (BatchRequests == 1)
        {
            throw new InvalidOperationException("Transient batch failure");
        }

        return Task.FromResult(new BatchTranslationResult(
            request.Texts.Select(text => $"translated:{text}").ToArray()));
    }
}

sealed class MalformedBatchTranslationService : ITranslationService
{
    public int BatchRequests { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", request.SourceLanguage));

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        BatchRequests++;
        return Task.FromResult(new BatchTranslationResult(["only-one-result"]));
    }
}

sealed class CancellationAwareTranslationService : ITranslationService
{
    public int SuccessfulRequests { get; private set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SuccessfulRequests++;
        return Task.FromResult(new TranslationResult(
            request.Text,
            $"translated:{request.Text}",
            request.SourceLanguage));
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new BatchTranslationResult(
            request.Texts.Select(text => $"translated:{text}").ToList()));
    }
}

sealed class EmptyTranslationService : ITranslationService
{
    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, string.Empty, request.SourceLanguage));

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new BatchTranslationResult(request.Texts.Select(_ => string.Empty).ToArray()));
}

sealed class BlockingBatchTranslationService : ITranslationService
{
    public TaskCompletionSource<bool> Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct) =>
        Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", request.SourceLanguage));

    public async Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        Started.TrySetResult(true);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        throw new InvalidOperationException("Unreachable");
    }
}

sealed class EchoGoogleTranslationHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public HttpMethod? LastMethod { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastMethod = request.Method;
        LastRequestUri = request.RequestUri;
        LastBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var translatedText = LastBody
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Where(parts => Uri.UnescapeDataString(parts[0]) == "q")
            .Select(parts => Uri.UnescapeDataString(parts[1].Replace("+", " ", StringComparison.Ordinal)))
            .Single();
        var json = System.Text.Json.JsonSerializer.Serialize(
            new object?[]
            {
                new object?[] { new object?[] { translatedText } },
                null,
                "en"
            });

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
    }
}

sealed class DeepLEndpointHandler : HttpMessageHandler
{
    public List<Uri> RequestUris { get; } = [];
    public List<string> AuthorizationParameters { get; } = [];
    public List<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri ?? throw new InvalidOperationException("DeepL request URI is missing."));
        AuthorizationParameters.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
        RequestBodies.Add(await (request.Content?.ReadAsStringAsync(cancellationToken)
            ?? Task.FromResult(string.Empty)));
        const string json = """{"translations":[{"detected_source_language":"EN","text":"translated"}]}""";
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}

sealed class ProviderRoutingHandler : HttpMessageHandler
{
    public List<string> RequestHosts { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host
            ?? throw new InvalidOperationException("Translation request URI is missing.");
        RequestHosts.Add(host);
        var json = host == "translate.googleapis.com"
            ? """[[["translated","source",null,null]],null,"hi"]"""
            : """{"translations":[{"detected_source_language":"EN","text":"translated"}]}""";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
    }
}

sealed class LegacyGoogleWebAppHandler : HttpMessageHandler
{
    private int activeSingles;
    private int maxConcurrentSingles;
    private int batchRequests;
    private int singleRequests;

    public int BatchRequests => Volatile.Read(ref batchRequests);
    public int SingleRequests => Volatile.Read(ref singleRequests);
    public int MaxConcurrentSingles => Volatile.Read(ref maxConcurrentSingles);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var query = document.RootElement.GetProperty("q");
        if (query.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            Interlocked.Increment(ref batchRequests);
            return JsonResponse("{}");
        }

        Interlocked.Increment(ref singleRequests);
        var active = Interlocked.Increment(ref activeSingles);
        UpdateMaximum(ref maxConcurrentSingles, active);
        try
        {
            await Task.Delay(40, cancellationToken);
            var source = query.GetString() ?? string.Empty;
            var json = System.Text.Json.JsonSerializer.Serialize(new { translatedText = $"translated:{source}" });
            return JsonResponse(json);
        }
        finally
        {
            Interlocked.Decrement(ref activeSingles);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (observed == current)
            {
                return;
            }
            current = observed;
        }
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

sealed class SwitchableTranslationService : ITranslationService
{
    public bool ShouldFail { get; set; }

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("API Error Mock");
        }

        return Task.FromResult(new TranslationResult(request.Text, $"translated:{request.Text}", request.SourceLanguage));
    }

    public Task<BatchTranslationResult> TranslateBatchAsync(BatchTranslationRequest request, CancellationToken ct)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("API Error Mock");
        }

        return Task.FromResult(new BatchTranslationResult(
            request.Texts.Select(text => $"translated:{text}").ToArray()));
    }
}

sealed class BlockingCacheStore(string cachePath) : ITranslationCacheStore, IDisposable
{
    private readonly ScreenTranslationCacheStore inner = new(cachePath);

    public ManualResetEventSlim SaveStarted { get; } = new(false);
    public ManualResetEventSlim AllowSave { get; } = new(false);

    public Dictionary<string, string> Load() => inner.Load();

    public bool Save(IReadOnlyDictionary<string, string> cache)
    {
        SaveStarted.Set();
        AllowSave.Wait(TimeSpan.FromSeconds(5));
        return inner.Save(cache);
    }

    public void Dispose()
    {
        SaveStarted.Dispose();
        AllowSave.Dispose();
    }
}
