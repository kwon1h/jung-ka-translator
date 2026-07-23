using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using OpenCvSharp;
using Sdcb.OpenVINO;
using Sdcb.OpenVINO.PaddleOCR;
using Sdcb.OpenVINO.PaddleOCR.Models;
using Sdcb.OpenVINO.PaddleOCR.Models.Online;

namespace GameOverlayTranslator.App.Services;

public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private static readonly string ModelRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "paddleocr-models");
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly OcrFrameCache frameCache = new();
    private PaddleOcrAll? currentOcr;
    private string? currentLanguageTag;

    public static bool IsModelAvailable(OcrLanguage language)
    {
        var requiredDirectories = new[] { "ch_PP-OCRv4_det", RecognitionDirectory(language.Tag), "ch_ppocr_mobile_v2.0_cls" };

        return requiredDirectories.All(directory => Directory.Exists(Path.Combine(ModelRoot, directory)) &&
            Directory.EnumerateFiles(Path.Combine(ModelRoot, directory), "inference.pdmodel", SearchOption.AllDirectories).Any());
    }

    public async Task PrepareAsync(OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await semaphore.WaitAsync(ct);
        try
        {
            await EnsureModelLoadedAsync(language, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task EnsureModelLoadedAsync(OcrLanguage language, CancellationToken ct)
    {
        if (currentOcr != null && currentLanguageTag == language.Tag)
        {
            return;
        }

        AppLog.Write($"Loading PaddleOCR model for language: {language.Tag}");

        FullOcrModel model;
        try
        {
            model = await DownloadModelAsync(language.Tag, ct);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Error] Failed to load PaddleOCR models: {ex.Message}");
            throw new Exception($"{language.DisplayName} OCR 모델을 준비하지 못했습니다. 네트워크 연결을 확인한 뒤 다시 시도하세요.", ex);
        }

        var nextOcr = new PaddleOcrAll(model, new DeviceOptions("CPU"))
        {
            AllowRotateDetection = false,
            Enable180Classification = false
        };
        nextOcr.Detector.MaxSize = 2048;
        var previousOcr = currentOcr;
        currentOcr = nextOcr;
        currentLanguageTag = language.Tag;
        previousOcr?.Dispose();
        frameCache.Clear();
        AppLog.Write($"PaddleOCR model for language {language.Tag} loaded successfully.");
    }

    private static string RecognitionDirectory(string languageTag) => languageTag switch
    {
        "zh-Hans" => "ch_PP-OCRv4_rec",
        "en" => "en_PP-OCRv4_rec",
        "ja" => "japan_PP-OCRv4_rec",
        "ko" => "korean_PP-OCRv4_rec",
        "ar" => "arabic_PP-OCRv4_rec",
        "hi" => "devanagari_PP-OCRv4_rec",
        "ta" => "ta_PP-OCRv4_rec",
        "te" => "te_PP-OCRv4_rec",
        "kn" => "ka_PP-OCRv4_rec",
        _ => throw new ArgumentOutOfRangeException(nameof(languageTag), languageTag, "지원하지 않는 OCR 언어입니다.")
    };

    private static Task<FullOcrModel> DownloadModelAsync(string languageTag, CancellationToken ct) => languageTag switch
    {
        "zh-Hans" => OnlineFullModels.ChineseV4.DownloadAsync(ct),
        "en" => OnlineFullModels.EnglishV4.DownloadAsync(ct),
        "ja" => OnlineFullModels.JapanV4.DownloadAsync(ct),
        "ko" => OnlineFullModels.KoreanV4.DownloadAsync(ct),
        "ar" => OnlineFullModels.ArabicV4.DownloadAsync(ct),
        "hi" => OnlineFullModels.DevanagariV4.DownloadAsync(ct),
        "ta" => OnlineFullModels.TamilV4.DownloadAsync(ct),
        "te" => OnlineFullModels.TeluguV4.DownloadAsync(ct),
        "kn" => OnlineFullModels.KannadaV4.DownloadAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(languageTag), languageTag, "지원하지 않는 OCR 언어입니다.")
    };

    public async Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await semaphore.WaitAsync(ct);
        try
        {
            await EnsureModelLoadedAsync(language, ct);

            using var mat = BitmapSourceToMat(frame.Bitmap);
            if (frameCache.TryGet(mat, language.Tag, out var cachedResult))
            {
                return cachedResult;
            }

            var ocr = currentOcr ?? throw new InvalidOperationException("PaddleOCR 모델이 준비되지 않았습니다.");
            var paddleResult = ocr.Run(mat);

            var lines = new List<OcrLineResult>();
            var words = new List<OcrWordResult>();

            foreach (var region in paddleResult.Regions)
            {
                if (string.IsNullOrWhiteSpace(region.Text))
                {
                    continue;
                }

                var openCvRect = region.Rect.BoundingRect();
                var wpfRect = new System.Windows.Rect(openCvRect.X, openCvRect.Y, openCvRect.Width, openCvRect.Height);

                lines.Add(new OcrLineResult(region.Text.Trim(), wpfRect));
                words.Add(new OcrWordResult(region.Text.Trim(), wpfRect));
            }

            var concatenatedText = string.Join(Environment.NewLine, lines.Select(line => line.Text));

            var result = new OcrResult(concatenatedText, lines)
            {
                Words = words
            };
            frameCache.Store(mat, language.Tag, result);
            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal static Mat BitmapSourceToMat(BitmapSource bitmap)
    {
        BitmapSource source = bitmap;
        if (bitmap.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            source = converted;
        }

        using var bgra = new Mat(source.PixelHeight, source.PixelWidth, MatType.CV_8UC4);
        var stride = checked((int)bgra.Step());
        source.CopyPixels(Int32Rect.Empty, bgra.Data, checked(stride * source.PixelHeight), stride);

        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    public void Dispose()
    {
        frameCache.Dispose();
        currentOcr?.Dispose();
        semaphore.Dispose();
    }
}
