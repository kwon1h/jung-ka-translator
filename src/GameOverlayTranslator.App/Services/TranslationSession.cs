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
        Publish("번역 중");
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
        Publish("중지됨");
    }

    private async Task RunAsync(SessionOptions options, CancellationToken ct)
    {
        var exactLines = new HashSet<string>(StringComparer.Ordinal);
        var screenTranslationCache = new Dictionary<string, string>(StringComparer.Ordinal);
        var recentChat = new RecentChatFilter();
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

                if (!string.IsNullOrWhiteSpace(recognized.Text))
                {
                    Publish("OCR 감지", ocrRawText: recognized.Text);
                }

                if (options.Mode == TranslationMode.Screen)
                {
                    var linesToTranslate = recognized.Lines;
                    if (linesToTranslate.Count == 0)
                    {
                        Publish("화면 번역 대기");
                        continue;
                    }

                    // 1. Pre-process all lines (apply dictionary, identify which ones need DeepL translation)
                    var processedLines = new List<(OcrLineResult OcrLine, string ProcessedText, string CacheKey, string? DirectTranslation)>();
                    var textsToTranslate = new List<string>();
                    var cacheKeysToTranslate = new List<string>();

                    foreach (var line in linesToTranslate)
                    {
                        var trimmed = line.Text.Trim();
                        
                        // Exact match in dictionary
                        var normalizedTrimmed = NormalizeForMatching(trimmed);
                        var exactEntry = dictExactEntries.FirstOrDefault(e => string.Equals(e.NormalizedSource, normalizedTrimmed, StringComparison.OrdinalIgnoreCase)).Entry;
                        if (exactEntry != null)
                        {
                            processedLines.Add((line, exactEntry.Target, CreateScreenTranslationCacheKey(line.Text, exactEntry.Target, options.TargetLanguage.Code), exactEntry.Target));
                            continue;
                        }

                        // Substring replacements in dictionary
                        var processed = line.Text;
                        foreach (var (entry, regex) in dictRegexes)
                        {
                            processed = regex.Replace(processed, entry.Target);
                        }

                        if (HasExpectedSourceScript(processed, options.OcrLanguage))
                        {
                            var cacheKey = CreateScreenTranslationCacheKey(line.Text, processed, options.TargetLanguage.Code);
                            if (screenTranslationCache.TryGetValue(cacheKey, out var cachedTranslation))
                            {
                                processedLines.Add((line, processed, cacheKey, cachedTranslation));
                            }
                            else
                            {
                                processedLines.Add((line, processed, cacheKey, null));
                                textsToTranslate.Add(processed);
                                cacheKeysToTranslate.Add(cacheKey);
                            }
                        }
                        else
                        {
                            processedLines.Add((line, processed, CreateScreenTranslationCacheKey(line.Text, processed, options.TargetLanguage.Code), processed));
                        }
                    }

                    // 2. Perform batch translation for lines that need it
                    IReadOnlyList<string> translatedTexts = Array.Empty<string>();
                    if (textsToTranslate.Count > 0)
                    {
                        try
                        {
                            var batchCharacters = textsToTranslate.Sum(text => text.Length);
                            totalTranslationRequests += textsToTranslate.Count;
                            totalTranslationCharacters += batchCharacters;
                            AppLog.Write($"TranslationRequest mode=screen count={textsToTranslate.Count} chars={batchCharacters}");
                            Publish(
                                "화면 번역 요청",
                                filterReason: $"요청 {textsToTranslate.Count}건 / {batchCharacters}자",
                                filterRule: "TranslationRequest",
                                translationRequestCount: textsToTranslate.Count,
                                translationCharacterCount: batchCharacters,
                                totalTranslationRequestCount: totalTranslationRequests,
                                totalTranslationCharacterCount: totalTranslationCharacters);
                            var batchResult = await translationService.TranslateBatchAsync(
                                new BatchTranslationRequest(textsToTranslate, options.TargetLanguage.Code), 
                                ct);
                            translatedTexts = batchResult.TranslatedTexts;
                            for (var index = 0; index < translatedTexts.Count && index < cacheKeysToTranslate.Count; index++)
                            {
                                screenTranslationCache[cacheKeysToTranslate[index]] = translatedTexts[index];
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLog.Write("Batch translation failed, falling back to raw texts", ex);
                            // Fallback to using the processed texts (with dictionary replacements)
                            translatedTexts = textsToTranslate;
                        }
                    }
                    else
                    {
                        Publish("화면 사전 처리 완료", filterReason: "사전 치환 후 번역 API 건너뜀", filterRule: "DictionaryOnly");
                    }

                    // 3. Reconstruct the screen translation items
                    var screenItems = new List<ScreenTranslationItem>();
                    int translateResultIndex = 0;

                    foreach (var entry in processedLines)
                    {
                        string translation;
                        if (entry.DirectTranslation != null)
                        {
                            translation = entry.DirectTranslation;
                        }
                        else
                        {
                            translation = translateResultIndex < translatedTexts.Count 
                                ? translatedTexts[translateResultIndex++] 
                                : entry.ProcessedText;
                        }
                        screenItems.Add(new ScreenTranslationItem(entry.OcrLine.Text, translation, entry.OcrLine.BoundingRect));
                    }

                    Publish("화면 번역 완료", screenItems: screenItems);
                    continue;
                }

                var chatLines = ChatLineParser.Parse(recognized.Text);
                AppLog.Write($"OCR chat poll raw={Quote(recognized.Text)} parsedLines={chatLines.Count}");
                if (chatLines.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(recognized.Text))
                    {
                        Publish("채팅 파싱 실패", ocrRawText: recognized.Text, filterReason: "대화방식(이름: 메시지) 파싱 불가", filterRule: "ChatLineParser");
                    }
                    Publish("채팅 줄 대기");
                    continue;
                }

                foreach (var line in chatLines)
                {
                    if (exactLines.Contains(line.DeduplicationKey))
                    {
                        Publish("중복 필터링", source: line.SourceLine, filterReason: "동일 프레임/세션 내 중복 메시지", filterRule: "ExactDuplicateFilter");
                        continue;
                    }

                    // 1. Try Exact Dictionary Match first (ignores spaces/punctuation, bypasses quality filter check)
                    var trimmedMsg = line.Message.Trim();
                    var normalizedMsg = NormalizeForMatching(trimmedMsg);
                    var exactEntry = dictExactEntries.FirstOrDefault(e => string.Equals(e.NormalizedSource, normalizedMsg, StringComparison.OrdinalIgnoreCase)).Entry;

                    if (exactEntry != null)
                    {
                        var dictionaryExactTranslation = exactEntry.Target;
                        AppLog.Write($"UserDictionary exact match source={Quote(line.Message)} target={Quote(dictionaryExactTranslation)}");

                        var decision = recentChat.Evaluate(new ChatLine(line.Speaker, dictionaryExactTranslation), options.Filter);
                        if (decision.Action == ChatFilterAction.Skip)
                        {
                            Publish("유사 채팅 건너뜀", line.SourceLine, filterReason: $"유사도 초과 (유사도: {decision.SimilarityScore:F2})", filterRule: "RecentChatFilter");
                            continue;
                        }

                        Publish(
                            decision.Action == ChatFilterAction.Replace ? "유사 채팅 교체" : "채팅 번역 완료",
                            line.SourceLine,
                            dictionaryExactTranslation,
                            speaker: line.Speaker,
                            isChatLine: true,
                            chatLineId: decision.Id,
                            replacesChatLine: decision.Action == ChatFilterAction.Replace,
                            filterReason: "유저 사전 100% 일치 (번역 API 건너뜀)",
                            filterRule: "UserDictionaryExact"
                        );
                        exactLines.Add(line.DeduplicationKey);
                        continue;
                    }

                    // 2. If not exact dictionary match, run Quality Filter Check
                    var initialQuality = ChatQualityFilter.Check(line, options.OcrLanguage, options.Filter);
                    if (!initialQuality.Accepted)
                    {
                        AppLog.Write($"ChatQualityFilter reject reason={initialQuality.Reason} line={Quote(line.SourceLine)}");
                        Publish($"채팅 품질 필터: {initialQuality.Reason}", line.SourceLine, filterReason: initialQuality.Reason, filterRule: initialQuality.Rule);
                        continue;
                    }

                    // 3. Substring user dictionary replacements (space and punctuation flexible)
                    var activeLine = line;
                    var processedMessage = line.Message;
                    bool replaced = false;
                    foreach (var (entry, regex) in dictRegexes)
                    {
                        var before = processedMessage;
                        processedMessage = regex.Replace(processedMessage, entry.Target);
                        if (processedMessage != before)
                        {
                            replaced = true;
                        }
                    }

                    if (replaced)
                    {
                        AppLog.Write($"UserDictionary substring replace. Before={Quote(line.Message)} After={Quote(processedMessage)}");
                        activeLine = new ChatLine(line.Speaker, processedMessage);
                    }

                    // Quality check after replacements (only if replaced, otherwise reuse initialQuality)
                    var quality = replaced ? ChatQualityFilter.Check(activeLine, options.OcrLanguage, options.Filter) : initialQuality;
                    if (!quality.Accepted)
                    {
                        AppLog.Write($"ChatQualityFilter reject reason={quality.Reason} line={Quote(activeLine.SourceLine)}");
                        Publish($"채팅 품질 필터: {quality.Reason}", activeLine.SourceLine, filterReason: quality.Reason, filterRule: quality.Rule);
                        continue;
                    }

                    var decisionForLine = recentChat.Evaluate(activeLine, options.Filter);
                    if (decisionForLine.Action == ChatFilterAction.Skip)
                    {
                        Publish("유사 채팅 건너뜀", activeLine.SourceLine, filterReason: $"유사도 초과 (유사도: {decisionForLine.SimilarityScore:F2})", filterRule: "RecentChatFilter");
                        continue;
                    }

                    if (!quality.TranslateWithService)
                    {
                        AppLog.Write($"ChatQualityFilter source-only reason={quality.Reason} line={Quote(activeLine.SourceLine)}");
                        Publish(
                            "채팅 원문 표시",
                            activeLine.SourceLine,
                            activeLine.Message,
                            speaker: activeLine.Speaker,
                            isChatLine: true,
                            chatLineId: decisionForLine.Id,
                            replacesChatLine: decisionForLine.Action == ChatFilterAction.Replace,
                            filterReason: quality.Reason,
                            filterRule: quality.Rule);
                        exactLines.Add(line.DeduplicationKey);
                        continue;
                    }

                    AppLog.Write($"TranslationRequest mode=chat count=1 chars={activeLine.Message.Length} speaker={Quote(activeLine.Speaker)} message={Quote(activeLine.Message)}");
                    totalTranslationRequests += 1;
                    totalTranslationCharacters += activeLine.Message.Length;
                    Publish(
                        "채팅 번역 요청",
                        activeLine.SourceLine,
                        filterReason: $"요청 1건 / {activeLine.Message.Length}자",
                        filterRule: "TranslationRequest",
                        translationRequestCount: 1,
                        translationCharacterCount: activeLine.Message.Length,
                        totalTranslationRequestCount: totalTranslationRequests,
                        totalTranslationCharacterCount: totalTranslationCharacters);
                    var translated = await translationService.TranslateAsync(new TranslationRequest(activeLine.Message, options.TargetLanguage.Code), ct);
                    Publish(
                        decisionForLine.Action == ChatFilterAction.Replace ? "유사 채팅 교체" : "채팅 번역 완료",
                        activeLine.SourceLine,
                        translated.TranslatedText,
                        speaker: activeLine.Speaker,
                        isChatLine: true,
                        chatLineId: decisionForLine.Id,
                        replacesChatLine: decisionForLine.Action == ChatFilterAction.Replace,
                        filterReason: replaced ? "유저 사전 치환 후 번역 완료" : "필터 통과 및 번역 완료",
                        filterRule: replaced ? "UserDictionaryReplace" : "TranslateSuccess"
                    );
                    exactLines.Add(line.DeduplicationKey);
                }
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
        int totalTranslationCharacterCount = 0) =>
        Updated?.Invoke(this, new SessionUpdate(status, source, translated, isError, speaker, isChatLine, chatLineId, replacesChatLine, ocrRawText, filterReason, filterRule, screenItems, translationRequestCount, translationCharacterCount, totalTranslationRequestCount, totalTranslationCharacterCount));

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static string CreateScreenTranslationCacheKey(string sourceText, string processedText, string targetLanguage) =>
        $"{targetLanguage}\u001f{NormalizeScreenCacheText(sourceText)}\u001f{NormalizeScreenCacheText(processedText)}";

    private static string NormalizeScreenCacheText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static bool HasExpectedSourceScript(string message, OcrLanguage language)
    {
        if (language.Tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return message.Any(IsHan);
        }

        if (language.Tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return message.Any(character => IsHan(character) || character is >= '\u3040' and <= '\u30FF');
        }

        return true;
    }

    private static bool IsHan(char character) => character is >= '\u3400' and <= '\u9FFF';

    private static string NormalizeForMatching(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (IsIgnoredPunctuation(character))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        var normalized = builder.ToString();
        if (normalized.Length == 0)
        {
            return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        }

        return normalized;
    }

    private static bool IsIgnoredPunctuation(char c)
    {
        return c is '_' or '~' or '·' or '!' or '！' or '.' or ',' or '，' or '。' or '?' or '？'
                  or '-' or '^' or '*' or '>' or '<' or '＞' or '＜' or '+' or '=' or '/' or '\\'
                  or '|' or '(' or ')' or '（' or '）' or '[' or ']' or '【' or '】' or '{' or '}'
                  or '`' or '@' or '#' or '$' or '%' or '&' or ';' or '；' or ':' or '：' or '"' or '\'' or '“' or '”';
    }

    private static Regex BuildFlexRegex(string source)
    {
        var coreChars = new List<string>();
        foreach (var c in source)
        {
            if (char.IsWhiteSpace(c) || IsIgnoredPunctuation(c))
            {
                continue;
            }
            coreChars.Add(Regex.Escape(c.ToString()));
        }

        if (coreChars.Count == 0)
        {
            return new Regex(Regex.Escape(source), RegexOptions.IgnoreCase);
        }

        const string noiseClass = @"[\s_~·!！\.,，。?？\-^＊\*><＞＜\+=/\\\|\(\)（）\[\]【】\{\}`@#\$%&;:；：]*";
        var patternBuilder = new StringBuilder();

        if (source.Length > 0 && (char.IsWhiteSpace(source[0]) || IsIgnoredPunctuation(source[0])))
        {
            patternBuilder.Append(noiseClass);
        }

        patternBuilder.Append(string.Join(noiseClass, coreChars));

        if (source.Length > 0 && (char.IsWhiteSpace(source[^1]) || IsIgnoredPunctuation(source[^1])))
        {
            patternBuilder.Append(noiseClass);
        }

        return new Regex(patternBuilder.ToString(), RegexOptions.IgnoreCase);
    }
}
