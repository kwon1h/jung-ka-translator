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
                var chatLines = ChatLineParser.Parse(recognized.Text);
                AppLog.Write($"OCR chat poll raw={Quote(recognized.Text)} parsedLines={chatLines.Count}");
                if (chatLines.Count == 0)
                {
                    Publish("채팅 줄 대기");
                    continue;
                }

                foreach (var line in chatLines.Where(line => exactLines.Add(line.DeduplicationKey)))
                {
                    var quality = ChatQualityFilter.Check(line, options.OcrLanguage);
                    if (!quality.Accepted)
                    {
                        AppLog.Write($"ChatQualityFilter reject reason={quality.Reason} line={Quote(line.SourceLine)}");
                        Publish($"채팅 품질 필터: {quality.Reason}", line.SourceLine);
                        continue;
                    }

                    var decision = recentChat.Evaluate(line);
                    if (decision.Action == ChatFilterAction.Skip)
                    {
                        Publish("유사 채팅 건너뜀", line.SourceLine);
                        continue;
                    }

                    if (!quality.TranslateWithService)
                    {
                        AppLog.Write($"ChatQualityFilter source-only reason={quality.Reason} line={Quote(line.SourceLine)}");
                        Publish(
                            "채팅 원문 표시",
                            line.SourceLine,
                            line.Message,
                            speaker: line.Speaker,
                            isChatLine: true,
                            chatLineId: decision.Id,
                            replacesChatLine: decision.Action == ChatFilterAction.Replace);
                        continue;
                    }

                    Publish("채팅 번역 요청", line.SourceLine);
                    var translated = await translationService.TranslateAsync(new TranslationRequest(line.Message, options.TargetLanguage.Code), ct);
                    Publish(
                        decision.Action == ChatFilterAction.Replace ? "유사 채팅 교체" : "채팅 번역 완료",
                        line.SourceLine,
                        translated.TranslatedText,
                        speaker: line.Speaker,
                        isChatLine: true,
                        chatLineId: decision.Id,
                        replacesChatLine: decision.Action == ChatFilterAction.Replace);
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
        bool replacesChatLine = false) =>
        Updated?.Invoke(this, new SessionUpdate(status, source, translated, isError, speaker, isChatLine, chatLineId, replacesChatLine));

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";
}
