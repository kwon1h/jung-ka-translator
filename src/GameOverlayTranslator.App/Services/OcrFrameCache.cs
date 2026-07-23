using System.Buffers;
using System.Windows;
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

    public void ApplyMasks(
        IReadOnlyList<Rect>? includedRects,
        IReadOnlyList<Rect>? excludedRects)
    {
        if (includedRects is { Count: > 0 })
        {
            var includedPixels = ToPixelRects(includedRects);
            if (includedPixels.Count == 0)
            {
                Buffer.AsSpan(0, Length).Clear();
            }
            else
            {
                includedPixels.Sort(static (left, right) => left.Left.CompareTo(right.Left));
                for (var y = 0; y < Height; y++)
                {
                    var cursor = 0;
                    foreach (var rect in includedPixels)
                    {
                        if (y < rect.Top || y >= rect.Bottom)
                        {
                            continue;
                        }

                        if (rect.Left > cursor)
                        {
                            ClearRowRange(y, cursor, rect.Left);
                        }

                        cursor = Math.Max(cursor, rect.Right);
                        if (cursor >= Width)
                        {
                            break;
                        }
                    }

                    if (cursor < Width)
                    {
                        ClearRowRange(y, cursor, Width);
                    }
                }
            }
        }

        if (excludedRects is { Count: > 0 })
        {
            foreach (var rect in ToPixelRects(excludedRects))
            {
                for (var y = rect.Top; y < rect.Bottom; y++)
                {
                    ClearRowRange(y, rect.Left, rect.Right);
                }
            }
        }
    }

    public void Dispose()
    {
        var rented = Interlocked.Exchange(ref buffer, null);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private List<PixelRect> ToPixelRects(IReadOnlyList<Rect> rects)
    {
        var result = new List<PixelRect>(rects.Count);
        foreach (var rect in rects)
        {
            var left = Math.Clamp((int)Math.Floor(rect.Left), 0, Width);
            var top = Math.Clamp((int)Math.Floor(rect.Top), 0, Height);
            var right = Math.Clamp((int)Math.Ceiling(rect.Right), 0, Width);
            var bottom = Math.Clamp((int)Math.Ceiling(rect.Bottom), 0, Height);
            if (right > left && bottom > top)
            {
                result.Add(new PixelRect(left, top, right, bottom));
            }
        }

        return result;
    }

    private void ClearRowRange(int y, int left, int right)
    {
        var offset = checked(y * Stride + left * 4);
        var length = checked((right - left) * 4);
        Buffer.AsSpan(offset, length).Clear();
    }

    private readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);
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
