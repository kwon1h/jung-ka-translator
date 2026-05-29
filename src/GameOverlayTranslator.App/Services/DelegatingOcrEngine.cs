using System;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

public sealed class DelegatingOcrEngine(Func<OcrEngineType> getActiveEngineType, IOcrEngine windowsOcr, IOcrEngine paddleOcr) : IOcrEngine, IDisposable
{
    public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        var activeEngine = getActiveEngineType() == OcrEngineType.PaddleOCR ? paddleOcr : windowsOcr;
        return activeEngine.RecognizeAsync(frame, language, ct);
    }

    public void Dispose()
    {
        if (windowsOcr is IDisposable wOcr)
        {
            wOcr.Dispose();
        }
        if (paddleOcr is IDisposable pOcr)
        {
            pOcr.Dispose();
        }
    }
}
