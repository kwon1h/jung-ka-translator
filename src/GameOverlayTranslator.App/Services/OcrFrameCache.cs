using GameOverlayTranslator.App.Domain;
using OpenCvSharp;

namespace GameOverlayTranslator.App.Services;

internal sealed class OcrFrameCache : IDisposable
{
    private Mat? previousFrame;
    private string? previousLanguageTag;
    private OcrResult? previousResult;

    public bool TryGet(Mat frame, string languageTag, out OcrResult result)
    {
        if (previousFrame is not null
            && previousResult is not null
            && string.Equals(previousLanguageTag, languageTag, StringComparison.OrdinalIgnoreCase)
            && previousFrame.Rows == frame.Rows
            && previousFrame.Cols == frame.Cols
            && previousFrame.Type() == frame.Type()
            && Cv2.Norm(previousFrame, frame, NormTypes.L1) == 0)
        {
            result = previousResult;
            return true;
        }

        result = null!;
        return false;
    }

    public void Store(Mat frame, string languageTag, OcrResult result)
    {
        var storedFrame = frame.Clone();
        previousFrame?.Dispose();
        previousFrame = storedFrame;
        previousLanguageTag = languageTag;
        previousResult = result;
    }

    public void Clear()
    {
        previousFrame?.Dispose();
        previousFrame = null;
        previousLanguageTag = null;
        previousResult = null;
    }

    public void Dispose() => Clear();
}
