using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Contracts;

public interface IWindowSource
{
    IReadOnlyList<CapturableWindow> ListWindows();
}

public interface ICaptureService
{
    Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct);
}

public interface IOcrEngine
{
    Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct);
}

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct);
}

public interface ITranslationSession
{
    event EventHandler<SessionUpdate>? Updated;

    bool IsRunning { get; }

    Task StartAsync(SessionOptions options, CancellationToken ct);

    Task StopAsync();
}
