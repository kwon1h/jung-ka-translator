using System.IO;
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

        return new GameOverlayTranslator.App.Domain.OcrResult(result.Text.Trim());
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
