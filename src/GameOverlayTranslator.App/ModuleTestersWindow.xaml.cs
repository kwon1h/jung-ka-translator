using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;
using GameOverlayTranslator.App.Services;
using Microsoft.Win32;

namespace GameOverlayTranslator.App;

public partial class ModuleTestersWindow : Window
{
    private static readonly IReadOnlyList<OcrLanguage> OcrLanguages = [new("zh-Hans", "중국어(간체)"), new("ja", "일본어")];
    private static readonly IReadOnlyList<TranslationLanguage> TranslationLanguages = [new("ko", "한국어")];

    private readonly IWindowSource windowSource = new Win32WindowSource();
    private readonly ICaptureService captureService = new WindowCaptureService();
    private readonly IOcrEngine ocrEngine = new WindowsOcrEngine();
    private readonly ITranslationService translationService;
    private CapturedFrame? ocrFrame;

    public ModuleTestersWindow(Func<string?> apiKeyProvider)
    {
        InitializeComponent();
        translationService = new DeepLTranslationService(new HttpClient(), apiKeyProvider);
        OcrTesterLanguageComboBox.ItemsSource = OcrLanguages;
        OcrTesterLanguageComboBox.SelectedIndex = 0;
        TranslationTesterTargetComboBox.ItemsSource = TranslationLanguages;
        TranslationTesterTargetComboBox.SelectedIndex = 0;
        RefreshTesterWindows(this, new RoutedEventArgs());
    }

    private void RefreshTesterWindows(object sender, RoutedEventArgs e)
    {
        var ownHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var windows = windowSource.ListWindows().Where(window => window.Handle != ownHandle).ToList();
        WindowTesterGrid.ItemsSource = windows;
        CaptureWindowComboBox.ItemsSource = windows;
        CaptureWindowComboBox.SelectedItem = windows.FirstOrDefault();
        WindowTesterStatus.Text = $"{windows.Count}개 창을 찾았습니다.";
    }

    private async void CaptureSelectedWindow(object sender, RoutedEventArgs e)
    {
        if (CaptureWindowComboBox.SelectedItem is not CapturableWindow window)
        {
            CaptureTesterStatus.Text = "캡처할 창을 선택하세요.";
            return;
        }

        await RunTesterAsync(CaptureTesterStatus, async () =>
        {
            var frame = await captureService.CaptureAsync(new CaptureTarget(window), new CaptureRegion(0, 0, 1, 1), CancellationToken.None);
            CapturePreviewImage.Source = frame.Bitmap;
            CaptureTesterStatus.Text = $"{frame.Bitmap.PixelWidth} x {frame.Bitmap.PixelHeight} 캡처 완료";
        });
    }

    private void LoadOcrImage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(dialog.FileName);
        image.EndInit();
        image.Freeze();
        ocrFrame = new CapturedFrame(image);
        OcrTesterImage.Source = image;
        OcrTesterResult.Clear();
        OcrTesterStatus.Text = $"{dialog.SafeFileName} 로드 완료";
    }

    private async void RunImageOcr(object sender, RoutedEventArgs e)
    {
        if (ocrFrame is null || OcrTesterLanguageComboBox.SelectedItem is not OcrLanguage language)
        {
            OcrTesterStatus.Text = "OCR 이미지와 언어를 먼저 선택하세요.";
            return;
        }

        await RunTesterAsync(OcrTesterStatus, async () =>
        {
            var result = await ocrEngine.RecognizeAsync(ocrFrame, language, CancellationToken.None);
            OcrTesterResult.Text = result.Text;
            OcrTesterStatus.Text = string.IsNullOrWhiteSpace(result.Text) ? "OCR 결과가 비어 있습니다." : "OCR 완료";
        });
    }

    private async void RunTranslation(object sender, RoutedEventArgs e)
    {
        if (TranslationTesterTargetComboBox.SelectedItem is not TranslationLanguage target || string.IsNullOrWhiteSpace(TranslationTesterSource.Text))
        {
            TranslationTesterStatus.Text = "원문과 대상 언어를 확인하세요.";
            return;
        }

        await RunTesterAsync(TranslationTesterStatus, async () =>
        {
            var result = await translationService.TranslateAsync(new TranslationRequest(TranslationTesterSource.Text, target.Code), CancellationToken.None);
            TranslationTesterResult.Text = result.TranslatedText;
            TranslationTesterStatus.Text = result.DetectedSourceLanguage is null
                ? "번역 완료"
                : $"번역 완료. 감지 언어: {result.DetectedSourceLanguage}";
        });
    }

    private async void RunFakeSession(object sender, RoutedEventArgs e)
    {
        var capture = new FakeCaptureService();
        var ocr = new FakeOcrEngine(["rider1: 注意地板", "rider1: 注意地板", "rider2: 快跑", "", "rider2: 快跑"]);
        var translator = new FakeTranslationService();
        var session = new TranslationSession(capture, ocr, translator);
        SessionTesterLog.Clear();
        session.Updated += (_, update) =>
        {
            Dispatcher.Invoke(() =>
            {
                SessionTesterLog.AppendText($"{DateTime.Now:HH:mm:ss.fff} | {update.Status} | {update.SourceText} | {update.TranslatedText}{Environment.NewLine}");
                SessionTesterLog.ScrollToEnd();
            });
        };

        await session.StartAsync(
            new SessionOptions(
                new CaptureTarget(new CapturableWindow(nint.Zero, "fake", "tester")),
                new CaptureRegion(0, 0, 1, 1),
                OcrLanguages[0],
                TranslationLanguages[0],
                TimeSpan.FromMilliseconds(90)),
            CancellationToken.None);
        await Task.Delay(560);
        await session.StopAsync();
        SessionTesterCounters.Text = $"캡처 {capture.Calls}회, OCR {ocr.Calls}회, 번역 {translator.Calls}회";
    }

    private static async Task RunTesterAsync(System.Windows.Controls.TextBlock status, Func<Task> action)
    {
        try
        {
            status.Text = "실행 중";
            await action();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
    }

    private sealed class FakeCaptureService : ICaptureService
    {
        public int Calls { get; private set; }

        public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
        {
            Calls++;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 2, 2));
            }

            var bitmap = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return Task.FromResult(new CapturedFrame(bitmap));
        }
    }

    private sealed class FakeOcrEngine(IReadOnlyList<string> texts) : IOcrEngine
    {
        public int Calls { get; private set; }

        public Task<OcrResult> RecognizeAsync(CapturedFrame frame, OcrLanguage language, CancellationToken ct)
        {
            var text = texts[Math.Min(Calls, texts.Count - 1)];
            Calls++;
            return Task.FromResult(new OcrResult(text));
        }
    }

    private sealed class FakeTranslationService : ITranslationService
    {
        public int Calls { get; private set; }

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new TranslationResult(request.Text, $"translated: {request.Text}", "fake"));
        }
    }
}
