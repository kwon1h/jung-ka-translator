using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GameOverlayTranslator.App.Services;

public sealed class WindowsOcrEngine : IOcrEngine
{
    public async Task<GameOverlayTranslator.App.Domain.OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var engine = OcrEngine.TryCreateFromLanguage(new Language(language.Tag))
            ?? throw new InvalidOperationException($"{language.DisplayName} Windows OCR 언어 팩을 사용할 수 없습니다.");

        using var stream = new InMemoryRandomAccessStream();
        await WritePngAsync(frame.Bitmap, stream, ct);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        ct.ThrowIfCancellationRequested();

        var lines = new List<OcrLineResult>();
        foreach (var ocrLine in result.Lines)
        {
            var text = ocrLine.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text) && ocrLine.Words.Count > 0)
            {
                var firstWordRect = ocrLine.Words[0].BoundingRect;
                double minX = firstWordRect.X;
                double minY = firstWordRect.Y;
                double maxX = firstWordRect.X + firstWordRect.Width;
                double maxY = firstWordRect.Y + firstWordRect.Height;

                for (int i = 1; i < ocrLine.Words.Count; i++)
                {
                    var wordRect = ocrLine.Words[i].BoundingRect;
                    minX = Math.Min(minX, wordRect.X);
                    minY = Math.Min(minY, wordRect.Y);
                    maxX = Math.Max(maxX, wordRect.X + wordRect.Width);
                    maxY = Math.Max(maxY, wordRect.Y + wordRect.Height);
                }

                var rect = new Rect(minX, minY, maxX - minX, maxY - minY);
                lines.Add(new OcrLineResult(text, rect));
            }
        }

        return new GameOverlayTranslator.App.Domain.OcrResult(result.Text.Trim(), lines);
    }

    private static async Task WritePngAsync(BitmapSource bitmap, IRandomAccessStream stream, CancellationToken ct)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        memory.Position = 0;
        await memory.CopyToAsync(stream.AsStreamForWrite(), ct);
        stream.Seek(0);
    }
}
