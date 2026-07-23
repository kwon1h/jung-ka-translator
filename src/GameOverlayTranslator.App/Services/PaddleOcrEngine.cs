using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
            if (currentOcr != null && currentLanguageTag == language.Tag)
            {
                return;
            }

            currentOcr?.Dispose();
            currentOcr = null;

            AppLog.Write($"Loading PaddleOCR model for language: {language.Tag}");

            FullOcrModel model;
            try
            {
                model = await DownloadModelAsync(language.Tag, ct);
            }
            catch (Exception ex)
            {
                AppLog.Write($"[Error] Failed to load PaddleOCR models: {ex.Message}");
                AppLog.Write("[Tip] If you are experiencing network download issues, please run the download script at: scripts/download-models.ps1");
                throw new Exception("PaddleOCR model files are missing or could not be downloaded. Please run the download helper script 'scripts/download-models.ps1' to manually set them up.", ex);
            }

            currentOcr = new PaddleOcrAll(model, new DeviceOptions("CPU"))
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };
            currentOcr.Detector.MaxSize = 2048;
            currentLanguageTag = language.Tag;
            AppLog.Write($"PaddleOCR model for language {language.Tag} loaded successfully.");
        }
        finally
        {
            semaphore.Release();
        }
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
        await PrepareAsync(language, ct);

        // Convert CapturedFrame Bitmap to OpenCV Mat
        using var mat = BitmapSourceToMat(frame.Bitmap);
        
        // Run OCR (Run is synchronous, so run it on the current thread)
        var ocr = currentOcr ?? throw new InvalidOperationException("PaddleOCR 모델이 준비되지 않았습니다.");
        var paddleResult = ocr.Run(mat);

        // Convert PaddleOcrResult to OcrResult
        var lines = new List<OcrLineResult>();
        var words = new List<OcrWordResult>();

        foreach (var region in paddleResult.Regions)
        {
            if (string.IsNullOrWhiteSpace(region.Text))
                continue;

            // Get bounding rect
            var openCvRect = region.Rect.BoundingRect();
            var wpfRect = new System.Windows.Rect(openCvRect.X, openCvRect.Y, openCvRect.Width, openCvRect.Height);
            
            lines.Add(new OcrLineResult(region.Text.Trim(), wpfRect));
            words.Add(new OcrWordResult(region.Text.Trim(), wpfRect));
        }

        // Concatenate text
        var concatenatedText = string.Join(Environment.NewLine, lines.Select(l => l.Text));

        return new OcrResult(concatenatedText, lines)
        {
            Words = words
        };
    }

    private static Mat BitmapSourceToMat(BitmapSource bitmap)
    {
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var bytes = memory.ToArray();
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    public void Dispose()
    {
        currentOcr?.Dispose();
        semaphore.Dispose();
    }
}
