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
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private PaddleOcrAll? currentOcr;
    private string? currentLanguageTag;

    public async Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await semaphore.WaitAsync(ct);
        try
        {
            if (currentOcr == null || currentLanguageTag != language.Tag)
            {
                currentOcr?.Dispose();
                currentOcr = null;

                AppLog.Write($"Loading PaddleOCR model for language: {language.Tag}");

                FullOcrModel model;
                try
                {
                    if (language.Tag == "ja")
                    {
                        model = await OnlineFullModels.JapanV4.DownloadAsync(ct);
                    }
                    else
                    {
                        // Default to ChineseV4 (supports Chinese + English)
                        model = await OnlineFullModels.ChineseV4.DownloadAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write($"[Error] Failed to load PaddleOCR models: {ex.Message}");
                    AppLog.Write("[Tip] If you are experiencing network download issues, please run the download script at: scripts/download-models.ps1");
                    throw new Exception("PaddleOCR model files are missing or could not be downloaded. Please run the download helper script 'scripts/download-models.ps1' to manually set them up.", ex);
                }

                // Initialize PaddleOcrAll with CPU device options
                currentOcr = new PaddleOcrAll(model, new DeviceOptions("CPU"))
                {
                    AllowRotateDetection = false, // Speed up by disabling rotation detection (almost all game text is horizontal)
                    Enable180Classification = false
                };
                
                // Set MaxSize to 2048 to prevent downscaling small game text and massively improve accuracy
                currentOcr.Detector.MaxSize = 2048;
                currentLanguageTag = language.Tag;

                AppLog.Write($"PaddleOCR model for language {language.Tag} loaded successfully.");
            }
        }
        finally
        {
            semaphore.Release();
        }

        // Convert CapturedFrame Bitmap to OpenCV Mat
        using var mat = BitmapSourceToMat(frame.Bitmap);
        
        // Run OCR (Run is synchronous, so run it on the current thread)
        var paddleResult = currentOcr.Run(mat);

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
