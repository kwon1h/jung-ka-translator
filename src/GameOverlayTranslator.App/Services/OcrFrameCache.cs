using System.Buffers;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Services;

internal sealed class OcrFramePixels : IDisposable
{
    private byte[]? buffer;

    public OcrFramePixels(byte[] buffer, int width, int height, int stride)
    {
        this.buffer = buffer;
        Width = width;
        Height = height;
        Stride = stride;
        Length = checked(stride * height);
    }

    public byte[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(OcrFramePixels));
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int Length { get; }

    public byte[] DetachBuffer()
    {
        var detached = Buffer;
        buffer = null;
        return detached;
    }

    public void Dispose()
    {
        var rented = Interlocked.Exchange(ref buffer, null);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

internal sealed class OcrFrameCache : IDisposable
{
    private byte[]? previousPixels;
    private int previousLength;
    private int previousWidth;
    private int previousHeight;
    private int previousStride;
    private string? previousLanguageTag;
    private OcrResult? previousResult;

    public bool TryGet(OcrFramePixels frame, string languageTag, out OcrResult result)
    {
        if (previousPixels is not null
            && previousResult is not null
            && string.Equals(previousLanguageTag, languageTag, StringComparison.OrdinalIgnoreCase)
            && previousWidth == frame.Width
            && previousHeight == frame.Height
            && previousStride == frame.Stride
            && previousLength == frame.Length
            && previousPixels.AsSpan(0, previousLength).SequenceEqual(frame.Buffer.AsSpan(0, frame.Length)))
        {
            result = previousResult;
            return true;
        }

        result = null!;
        return false;
    }

    public void Store(OcrFramePixels frame, string languageTag, OcrResult result)
    {
        ReleasePreviousPixels();
        previousPixels = frame.DetachBuffer();
        previousLength = frame.Length;
        previousWidth = frame.Width;
        previousHeight = frame.Height;
        previousStride = frame.Stride;
        previousLanguageTag = languageTag;
        previousResult = result;
    }

    public void Clear()
    {
        ReleasePreviousPixels();
        previousLength = 0;
        previousWidth = 0;
        previousHeight = 0;
        previousStride = 0;
        previousLanguageTag = null;
        previousResult = null;
    }

    public void Dispose() => Clear();

    private void ReleasePreviousPixels()
    {
        if (previousPixels is not null)
        {
            ArrayPool<byte>.Shared.Return(previousPixels);
            previousPixels = null;
        }
    }
}
