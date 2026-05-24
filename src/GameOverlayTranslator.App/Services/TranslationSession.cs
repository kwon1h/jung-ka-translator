using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App.Services;

public sealed class TranslationSession(ICaptureService captureService, IOcrEngine ocrEngine, ITranslationService translationService) : ITranslationSession
{
    private CancellationTokenSource? runCancellation;
    private Task? runTask;

    public event EventHandler<SessionUpdate>? Updated;
    public Func<CancellationToken, Task>? BeforeCaptureAsync { get; set; }
    public Func<CancellationToken, Task>? AfterCaptureAsync { get; set; }

    public bool IsRunning => runTask is { IsCompleted: false };

    public Task StartAsync(SessionOptions options, CancellationToken ct)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runTask = RunAsync(options, runCancellation.Token);
        Publish("Translation running");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (runCancellation is null)
        {
            return;
        }

        await runCancellation.CancelAsync();
        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        runCancellation.Dispose();
        runCancellation = null;
        runTask = null;
        Publish("Stopped");
    }

    private async Task RunAsync(SessionOptions options, CancellationToken ct)
    {
        var exactLines = new HashSet<string>(StringComparer.Ordinal);
        var recentChat = new RecentChatFilter();
        var screenTranslationMemory = new ScreenTranslationMemory();
        var totalTranslationRequests = 0;
        var totalTranslationCharacters = 0;

        var dictExactEntries = options.UserDictionary
            .Select(entry => (Entry: entry, NormalizedSource: NormalizeForMatching(entry.Source)))
            .ToList();

        var dictRegexes = options.UserDictionary
            .Select(entry => (Entry: entry, Regex: BuildFlexRegex(entry.Source)))
            .ToList();

        using var timer = new PeriodicTimer(options.Interval);

        do
        {
            try
            {
                if (BeforeCaptureAsync is not null)
                {
                    await BeforeCaptureAsync(ct);
                }

                CapturedFrame frame;
                try
                {
                    frame = await captureService.CaptureAsync(options.Target, options.Region, ct);
                }
                finally
                {
                    if (AfterCaptureAsync is not null)
                    {
                        await AfterCaptureAsync(ct);
                    }
                }

                var recognized = await ocrEngine.RecognizeAsync(frame, options.OcrLanguage, ct);

                if (options.Mode == TranslationMode.Screen)
                {
                    await HandleScreenTranslationAsync(
                        options,
                        recognized,
                        dictExactEntries,
                        dictRegexes,
                        screenTranslationMemory,
                        usage =>
                        {
                            totalTranslationRequests += usage.OutboundRequestCount;
                            totalTranslationCharacters += usage.OutboundCharacterCount;
                            return (totalTranslationRequests, totalTranslationCharacters);
                        },
                        ct);
                    continue;
                }

                await HandleChatTranslationAsync(
                    options,
                    recognized,
                    exactLines,
                    recentChat,
                    dictExactEntries,
                    dictRegexes,
                    usage =>
                    {
                        totalTranslationRequests += usage.OutboundRequestCount;
                        totalTranslationCharacters += usage.OutboundCharacterCount;
                        return (totalTranslationRequests, totalTranslationCharacters);
                    },
                    ct);
            }
            catch (CaptureException ex)
            {
                Publish(ex.Message, isError: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Publish(ex.Message, isError: true);
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task HandleScreenTranslationAsync(
        SessionOptions options,
        OcrResult recognized,
        IReadOnlyList<(UserDictEntry Entry, string NormalizedSource)> dictExactEntries,
        IReadOnlyList<(UserDictEntry Entry, Regex Regex)> dictRegexes,
        ScreenTranslationMemory screenTranslationMemory,
        Func<TranslationUsage, (int TotalRequests, int TotalCharacters)> addUsage,
        CancellationToken ct)
    {
        if (recognized.Lines.Count == 0)
        {
            Publish(
                "스킵",
                ocrRawText: recognized.Text,
                filterReason: "No OCR lines",
                filterRule: "NoText",
                diagnosticKind: DiagnosticKind.OcrSkipped);
            return;
        }

        var linePlans = new List<ScreenLinePlan>();
        var textsToTranslate = new List<string>();
        var textKeysToTranslate = new List<string>();
        var uniqueTextKeys = new HashSet<string>(StringComparer.Ordinal);
        var translationMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendingScreenUsage = TranslationUsage.None;
        var pendingScreenTotals = (TotalRequests: 0, TotalCharacters: 0);

        foreach (var line in recognized.Lines)
        {
            var segmentPlans = new List<ScreenSegmentPlan>();
            foreach (var segment in ScreenTranslationSegmenter.Split(line.Text, options.OcrLanguage))
            {
                var segmentText = segment.Text.Trim();
                var normalizedSegment = NormalizeForMatching(segmentText);
                var exactEntry = dictExactEntries
                    .FirstOrDefault(entry => string.Equals(entry.NormalizedSource, normalizedSegment, StringComparison.OrdinalIgnoreCase))
                    .Entry;

                if (exactEntry is not null)
                {
                    var canonicalExact = TranslationTextNormalizer.CanonicalizeCacheText(segmentText);
                    screenTranslationMemory.Remember(canonicalExact, segmentText, exactEntry.Target);
                    segmentPlans.Add(new ScreenSegmentPlan(segmentText, canonicalExact, exactEntry.Target));
                    continue;
                }

                var processed = segmentText;
                foreach (var (entry, regex) in dictRegexes)
                {
                    processed = regex.Replace(processed, entry.Target);
                }

                var canonical = TranslationTextNormalizer.CanonicalizeCacheText(processed);
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    segmentPlans.Add(new ScreenSegmentPlan(segmentText, canonical, processed));
                    continue;
                }

                if (screenTranslationMemory.TryGet(canonical, out var rememberedTranslation))
                {
                    translationMap[canonical] = rememberedTranslation;
                    segmentPlans.Add(new ScreenSegmentPlan(processed, canonical, rememberedTranslation));
                    continue;
                }

                var processedSegment = new ScreenTextSegment(processed, canonical);
                var shouldSend = ScreenTranslationSegmenter.ShouldSendToTranslation(processedSegment, options.OcrLanguage)
                                 || NeedsTranslationDueToChineseRatio(processed);
                if (!shouldSend)
                {
                    segmentPlans.Add(new ScreenSegmentPlan(processed, canonical, processed));
                    continue;
                }

                segmentPlans.Add(new ScreenSegmentPlan(processed, canonical, null));
                if (uniqueTextKeys.Add(canonical))
                {
                    textKeysToTranslate.Add(canonical);
                    textsToTranslate.Add(processed);
                }
            }

            if (segmentPlans.Count > 0)
            {
                linePlans.Add(new ScreenLinePlan(line, segmentPlans));
            }
        }

        if (textsToTranslate.Count > 0)
        {
            try
            {
                var batchCharacters = textsToTranslate.Sum(text => text.Length);
                var batchResult = await translationService.TranslateBatchAsync(
                    new BatchTranslationRequest(textsToTranslate, options.TargetLanguage.Code),
                    ct);

                for (var index = 0; index < textsToTranslate.Count; index++)
                {
                    var translatedText = index < batchResult.TranslatedTexts.Count
                        ? batchResult.TranslatedTexts[index]
                        : textsToTranslate[index];
                    var key = textKeysToTranslate[index];
                    translationMap[key] = translatedText;
                    screenTranslationMemory.Remember(key, textsToTranslate[index], translatedText);
                }

                var usage = batchResult.Usage ?? TranslationUsage.Outbound(1, batchCharacters);
                if (usage.OutboundRequestCount > 0 || usage.OutboundCharacterCount > 0)
                {
                    var totals = addUsage(usage);
                    AppLog.Write($"TranslationRequest mode=screen count={usage.OutboundRequestCount} chars={usage.OutboundCharacterCount}");
                    pendingScreenUsage = usage;
                    pendingScreenTotals = totals;
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Batch translation failed, falling back to raw texts", ex);
                for (var index = 0; index < textsToTranslate.Count; index++)
                {
                    translationMap[textKeysToTranslate[index]] = textsToTranslate[index];
                }
            }
        }
        var screenItems = new List<ScreenTranslationItem>();
        foreach (var linePlan in linePlans)
        {
            foreach (var segment in linePlan.Segments)
            {
                if (string.IsNullOrWhiteSpace(segment.TranslatedText)
                    && translationMap.TryGetValue(segment.CanonicalText, out var translatedSegment))
                {
                    segment.TranslatedText = translatedSegment;
                }
                else if (string.IsNullOrWhiteSpace(segment.TranslatedText))
                {
                    segment.TranslatedText = segment.SourceText;
                }
            }

            screenItems.Add(new ScreenTranslationItem(linePlan.OcrLine.Text, JoinTranslatedSegments(linePlan.Segments), linePlan.OcrLine.BoundingRect));
        }

        if (screenItems.Count > 0)
        {
            if (pendingScreenUsage.OutboundRequestCount > 0 || pendingScreenUsage.OutboundCharacterCount > 0)
            {
                Publish(
                    "번역",
                    ocrRawText: recognized.Text,
                    filterReason: $"Outbound {pendingScreenUsage.OutboundRequestCount} request(s) / {pendingScreenUsage.OutboundCharacterCount} chars",
                    filterRule: "Translated",
                    screenItems: screenItems,
                    translationRequestCount: pendingScreenUsage.OutboundRequestCount,
                    translationCharacterCount: pendingScreenUsage.OutboundCharacterCount,
                    totalTranslationRequestCount: pendingScreenTotals.TotalRequests,
                    totalTranslationCharacterCount: pendingScreenTotals.TotalCharacters,
                    diagnosticKind: DiagnosticKind.OcrTranslated);
            }
            else
            {
                Publish(
                    "스킵",
                    ocrRawText: recognized.Text,
                    filterReason: "Dictionary, cache, memory, or quality filters skipped translation API",
                    filterRule: "CacheHit",
                    screenItems: screenItems,
                    diagnosticKind: DiagnosticKind.OcrSkipped);
            }
        }
    }

    private async Task HandleChatTranslationAsync(
        SessionOptions options,
        OcrResult recognized,
        HashSet<string> exactLines,
        RecentChatFilter recentChat,
        IReadOnlyList<(UserDictEntry Entry, string NormalizedSource)> dictExactEntries,
        IReadOnlyList<(UserDictEntry Entry, Regex Regex)> dictRegexes,
        Func<TranslationUsage, (int TotalRequests, int TotalCharacters)> addUsage,
        CancellationToken ct)
    {
        var normalizedOcrText = NormalizeOcrTextForChat(recognized.Text, options.OcrLanguage);
        var chatLines = ChatLineParser.Parse(normalizedOcrText);
        AppLog.Write($"OCR chat poll raw={Quote(recognized.Text)} parsedLines={chatLines.Count}");
        if (chatLines.Count == 0)
        {
            Publish(
                "스킵",
                ocrRawText: recognized.Text,
                filterReason: string.IsNullOrWhiteSpace(recognized.Text) ? "No OCR text" : "Cannot parse speaker/message",
                filterRule: string.IsNullOrWhiteSpace(recognized.Text) ? "NoText" : "QualityFilter",
                diagnosticKind: DiagnosticKind.OcrSkipped);
            return;
        }

        foreach (var line in chatLines)
        {
            if (exactLines.Contains(line.DeduplicationKey))
            {
                Publish(
                    "스킵",
                    source: line.SourceLine,
                    filterReason: "Same line in session",
                    filterRule: "Duplicate",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                continue;
            }

            var normalizedMessage = NormalizeTextForTranslation(line.Message, options.OcrLanguage);
            var activeLine = new ChatLine(line.Speaker, normalizedMessage);

            var normalizedMsg = NormalizeForMatching(activeLine.Message.Trim());
            var exactEntry = dictExactEntries
                .FirstOrDefault(entry => string.Equals(entry.NormalizedSource, normalizedMsg, StringComparison.OrdinalIgnoreCase))
                .Entry;

            if (exactEntry is not null)
            {
                AppLog.Write($"UserDictionary exact match source={Quote(activeLine.Message)} target={Quote(exactEntry.Target)}");
                var decision = recentChat.Evaluate(new ChatLine(activeLine.Speaker, exactEntry.Target), options.Filter);
                if (decision.Action == ChatFilterAction.Skip)
                {
                    Publish(
                        "스킵",
                        line.SourceLine,
                        filterReason: $"Similarity {decision.SimilarityScore:F2}",
                        filterRule: "Duplicate",
                        diagnosticKind: DiagnosticKind.OcrSkipped);
                    continue;
                }

                Publish(
                    "스킵",
                    line.SourceLine,
                    exactEntry.Target,
                    speaker: activeLine.Speaker,
                    isChatLine: true,
                    chatLineId: decision.Id,
                    replacesChatLine: decision.Action == ChatFilterAction.Replace,
                    filterReason: "User dictionary exact match",
                    filterRule: "Dictionary",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                exactLines.Add(line.DeduplicationKey);
                continue;
            }

            var initialQuality = ChatQualityFilter.Check(activeLine, options.OcrLanguage, options.Filter);
            var bypassFilter = NeedsTranslationDueToChineseRatio(activeLine.Message);
            if (!initialQuality.Accepted && !bypassFilter)
            {
                AppLog.Write($"ChatQualityFilter reject reason={initialQuality.Reason} line={Quote(line.SourceLine)}");
                Publish(
                    "스킵",
                    line.SourceLine,
                    filterReason: initialQuality.Reason,
                    filterRule: "QualityFilter",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                continue;
            }

            var processedMessage = activeLine.Message;
            var replaced = false;
            foreach (var (entry, regex) in dictRegexes)
            {
                var before = processedMessage;
                processedMessage = regex.Replace(processedMessage, entry.Target);
                replaced |= processedMessage != before;
            }

            if (replaced)
            {
                AppLog.Write($"UserDictionary substring replace. Before={Quote(activeLine.Message)} After={Quote(processedMessage)}");
                activeLine = new ChatLine(activeLine.Speaker, processedMessage);
            }

            var quality = replaced ? ChatQualityFilter.Check(activeLine, options.OcrLanguage, options.Filter) : initialQuality;
            if (!quality.Accepted && !bypassFilter)
            {
                AppLog.Write($"ChatQualityFilter reject reason={quality.Reason} line={Quote(activeLine.SourceLine)}");
                Publish(
                    "스킵",
                    activeLine.SourceLine,
                    filterReason: quality.Reason,
                    filterRule: "QualityFilter",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                continue;
            }

            var decisionForLine = recentChat.Evaluate(activeLine, options.Filter);
            if (decisionForLine.Action == ChatFilterAction.Skip)
            {
                Publish(
                    "스킵",
                    activeLine.SourceLine,
                    filterReason: $"Similarity {decisionForLine.SimilarityScore:F2}",
                    filterRule: "Duplicate",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                continue;
            }

            if (!quality.TranslateWithService && !bypassFilter)
            {
                AppLog.Write($"ChatQualityFilter source-only reason={quality.Reason} line={Quote(activeLine.SourceLine)}");
                Publish(
                    "스킵",
                    activeLine.SourceLine,
                    activeLine.Message,
                    speaker: activeLine.Speaker,
                    isChatLine: true,
                    chatLineId: decisionForLine.Id,
                    replacesChatLine: decisionForLine.Action == ChatFilterAction.Replace,
                    filterReason: quality.Reason,
                    filterRule: "QualityFilter",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
                exactLines.Add(line.DeduplicationKey);
                continue;
            }

            var translated = await translationService.TranslateAsync(new TranslationRequest(activeLine.Message, options.TargetLanguage.Code), ct);
            var usage = translated.Usage ?? TranslationUsage.Outbound(1, activeLine.Message.Length);
            if (usage.OutboundRequestCount > 0 || usage.OutboundCharacterCount > 0)
            {
                var totals = addUsage(usage);
                AppLog.Write($"TranslationRequest mode=chat count={usage.OutboundRequestCount} chars={usage.OutboundCharacterCount} speaker={Quote(activeLine.Speaker)} message={Quote(activeLine.Message)}");
                Publish(
                    "번역",
                    activeLine.SourceLine,
                    translated.TranslatedText,
                    speaker: activeLine.Speaker,
                    isChatLine: true,
                    chatLineId: decisionForLine.Id,
                    replacesChatLine: decisionForLine.Action == ChatFilterAction.Replace,
                    filterReason: replaced ? "User dictionary replacement then translated" : "Translated",
                    filterRule: "Translated",
                    translationRequestCount: usage.OutboundRequestCount,
                    translationCharacterCount: usage.OutboundCharacterCount,
                    totalTranslationRequestCount: totals.TotalRequests,
                    totalTranslationCharacterCount: totals.TotalCharacters,
                    diagnosticKind: DiagnosticKind.OcrTranslated);
            }
            else
            {
                Publish(
                    "스킵",
                    activeLine.SourceLine,
                    translated.TranslatedText,
                    speaker: activeLine.Speaker,
                    isChatLine: true,
                    chatLineId: decisionForLine.Id,
                    replacesChatLine: decisionForLine.Action == ChatFilterAction.Replace,
                    filterReason: "Cache hit or translation bypass",
                    filterRule: "CacheHit",
                    diagnosticKind: DiagnosticKind.OcrSkipped);
            }
            exactLines.Add(line.DeduplicationKey);
        }
    }

    private static string JoinTranslatedSegments(IReadOnlyList<ScreenSegmentPlan> segments) =>
        segments.Count == 1
            ? segments[0].TranslatedText ?? segments[0].SourceText
            : string.Join(" ", segments.Select(segment => segment.TranslatedText ?? segment.SourceText)).Trim();

    private void Publish(
        string status,
        string? source = null,
        string? translated = null,
        bool isError = false,
        string? speaker = null,
        bool isChatLine = false,
        string? chatLineId = null,
        bool replacesChatLine = false,
        string? ocrRawText = null,
        string? filterReason = null,
        string? filterRule = null,
        IReadOnlyList<ScreenTranslationItem>? screenItems = null,
        int translationRequestCount = 0,
        int translationCharacterCount = 0,
        int totalTranslationRequestCount = 0,
        int totalTranslationCharacterCount = 0,
        DiagnosticKind diagnosticKind = DiagnosticKind.Other) =>
        Updated?.Invoke(this, new SessionUpdate(status, source, translated, isError, speaker, isChatLine, chatLineId, replacesChatLine, ocrRawText, filterReason, filterRule, screenItems, translationRequestCount, translationCharacterCount, totalTranslationRequestCount, totalTranslationCharacterCount, diagnosticKind));

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static string NormalizeForMatching(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character) || IsIgnoredPunctuation(character))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static bool IsIgnoredPunctuation(char character)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        return category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation
            or UnicodeCategory.MathSymbol
            or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol;
    }

    private static Regex BuildFlexRegex(string source)
    {
        var coreChars = new List<string>();
        foreach (var character in source.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character) || IsIgnoredPunctuation(character))
            {
                continue;
            }
            coreChars.Add(Regex.Escape(character.ToString()));
        }

        if (coreChars.Count == 0)
        {
            return new Regex(Regex.Escape(source), RegexOptions.IgnoreCase);
        }

        const string noiseClass = @"[\s\p{P}\p{S}]*";
        return new Regex(string.Join(noiseClass, coreChars), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeTextForTranslation(string text, OcrLanguage language) =>
        TranslationTextNormalizer.NormalizeForTranslation(text, language);

    private static string NormalizeOcrTextForChat(string text, OcrLanguage language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        return string.Join("\n", lines.Select(line => NormalizeTextForTranslation(line, language)));
    }

    private sealed record ScreenLinePlan(OcrLineResult OcrLine, IReadOnlyList<ScreenSegmentPlan> Segments);

    private sealed class ScreenSegmentPlan(string sourceText, string canonicalText, string? translatedText)
    {
        public string SourceText { get; } = sourceText;
        public string CanonicalText { get; } = canonicalText;
        public string? TranslatedText { get; set; } = translatedText;
    }

    private static bool IsKorean(char character)
    {
        return (character >= '\uAC00' && character <= '\uD7A3')
            || (character >= '\u3130' && character <= '\u318F')
            || (character >= '\u1100' && character <= '\u11FF');
    }

    private static bool IsChinese(char character)
    {
        return character is >= '\u3400' and <= '\u9FFF';
    }

    private static bool NeedsTranslationDueToChineseRatio(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int koreanCount = 0;
        int chineseCount = 0;

        foreach (var character in text)
        {
            if (IsKorean(character))
            {
                koreanCount++;
            }
            else if (IsChinese(character))
            {
                chineseCount++;
            }
        }

        int total = koreanCount + chineseCount;
        if (total == 0)
        {
            return false;
        }

        double koreanRatio = (double)koreanCount / total;
        return koreanRatio <= 0.95;
    }
}
