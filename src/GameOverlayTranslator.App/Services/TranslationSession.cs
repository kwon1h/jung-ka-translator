using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App.Services;

public sealed class TranslationSession(ICaptureService captureService, IOcrEngine ocrEngine, ITranslationService translationService) : ITranslationSession
{
    private CancellationTokenSource? runCancellation;
    private Task? runTask;

    public event EventHandler<SessionUpdate>? Updated;

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
        var recentChat = new RecentChatFilter();
        using var timer = new PeriodicTimer(options.Interval);

        do
        {
            try
            {
                var frame = await captureService.CaptureAsync(options.Target, options.Region, ct);
                var recognized = await ocrEngine.RecognizeAsync(frame, options.OcrLanguage, ct);

                if (!string.IsNullOrWhiteSpace(recognized.Text))
                {
                    Publish("OCR 감지", ocrRawText: recognized.Text);
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
                    if (!exactLines.Add(line.DeduplicationKey))
                    {
                        Publish("중복 필터링", source: line.SourceLine, filterReason: "동일 프레임/세션 내 중복 메시지", filterRule: "ExactDuplicateFilter");
                        continue;
                    }

                    // Apply User Dictionary
                    var activeLine = line;
                    bool dictionaryMatchedExact = false;
                    string? dictionaryExactTranslation = null;

                    var trimmedMsg = line.Message.Trim();
                    var exactEntry = options.UserDictionary.FirstOrDefault(e => string.Equals(e.Source.Trim(), trimmedMsg, StringComparison.OrdinalIgnoreCase));
                    if (exactEntry != null)
                    {
                        dictionaryMatchedExact = true;
                        dictionaryExactTranslation = exactEntry.Target;
                    }

                    if (dictionaryMatchedExact && dictionaryExactTranslation != null)
                    {
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
                        continue;
                    }

                    var processedMessage = line.Message;
                    bool replaced = false;
                    foreach (var entry in options.UserDictionary)
                    {
                        if (processedMessage.Contains(entry.Source, StringComparison.OrdinalIgnoreCase))
                        {
                            processedMessage = processedMessage.Replace(entry.Source, entry.Target, StringComparison.OrdinalIgnoreCase);
                            replaced = true;
                        }
                    }

                    if (replaced)
                    {
                        AppLog.Write($"UserDictionary substring replace. Before={Quote(line.Message)} After={Quote(processedMessage)}");
                        activeLine = new ChatLine(line.Speaker, processedMessage);
                    }

                    // Quality check
                    var quality = ChatQualityFilter.Check(activeLine, options.OcrLanguage, options.Filter);
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
                        continue;
                    }

                    Publish("채팅 번역 요청", activeLine.SourceLine);
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
        string? filterRule = null) =>
        Updated?.Invoke(this, new SessionUpdate(status, source, translated, isError, speaker, isChatLine, chatLineId, replacesChatLine, ocrRawText, filterReason, filterRule));

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";
}
