using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

    public static bool IsModelAvailable(OcrLanguage language) => IsModelAvailable(language, ModelRoot);

    internal static bool IsModelAvailable(OcrLanguage language, string modelRoot)
        => IsModelAvailable(GetModelKey(language.Tag), modelRoot);

    internal static bool IsModelAvailable(string languageOrModelKey)
        => IsModelAvailable(GetModelKey(languageOrModelKey), ModelRoot);

    internal static bool IsModelAvailable(string languageOrModelKey, string modelRoot)
    {
        var requiredDirectories = RequiredModelDirectories(GetModelKey(languageOrModelKey));

        return requiredDirectories.All(directory =>
        {
            var directoryPath = Path.Combine(modelRoot, directory);
            return Directory.Exists(directoryPath)
                && Directory.EnumerateFiles(directoryPath, "inference.pdmodel", SearchOption.AllDirectories).Any()
                && Directory.EnumerateFiles(directoryPath, "inference.pdiparams", SearchOption.AllDirectories).Any();
        });
    }

    public async Task PrepareAsync(OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await semaphore.WaitAsync(ct);
        try
        {
            await EnsureModelLoadedAsync(GetModelKey(language.Tag), language.DisplayName, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal async Task DownloadModelPackageAsync(string modelKey, string displayName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        modelKey = GetModelKey(modelKey);

        await semaphore.WaitAsync(ct);
        try
        {
            if (IsModelAvailable(modelKey))
            {
                return;
            }

            AppLog.Write($"Downloading PaddleOCR model: {modelKey}");
            _ = await FetchModelAsync(modelKey, displayName, ct);
            if (!IsModelAvailable(modelKey))
            {
                throw new InvalidOperationException($"{displayName} OCR 모델 파일을 확인할 수 없습니다.");
            }

            AppLog.Write($"PaddleOCR model {modelKey} downloaded successfully.");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task EnsureModelLoadedAsync(string modelKey, string displayName, CancellationToken ct)
    {
        if (currentOcr != null && currentLanguageTag == modelKey)
        {
            return;
        }

        AppLog.Write($"Loading PaddleOCR model: {modelKey}");

        var model = await FetchModelAsync(modelKey, displayName, ct);
        var nextOcr = new PaddleOcrAll(model, new DeviceOptions("CPU"))
        {
            AllowRotateDetection = false,
            Enable180Classification = false
        };
        nextOcr.Detector.MaxSize = 2048;
        var previousOcr = currentOcr;
        currentOcr = nextOcr;
        currentLanguageTag = modelKey;
        previousOcr?.Dispose();
        frameCache.Clear();
        AppLog.Write($"PaddleOCR model {modelKey} loaded successfully.");
    }

    private static async Task<FullOcrModel> FetchModelAsync(
        string modelKey,
        string displayName,
        CancellationToken ct)
    {
        try
        {
            return await DownloadModelAsync(modelKey, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write($"[Error] Failed to prepare PaddleOCR models: {ex.Message}");
            throw new Exception($"{displayName} OCR 모델을 준비하지 못했습니다. 네트워크 연결을 확인한 뒤 다시 시도하세요.", ex);
        }
    }

    internal static string GetModelKey(string languageTag) => languageTag switch
    {
        "de" or "fr" or "es" or "pt" or "it" or "nl" or "pl" or "tr" or "vi" or "id" => "latin",
        "ru" or "uk" or "bg" => "cyrillic",
        "fa" or "ur" => "ar",
        "mr" or "ne" => "hi",
        "zh-Hans" or "zh-Hant" or "en" or "ja" or "ko" or "latin" or "cyrillic"
            or "ar" or "hi" or "ta" or "te" or "kn" => languageTag,
        _ => throw new ArgumentOutOfRangeException(nameof(languageTag), languageTag, "지원하지 않는 OCR 언어입니다.")
    };

    private static IReadOnlyList<string> RequiredModelDirectories(string modelKey) => modelKey switch
    {
        "zh-Hant" => ["ch_PP-OCRv3_det", "chinese_cht_PP-OCRv3_rec", "ch_ppocr_mobile_v2.0_cls"],
        "latin" => ["ml_PP-OCRv3_det", "latin_PP-OCRv3_rec", "ch_ppocr_mobile_v2.0_cls"],
        "cyrillic" => ["ml_PP-OCRv3_det", "cyrillic_PP-OCRv3_rec", "ch_ppocr_mobile_v2.0_cls"],
        "zh-Hans" => ["ch_PP-OCRv4_det", "ch_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "en" => ["ch_PP-OCRv4_det", "en_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "ja" => ["ch_PP-OCRv4_det", "japan_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "ko" => ["ch_PP-OCRv4_det", "korean_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "ar" => ["ch_PP-OCRv4_det", "arabic_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "hi" => ["ch_PP-OCRv4_det", "devanagari_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "ta" => ["ch_PP-OCRv4_det", "ta_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "te" => ["ch_PP-OCRv4_det", "te_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        "kn" => ["ch_PP-OCRv4_det", "ka_PP-OCRv4_rec", "ch_ppocr_mobile_v2.0_cls"],
        _ => throw new ArgumentOutOfRangeException(nameof(modelKey), modelKey, "지원하지 않는 OCR 모델입니다.")
    };

    private static Task<FullOcrModel> DownloadModelAsync(string modelKey, CancellationToken ct) => modelKey switch
    {
        "zh-Hans" => OnlineFullModels.ChineseV4.DownloadAsync(ct),
        "zh-Hant" => OnlineFullModels.TraditionalChineseV3.DownloadAsync(ct),
        "en" => OnlineFullModels.EnglishV4.DownloadAsync(ct),
        "ja" => OnlineFullModels.JapanV4.DownloadAsync(ct),
        "ko" => OnlineFullModels.KoreanV4.DownloadAsync(ct),
        "latin" => OnlineFullModels.LatinV3.DownloadAsync(ct),
        "cyrillic" => OnlineFullModels.CyrillicV3.DownloadAsync(ct),
        "ar" => OnlineFullModels.ArabicV4.DownloadAsync(ct),
        "hi" => OnlineFullModels.DevanagariV4.DownloadAsync(ct),
        "ta" => OnlineFullModels.TamilV4.DownloadAsync(ct),
        "te" => OnlineFullModels.TeluguV4.DownloadAsync(ct),
        "kn" => OnlineFullModels.KannadaV4.DownloadAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(modelKey), modelKey, "지원하지 않는 OCR 모델입니다.")
    };

    public async Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await semaphore.WaitAsync(ct);
        try
        {
            await EnsureModelLoadedAsync(GetModelKey(language.Tag), language.DisplayName, ct);

            using var pixels = CaptureBitmapPixels(frame.Bitmap);
            pixels.ApplyMasks(frame.IncludedOcrRects, frame.ExcludedOcrRects);
            if (frameCache.TryGet(pixels, language.Tag, out var cachedResult))
            {
                return cachedResult;
            }

            using var mat = PixelsToMat(pixels);
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
            frameCache.Store(pixels, language.Tag, result);
            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal static Mat BitmapSourceToMat(BitmapSource bitmap)
    {
        using var pixels = CaptureBitmapPixels(bitmap);
        return PixelsToMat(pixels);
    }

    internal static OcrFramePixels CaptureBitmapPixels(BitmapSource bitmap)
    {
        BitmapSource source = bitmap;
        if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Bgr32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            source = converted;
        }

        var stride = checked(source.PixelWidth * 4);
        var length = checked(stride * source.PixelHeight);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            source.CopyPixels(buffer, stride, 0);
            return new OcrFramePixels(buffer, source.PixelWidth, source.PixelHeight, stride);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    private static Mat PixelsToMat(OcrFramePixels pixels)
    {
        var pinned = GCHandle.Alloc(pixels.Buffer, GCHandleType.Pinned);
        try
        {
            using var bgra = Mat.FromPixelData(
                pixels.Height,
                pixels.Width,
                MatType.CV_8UC4,
                pinned.AddrOfPinnedObject(),
                pixels.Stride);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Dispose()
    {
        frameCache.Dispose();
        currentOcr?.Dispose();
        semaphore.Dispose();
    }
}
