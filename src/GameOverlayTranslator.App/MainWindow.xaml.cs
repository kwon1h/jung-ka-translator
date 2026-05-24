using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net.Http;
using System.Text.RegularExpressions;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public partial class MainWindow : Window
{
    private sealed record DisplayModeChoice(TranslationDisplayMode Mode, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ColorChoice(string Hex, string Name)
    {
        public Brush HexBrush { get; } = CreateFrozenBrush(Hex);

        private static Brush CreateFrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public override string ToString() => Name;
    }

    private sealed record OverlayPreset(string Name, double FontSize, string TextColor, string OutlineColor, double StrokeThickness, double OverlayOpacity, string BackgroundColor)
    {
        public override string ToString() => Name;
    }

    private static readonly IReadOnlyList<OcrLanguage> OcrLanguages = [new("zh-Hans", "중국어(간체)"), new("ja", "일본어")];
    private static readonly IReadOnlyList<TranslationLanguage> TargetLanguages = [new("ko", "한국어")];
    private static readonly IReadOnlyList<DisplayModeChoice> DisplayModes =
    [
        new(TranslationDisplayMode.Window, "별도 결과 창"),
        new(TranslationDisplayMode.TransparentOverlay, "선택 영역 오버레이")
    ];

    private static readonly IReadOnlyList<ColorChoice> TextColors =
    [
        new("#FFFFFF", "흰색"),
        new("#FFFF00", "노란색"),
        new("#00FF00", "연두색"),
        new("#00FFFF", "하늘색"),
        new("#FF8888", "연분홍색"),
        new("#FFA500", "주황색"),
        new("#000000", "검은색")
    ];

    private static readonly IReadOnlyList<ColorChoice> OutlineColors =
    [
        new("#000000", "검은색"),
        new("#FFFFFF", "흰색"),
        new("#444444", "회색"),
        new("#2563EB", "파란색"),
        new("#990000", "빨간색")
    ];

    private static readonly IReadOnlyList<double> StrokeThicknesses = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0];
    private static readonly IReadOnlyList<string> DictionaryCategories =
    [
        UserDictionaryStore.UserCategory,
        UserDictionaryStore.QuickReplyCategory,
        UserDictionaryStore.UiCategory,
        UserDictionaryStore.RaceCategory,
        UserDictionaryStore.ItemCategory
    ];
    private static readonly IReadOnlyList<OverlayPreset> OverlayPresets =
    [
        new("기본", 22, "#FFFFFF", "#000000", 2.5, 0.92, "#99000000"),
        new("강조", 24, "#FFFF00", "#000000", 3.0, 0.95, "#B3000000"),
        new("원문 보호", 22, "#111111", "#FFFFFF", 3.0, 0.92, "#00000000"),
        new("밝은 배경용", 23, "#FFFFFF", "#000000", 3.0, 0.95, "#CC000000"),
        new("어두운 배경용", 22, "#111111", "#FFFFFF", 2.5, 0.88, "#80FFFFFF"),
        new("사용자 지정", 22, "#FFFFFF", "#000000", 2.5, 0.92, "#99000000")
    ];
    private readonly IWindowSource windowSource = new Win32WindowSource();
    private readonly ICaptureService dictionaryCaptureService = new WindowCaptureService();
    private readonly IOcrEngine dictionaryOcrEngine = new WindowsOcrEngine();
    private static readonly CaptureRegion FullWindowRegion = new(0, 0, 1, 1);
    private readonly ApiKeyStore apiKeyStore = new();
    private readonly AppSettingsStore settingsStore = new();
    private static readonly HttpClient httpClient = new();
    private readonly TranslationSession session;
    private AppSettings settings;
    private ResultWindow? resultWindow;
    private OverlayWindow? overlayWindow;
    private CaptureRegion? selectedRegion;
    private CancellationTokenSource? sessionCancellation;
    private bool applyingOverlayPreset;

    private readonly UserDictionaryStore userDictStore = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DiagnosticLogItem> diagnosticLogs = new();
    private readonly List<UserDictEntry> userDictionaryEntries = new();
    private int totalTranslationRequestCount;
    private int totalTranslationCharacterCount;

    public MainWindow()
    {
        settings = settingsStore.Load();
        InitializeComponent();
        var delegator = new TranslationServiceDelegator(
            httpClient,
            () => ApiKeyPasswordBox.Password,
            () => settings
        );
        var cachingTranslationService = new CachingTranslationService(delegator, new ScreenTranslationCacheStore());
        session = new TranslationSession(new WindowCaptureService(), new WindowsOcrEngine(), cachingTranslationService);
        session.BeforeCaptureAsync = SetOverlayCaptureVisibilityAsync(false);
        session.AfterCaptureAsync = SetOverlayCaptureVisibilityAsync(true);
        session.Updated += SessionUpdated;
        OcrLanguageComboBox.ItemsSource = OcrLanguages;
        var selectedOcr = OcrLanguages.FirstOrDefault(l => string.Equals(l.Tag, settings.OcrLanguageTag, StringComparison.OrdinalIgnoreCase)) ?? OcrLanguages[0];
        OcrLanguageComboBox.SelectedItem = selectedOcr;

        TargetLanguageComboBox.ItemsSource = TargetLanguages;
        var selectedTarget = TargetLanguages.FirstOrDefault(l => string.Equals(l.Code, settings.TargetLanguageCode, StringComparison.OrdinalIgnoreCase)) ?? TargetLanguages[0];
        TargetLanguageComboBox.SelectedItem = selectedTarget;
        DisplayModeComboBox.ItemsSource = DisplayModes;
        OverlayPresetComboBox.ItemsSource = OverlayPresets;
        OverlayPresetComboBox.SelectedItem = OverlayPresets.FirstOrDefault(preset => preset.Name == settings.OverlayPreset) ?? OverlayPresets[0];
        ApiKeyPasswordBox.Password = apiKeyStore.Load() ?? string.Empty;
        RestoreRegion(settings.LastRegion);
        UpdateRegionButtonVisual();
        DisplayModeComboBox.SelectedItem = DisplayModes.First(mode => mode.Mode == settings.DisplayMode);
        ShowOverlayInScreenShareCheckBox.IsChecked = settings.ShowOverlayInScreenShare;
        UpdateDisplayModePreview();

        // Populate and select font family
        var systemFonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).ToList();
        FontFamilyComboBox.ItemsSource = systemFonts;
        var selectedFont = systemFonts.FirstOrDefault(f => string.Equals(f, settings.FontFamily, StringComparison.OrdinalIgnoreCase)) ?? "Malgun Gothic";
        FontFamilyComboBox.SelectedItem = selectedFont;

        // Initialize font size slider and label
        FontSizeSlider.Value = settings.FontSize;
        FontSizeLabel.Text = $"{settings.FontSize}pt";

        // Populate and select text color swatches
        TextColorListBox.ItemsSource = TextColors;
        var selectedTextColor = TextColors.FirstOrDefault(c => string.Equals(c.Hex, settings.TextColor, StringComparison.OrdinalIgnoreCase)) ?? TextColors[0];
        TextColorListBox.SelectedItem = selectedTextColor;

        // Populate and select outline color swatches
        OutlineColorListBox.ItemsSource = OutlineColors;
        var selectedOutlineColor = OutlineColors.FirstOrDefault(c => string.Equals(c.Hex, settings.OutlineColor, StringComparison.OrdinalIgnoreCase)) ?? OutlineColors[0];
        OutlineColorListBox.SelectedItem = selectedOutlineColor;

        // Initialize stroke thickness slider and label
        StrokeThicknessSlider.Value = settings.StrokeThickness;
        StrokeThicknessLabel.Text = $"{settings.StrokeThickness:F1}px";
        OverlayOpacitySlider.Value = settings.OverlayOpacity;
        OverlayOpacityLabel.Text = $"{settings.OverlayOpacity:P0}";

        // Load User Dictionary
        userDictionaryEntries = userDictStore.Load();
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;
        DictCategoryComboBox.ItemsSource = DictionaryCategories;
        DictCategoryComboBox.SelectedItem = UserDictionaryStore.UserCategory;

        // Load Advanced Filter Settings to UI
        EnableLengthFilterCheckBox.IsChecked = settings.EnableLengthFilter;
        MinMessageLengthSlider.Value = settings.MinMessageLength;
        MinMessageLengthLabel.Text = $"{settings.MinMessageLength}자";
        MaxMessageLengthSlider.Value = settings.MaxMessageLength;
        MaxMessageLengthLabel.Text = $"{settings.MaxMessageLength}자";

        EnableNoiseFilterCheckBox.IsChecked = settings.EnableNoiseFilter;
        MaxNoiseTokenCountSlider.Value = settings.MaxNoiseTokenCount;
        MaxNoiseTokenCountLabel.Text = $"{settings.MaxNoiseTokenCount}개";

        EnableSeparatorFilterCheckBox.IsChecked = settings.EnableSeparatorFilter;
        MaxSeparatorsCountSlider.Value = settings.MaxSeparatorsCount;
        MaxSeparatorsCountLabel.Text = $"{settings.MaxSeparatorsCount}개";

        EnableSimilarityFilterCheckBox.IsChecked = settings.EnableSimilarityFilter;
        SimilarityThresholdSlider.Value = settings.SimilarityThreshold;
        SimilarityThresholdLabel.Text = $"{settings.SimilarityThreshold:F2}";
        ReplacementSimilarityThresholdSlider.Value = settings.ReplacementSimilarityThreshold;
        ReplacementSimilarityThresholdLabel.Text = $"{settings.ReplacementSimilarityThreshold:F2}";
        SimilarityCacheSecondsSlider.Value = settings.SimilarityCacheSeconds;
        SimilarityCacheSecondsLabel.Text = $"{settings.SimilarityCacheSeconds}초";

        // Bind Diagnostic Log List
        DiagnosticLogsListView.ItemsSource = diagnosticLogs;

        // Initialize filter panel enablement
        UpdateFilterPanelEnablement();

        UpdateFontPreview();

        ChatTranslationRadioButton.IsChecked = settings.TranslationMode == TranslationMode.Chat;
        ScreenTranslationRadioButton.IsChecked = settings.TranslationMode == TranslationMode.Screen;
        UpdateTranslationModeUI();

        RefreshWindows(this, new RoutedEventArgs());
        Closed += OnClosed;

        // Restore Translator selector and URL textbox states
        var selectedTranslatorTag = settings.TranslatorType.ToString();
        foreach (ComboBoxItem item in TranslatorTypeComboBox.Items)
        {
            if (item.Tag is string tag && tag == selectedTranslatorTag)
            {
                TranslatorTypeComboBox.SelectedItem = item;
                break;
            }
        }
        GoogleWebAppUrlTextBox.Text = settings.GoogleWebAppUrl;
        UpdateTranslatorPanelsVisibility(settings.TranslatorType);
    }

    private void RefreshWindows(object sender, RoutedEventArgs e)
    {
        var selectedHandle = (WindowComboBox.SelectedItem as CapturableWindow)?.Handle;
        var ownHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var windows = windowSource.ListWindows().Where(window => window.Handle != ownHandle).ToList();
        WindowComboBox.ItemsSource = windows;
        WindowComboBox.SelectedItem =
            windows.FirstOrDefault(window => window.Handle == selectedHandle)
            ?? FindSavedWindow(windows)
            ?? FindKartWindow(windows)
            ?? windows.FirstOrDefault();
        SetStatus(windows.Count == 0 ? "선택 가능한 창이 없습니다." : "게임 창을 선택하세요.");
    }

    private void SaveApiKey(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
        {
            SetStatus("저장할 API 키가 비어 있습니다.", true);
            return;
        }
        apiKeyStore.Save(ApiKeyPasswordBox.Password);
        SetStatus("API 키를 보호 저장했습니다.");
    }

    private void DeleteApiKey(object sender, RoutedEventArgs e)
    {
        apiKeyStore.Delete();
        ApiKeyPasswordBox.Clear();
        SetStatus("저장된 API 키를 삭제했습니다.");
    }

    private void UpdateTranslatorPanelsVisibility(TranslationServiceType translatorType)
    {
        if (DeepLSettingsPanel == null || GoogleUnofficialSettingsPanel == null || GoogleWebAppSettingsPanel == null)
            return;

        DeepLSettingsPanel.Visibility = translatorType == TranslationServiceType.DeepL ? Visibility.Visible : Visibility.Collapsed;
        GoogleUnofficialSettingsPanel.Visibility = translatorType == TranslationServiceType.GoogleUnofficial ? Visibility.Visible : Visibility.Collapsed;
        GoogleWebAppSettingsPanel.Visibility = translatorType == TranslationServiceType.GoogleWebApp ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TranslatorTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TranslatorTypeComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string tag)
        {
            if (Enum.TryParse<TranslationServiceType>(tag, out var translatorType))
            {
                settings = settings with { TranslatorType = translatorType };
                settingsStore.Save(settings);
                UpdateTranslatorPanelsVisibility(translatorType);
                SetStatus($"번역 서비스가 {selectedItem.Content}로 변경되었습니다.");
            }
        }
    }

    private void SaveGoogleWebAppUrl(object sender, RoutedEventArgs e)
    {
        var url = GoogleWebAppUrlTextBox.Text;
        settings = settings with { GoogleWebAppUrl = url ?? string.Empty };
        settingsStore.Save(settings);
        SetStatus("Google Web App URL을 저장했습니다.");
    }

    private void OpenDeepLApiKeysPage(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.deepl.com/ko/your-account/keys",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("Failed to open DeepL API Keys page", ex);
            SetStatus("링크를 열 수 없습니다.", true);
        }
    }

    private void WindowSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (WindowComboBox.SelectedItem is not CapturableWindow window || settings.LastWindowTitle is null)
        {
            return;
        }

        if (!string.Equals(window.Title, settings.LastWindowTitle, StringComparison.CurrentCulture)
            || !string.Equals(window.ProcessName, settings.LastWindowProcessName, StringComparison.OrdinalIgnoreCase))
        {
            selectedRegion = null;
            RegionText.Text = "선택되지 않음";
            UpdateRegionButtonVisual();
        }
    }

    private void TranslationModeChanged(object sender, RoutedEventArgs e)
    {
        if (settings == null || ChatTranslationRadioButton == null || ScreenTranslationRadioButton == null) return;
        var mode = ScreenTranslationRadioButton.IsChecked == true ? TranslationMode.Screen : TranslationMode.Chat;
        settings = settings with { TranslationMode = mode };
        settingsStore.Save(settings);
        UpdateTranslationModeUI();
    }

    private void UpdateTranslationModeUI()
    {
        if (ChatTranslationRadioButton.IsChecked == true)
        {
            SelectRegionButton.IsEnabled = true;
            if (selectedRegion is { } r)
            {
                ShowRegion(r);
            }
            else
            {
                RegionText.Text = "선택되지 않음";
            }
            UpdateRegionButtonVisual();
        }
        else
        {
            SelectRegionButton.IsEnabled = false;
            RegionText.Text = "전체 화면 (자동)";
            SelectRegionButton.ClearValue(BackgroundProperty);
            SelectRegionButton.ClearValue(ForegroundProperty);
            SelectRegionButton.ClearValue(FontWeightProperty);
        }
    }

    private void DisplayModeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DisplayModeComboBox.SelectedItem is not DisplayModeChoice choice)
        {
            return;
        }

        var displayMode = choice.Mode;
        settings = settings with { DisplayMode = displayMode };
        settingsStore.Save(settings);
        if (resultWindow is not null)
        {
            resultWindow.ApplyMode(displayMode);
        }
        UpdateDisplayModePreview();
    }

    private void ShowOverlayInScreenShareChanged(object sender, RoutedEventArgs e)
    {
        if (settings == null) return;
        settings = settings with { ShowOverlayInScreenShare = ShowOverlayInScreenShareCheckBox.IsChecked == true };
        settingsStore.Save(settings);

        if (overlayWindow is not null)
        {
            overlayWindow.ExcludeFromCapture = !settings.ShowOverlayInScreenShare;
            overlayWindow.UpdateDisplayAffinity();
        }
    }

    private void OcrLanguageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (OcrLanguageComboBox.SelectedItem is OcrLanguage ocrLanguage)
        {
            settings = settings with { OcrLanguageTag = ocrLanguage.Tag };
            settingsStore.Save(settings);
        }
    }

    private void TargetLanguageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (TargetLanguageComboBox.SelectedItem is TranslationLanguage targetLanguage)
        {
            settings = settings with { TargetLanguageCode = targetLanguage.Code };
            settingsStore.Save(settings);
        }
    }

    private void SelectRegion(object sender, RoutedEventArgs e)
    {
        if (WindowComboBox.SelectedItem is not CapturableWindow window)
        {
            SetStatus("먼저 게임 창을 선택하세요.", true);
            return;
        }
        var picker = new RegionSelectionWindow(window) { Owner = this };
        if (picker.ShowDialog() == true && picker.Region is { } region)
        {
            selectedRegion = region;
            ShowRegion(region);
            SaveSelection(window, region);
            SetStatus("번역 영역을 선택했습니다.");
            UpdateRegionButtonVisual();
        }
    }

    private async void ToggleSession(object sender, RoutedEventArgs e)
    {
        if (session.IsRunning)
        {
            await StopSessionAsync();
            return;
        }
        if (WindowComboBox.SelectedItem is not CapturableWindow window)
        {
            SetStatus("게임 창을 먼저 선택하세요.", true);
            return;
        }

        CaptureRegion region;
        if (settings.TranslationMode == TranslationMode.Screen)
        {
            region = new CaptureRegion(0, 0, 1, 1);
        }
        else
        {
            if (selectedRegion is not { } r)
            {
                SetStatus("번역 영역을 먼저 선택하세요.", true);
                return;
            }
            region = r;
        }

        if (OcrLanguageComboBox.SelectedItem is not OcrLanguage ocrLanguage || TargetLanguageComboBox.SelectedItem is not TranslationLanguage targetLanguage)
        {
            SetStatus("언어 설정을 확인하세요.", true);
            return;
        }
        sessionCancellation = new CancellationTokenSource();
        totalTranslationRequestCount = 0;
        totalTranslationCharacterCount = 0;
        UpdateApiUsageText();
        ShowTranslationOutput(window, region);

        var filterSettings = new FilterSettings(
            EnableLengthFilterCheckBox.IsChecked == true,
            (int)MinMessageLengthSlider.Value,
            (int)MaxMessageLengthSlider.Value,
            EnableNoiseFilterCheckBox.IsChecked == true,
            (int)MaxNoiseTokenCountSlider.Value,
            EnableSeparatorFilterCheckBox.IsChecked == true,
            (int)MaxSeparatorsCountSlider.Value,
            EnableSimilarityFilterCheckBox.IsChecked == true,
            SimilarityThresholdSlider.Value,
            ReplacementSimilarityThresholdSlider.Value,
            (int)SimilarityCacheSecondsSlider.Value
        );

        var userDict = userDictStore.Load();

        await session.StartAsync(new SessionOptions(
            new CaptureTarget(window), 
            region, 
            ocrLanguage, 
            targetLanguage, 
            TimeSpan.FromSeconds(1), 
            filterSettings, 
            userDict,
            settings.TranslationMode), 
            sessionCancellation.Token);

        if (settings.TranslationMode != TranslationMode.Screen)
        {
            SaveSelection(window, region);
        }
        else
        {
            settings = settings with { LastWindowTitle = window.Title, LastWindowProcessName = window.ProcessName };
            settingsStore.Save(settings);
        }
        StartStopButton.Content = "번역 정지 (F8)";
    }

    private async Task StopSessionAsync()
    {
        sessionCancellation?.Cancel();
        await session.StopAsync();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        StartStopButton.Content = "번역 시작 (F8)";
        overlayWindow?.ClearAll();
    }

    private void SessionUpdated(object? sender, SessionUpdate update) => Dispatcher.Invoke(() =>
    {
        SetStatus(update.Status, update.IsError);
        resultWindow?.Apply(update);
        if (overlayWindow is not null && WindowComboBox.SelectedItem is CapturableWindow window)
        {
            var region = settings.TranslationMode == TranslationMode.Screen
                ? new CaptureRegion(0, 0, 1, 1)
                : (selectedRegion ?? new CaptureRegion(0, 0, 1, 1));
            overlayWindow.PositionOver(window, region);
            overlayWindow.CurrentMode = settings.TranslationMode;
            overlayWindow.Topmost = false;
            overlayWindow.Topmost = true;
        }
        overlayWindow?.Apply(update);

        // Handle diagnostic log recording
        if (update.DiagnosticKind is DiagnosticKind.OcrTranslated or DiagnosticKind.OcrSkipped)
        {
            var logItem = new DiagnosticLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Status = update.Status,
                Source = update.OcrRawText ?? update.SourceText ?? string.Empty,
                Rule = update.FilterRule ?? string.Empty,
                Reason = update.FilterReason ?? string.Empty,
                ApiUsage = update.DiagnosticKind == DiagnosticKind.OcrTranslated
                    ? $"{update.TranslationRequestCount}건/{update.TranslationCharacterCount}자"
                    : "0건/0자"
            };
            diagnosticLogs.Insert(0, logItem);
            while (diagnosticLogs.Count > 100)
            {
                diagnosticLogs.RemoveAt(diagnosticLogs.Count - 1);
            }
        }

        if (update.DiagnosticKind == DiagnosticKind.OcrTranslated)
        {
            totalTranslationRequestCount = update.TotalTranslationRequestCount;
            totalTranslationCharacterCount = update.TotalTranslationCharacterCount;
            UpdateApiUsageText();
        }
        else if (update.DiagnosticKind == DiagnosticKind.OcrSkipped)
        {
            UpdateApiUsageText();
        }
    });

    private void UpdateApiUsageText()
    {
        if (ApiUsageText is not null)
        {
            ApiUsageText.Text = $"이번 세션 {totalTranslationRequestCount}건 / {totalTranslationCharacterCount}자";
        }
    }

    private Func<CancellationToken, Task> SetOverlayCaptureVisibilityAsync(bool visible, bool force = false) =>
        async ct =>
        {
            if (overlayWindow is null)
            {
                return;
            }

            if (!force && !settings.ShowOverlayInScreenShare)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() => overlayWindow.SetCaptureVisibility(visible));
            if (!visible)
            {
                await Task.Delay(60, ct);
            }
        };

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F8)
        {
            ToggleSession(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            if (session.IsRunning)
            {
                await StopSessionAsync();
            }
            SelectRegion(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await StopSessionAsync();
        if (resultWindow is not null)
        {
            resultWindow.Closed -= ResultWindowClosed;
            resultWindow.Close();
        }
        overlayWindow?.Close();
    }

    private void SetStatus(string status, bool isError = false)
    {
        StatusText.Text = status;
        StatusText.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#F1D6CF" : "#E2E8DD"));
    }

    private ResultWindow EnsureResultWindow()
    {
        if (resultWindow is not null)
        {
            return resultWindow;
        }

        resultWindow = new ResultWindow();
        resultWindow.ApplyMode(settings.DisplayMode);
        resultWindow.Closed += ResultWindowClosed;
        return resultWindow;
    }

    private void ShowTranslationOutput(CapturableWindow window, CaptureRegion region)
    {
        if (settings.DisplayMode == TranslationDisplayMode.TransparentOverlay)
        {
            resultWindow?.Close();
            resultWindow = null;
            overlayWindow ??= new OverlayWindow();
            overlayWindow.ClearAll();
            overlayWindow.CurrentMode = settings.TranslationMode;
            overlayWindow.PositionOver(window, region);
            overlayWindow.FontFamily = new FontFamily(settings.FontFamily);
            overlayWindow.FontSize = settings.FontSize;
            overlayWindow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.TextColor));
            overlayWindow.StrokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.OutlineColor));
            overlayWindow.StrokeThicknessValue = settings.StrokeThickness;
            overlayWindow.OverlayBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.OverlayBackgroundColor));
            overlayWindow.Opacity = settings.OverlayOpacity;
            overlayWindow.ExcludeFromCapture = !settings.ShowOverlayInScreenShare;
            overlayWindow.Show();
            overlayWindow.UpdateDisplayAffinity();
            return;
        }

        overlayWindow?.Close();
        overlayWindow = null;
        var result = EnsureResultWindow();
        result.Show();
        result.Activate();
    }

    private void ResultWindowClosed(object? sender, EventArgs e)
    {
        resultWindow = null;
    }

    private CapturableWindow? FindSavedWindow(IReadOnlyList<CapturableWindow> windows)
    {
        if (string.IsNullOrWhiteSpace(settings.LastWindowTitle))
        {
            return null;
        }

        return windows.FirstOrDefault(window =>
                   string.Equals(window.Title, settings.LastWindowTitle, StringComparison.CurrentCulture)
                   && string.Equals(window.ProcessName, settings.LastWindowProcessName, StringComparison.OrdinalIgnoreCase))
               ?? windows.FirstOrDefault(window =>
                   string.Equals(window.Title, settings.LastWindowTitle, StringComparison.CurrentCulture));
    }

    private void RestoreRegion(CaptureRegion? region)
    {
        if (region is not { Width: > 0, Height: > 0 } restored)
        {
            return;
        }

        selectedRegion = restored;
        ShowRegion(restored);
    }

    private void ShowRegion(CaptureRegion region)
    {
        RegionText.Text = $"{region.X:P0}, {region.Y:P0} / {region.Width:P0} x {region.Height:P0}";
    }

    private void SaveSelection(CapturableWindow window, CaptureRegion region)
    {
        settings = settings with { LastWindowTitle = window.Title, LastWindowProcessName = window.ProcessName, LastRegion = region };
        settingsStore.Save(settings);
    }

    private CapturableWindow? FindKartWindow(IReadOnlyList<CapturableWindow> windows)
    {
        return windows.FirstOrDefault(window =>
            (window.Title != null && window.Title.Contains("kart", StringComparison.OrdinalIgnoreCase)) ||
            (window.ProcessName != null && window.ProcessName.Contains("kart", StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateRegionButtonVisual()
    {
        if (selectedRegion == null)
        {
            SelectRegionButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65F2B"));
            SelectRegionButton.Foreground = Brushes.White;
            SelectRegionButton.FontWeight = FontWeights.Bold;
        }
        else
        {
            SelectRegionButton.ClearValue(BackgroundProperty);
            SelectRegionButton.ClearValue(ForegroundProperty);
            SelectRegionButton.ClearValue(FontWeightProperty);
        }
    }

    private void UpdateDisplayModePreview()
    {
        if (DisplayModeComboBox.SelectedItem is not DisplayModeChoice choice)
        {
            return;
        }

        if (choice.Mode == TranslationDisplayMode.Window)
        {
            ResultWindowPreviewGrid.Visibility = Visibility.Visible;
            OverlayPreviewGrid.Visibility = Visibility.Collapsed;
        }
        else if (choice.Mode == TranslationDisplayMode.TransparentOverlay)
        {
            ResultWindowPreviewGrid.Visibility = Visibility.Collapsed;
            OverlayPreviewGrid.Visibility = Visibility.Visible;
        }
    }

    private void UpdateFontPreview()
    {
        if (FontPreviewTextBlock == null) return;

        if (FontFamilyComboBox?.SelectedItem is string fontFamilyName)
        {
            FontPreviewTextBlock.FontFamily = new FontFamily(fontFamilyName);
        }
        if (FontSizeSlider != null)
        {
            FontPreviewTextBlock.FontSize = Math.Round(FontSizeSlider.Value);
        }
        if (TextColorListBox?.SelectedItem is ColorChoice textColor)
        {
            FontPreviewTextBlock.Fill = textColor.HexBrush;
        }
        if (OutlineColorListBox?.SelectedItem is ColorChoice outlineColor)
        {
            FontPreviewTextBlock.Stroke = outlineColor.HexBrush;
        }
        if (StrokeThicknessSlider != null)
        {
            FontPreviewTextBlock.StrokeThickness = Math.Round(StrokeThicknessSlider.Value, 1);
        }
    }

    private void FontFamilySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (FontFamilyComboBox.SelectedItem is string fontFamilyName)
        {
            settings = settings with { FontFamily = fontFamilyName, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
            settingsStore.Save(settings);
            UpdateFontPreview();
            if (overlayWindow is not null)
            {
                overlayWindow.FontFamily = new FontFamily(fontFamilyName);
            }
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settings == null || FontSizeLabel == null) return;
        double fontSize = Math.Round(e.NewValue);
        FontSizeLabel.Text = $"{fontSize}pt";
        settings = settings with { FontSize = fontSize, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
        settingsStore.Save(settings);
        UpdateFontPreview();
        if (overlayWindow is not null)
        {
            overlayWindow.FontSize = fontSize;
        }
    }

    private void TextColorSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (TextColorListBox.SelectedItem is ColorChoice color)
        {
            settings = settings with { TextColor = color.Hex, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
            settingsStore.Save(settings);
            UpdateFontPreview();
            if (overlayWindow is not null)
            {
                overlayWindow.Foreground = color.HexBrush;
            }
        }
    }

    private void OutlineColorSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (OutlineColorListBox.SelectedItem is ColorChoice color)
        {
            settings = settings with { OutlineColor = color.Hex, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
            settingsStore.Save(settings);
            UpdateFontPreview();
            if (overlayWindow is not null)
            {
                overlayWindow.StrokeBrush = color.HexBrush;
            }
        }
    }

    private void StrokeThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settings == null || StrokeThicknessLabel == null) return;
        double thickness = Math.Round(e.NewValue, 1);
        StrokeThicknessLabel.Text = $"{thickness:F1}px";
        settings = settings with { StrokeThickness = thickness, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
        settingsStore.Save(settings);
        UpdateFontPreview();
        if (overlayWindow is not null)
        {
            overlayWindow.StrokeThicknessValue = thickness;
        }
    }

    private void OverlayPresetSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null || OverlayPresetComboBox?.SelectedItem is not OverlayPreset preset)
        {
            return;
        }

        ApplyOverlayPreset(preset);
    }

    private void ApplyOverlayPreset(OverlayPreset preset)
    {
        settings = settings with
        {
            OverlayPreset = preset.Name,
            FontSize = preset.FontSize,
            TextColor = preset.TextColor,
            OutlineColor = preset.OutlineColor,
            StrokeThickness = preset.StrokeThickness,
            OverlayOpacity = preset.OverlayOpacity,
            OverlayBackgroundColor = preset.BackgroundColor
        };
        settingsStore.Save(settings);

        applyingOverlayPreset = true;
        try
        {
            FontSizeSlider.Value = preset.FontSize;
            FontSizeLabel.Text = $"{preset.FontSize}pt";
            TextColorListBox.SelectedItem = TextColors.FirstOrDefault(color => string.Equals(color.Hex, preset.TextColor, StringComparison.OrdinalIgnoreCase));
            OutlineColorListBox.SelectedItem = OutlineColors.FirstOrDefault(color => string.Equals(color.Hex, preset.OutlineColor, StringComparison.OrdinalIgnoreCase));
            StrokeThicknessSlider.Value = preset.StrokeThickness;
            StrokeThicknessLabel.Text = $"{preset.StrokeThickness:F1}px";
            OverlayOpacitySlider.Value = preset.OverlayOpacity;
            OverlayOpacityLabel.Text = $"{preset.OverlayOpacity:P0}";
        }
        finally
        {
            applyingOverlayPreset = false;
        }
        UpdateFontPreview();

        if (overlayWindow is not null)
        {
            overlayWindow.FontSize = preset.FontSize;
            overlayWindow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.TextColor));
            overlayWindow.StrokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.OutlineColor));
            overlayWindow.StrokeThicknessValue = preset.StrokeThickness;
            overlayWindow.OverlayBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.BackgroundColor));
            overlayWindow.Opacity = preset.OverlayOpacity;
        }
    }

    private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settings == null || OverlayOpacityLabel == null)
        {
            return;
        }

        var opacity = Math.Round(e.NewValue, 2);
        OverlayOpacityLabel.Text = $"{opacity:P0}";
        settings = settings with { OverlayOpacity = opacity, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
        settingsStore.Save(settings);
        if (overlayWindow is not null)
        {
            overlayWindow.Opacity = opacity;
        }
    }

    private void FilterSettingChanged(object sender, RoutedEventArgs e)
    {
        if (settings == null || MinMessageLengthSlider == null || MaxMessageLengthSlider == null || 
            MaxNoiseTokenCountSlider == null || MaxSeparatorsCountSlider == null || 
            SimilarityThresholdSlider == null || ReplacementSimilarityThresholdSlider == null || 
            SimilarityCacheSecondsSlider == null)
        {
            return;
        }

        // Update Labels
        MinMessageLengthLabel.Text = $"{(int)MinMessageLengthSlider.Value}자";
        MaxMessageLengthLabel.Text = $"{(int)MaxMessageLengthSlider.Value}자";
        MaxNoiseTokenCountLabel.Text = $"{(int)MaxNoiseTokenCountSlider.Value}개";
        MaxSeparatorsCountLabel.Text = $"{(int)MaxSeparatorsCountSlider.Value}개";
        SimilarityThresholdLabel.Text = $"{SimilarityThresholdSlider.Value:F2}";
        ReplacementSimilarityThresholdLabel.Text = $"{ReplacementSimilarityThresholdSlider.Value:F2}";
        SimilarityCacheSecondsLabel.Text = $"{(int)SimilarityCacheSecondsSlider.Value}초";

        // Enablement
        UpdateFilterPanelEnablement();

        // Update Settings object and Save
        settings = settings with
        {
            EnableLengthFilter = EnableLengthFilterCheckBox.IsChecked == true,
            MinMessageLength = (int)MinMessageLengthSlider.Value,
            MaxMessageLength = (int)MaxMessageLengthSlider.Value,
            EnableNoiseFilter = EnableNoiseFilterCheckBox.IsChecked == true,
            MaxNoiseTokenCount = (int)MaxNoiseTokenCountSlider.Value,
            EnableSeparatorFilter = EnableSeparatorFilterCheckBox.IsChecked == true,
            MaxSeparatorsCount = (int)MaxSeparatorsCountSlider.Value,
            EnableSimilarityFilter = EnableSimilarityFilterCheckBox.IsChecked == true,
            SimilarityThreshold = Math.Round(SimilarityThresholdSlider.Value, 2),
            ReplacementSimilarityThreshold = Math.Round(ReplacementSimilarityThresholdSlider.Value, 2),
            SimilarityCacheSeconds = (int)SimilarityCacheSecondsSlider.Value
        };
        settingsStore.Save(settings);
    }

    private void UpdateFilterPanelEnablement()
    {
        if (LengthFilterSettingsPanel != null && EnableLengthFilterCheckBox != null)
        {
            LengthFilterSettingsPanel.IsEnabled = EnableLengthFilterCheckBox.IsChecked == true;
        }
        if (NoiseFilterSettingsPanel != null && EnableNoiseFilterCheckBox != null)
        {
            NoiseFilterSettingsPanel.IsEnabled = EnableNoiseFilterCheckBox.IsChecked == true;
        }
        if (SeparatorFilterSettingsPanel != null && EnableSeparatorFilterCheckBox != null)
        {
            SeparatorFilterSettingsPanel.IsEnabled = EnableSeparatorFilterCheckBox.IsChecked == true;
        }
        if (SimilarityFilterSettingsPanel != null && EnableSimilarityFilterCheckBox != null)
        {
            SimilarityFilterSettingsPanel.IsEnabled = EnableSimilarityFilterCheckBox.IsChecked == true;
        }
    }

    private void AddDictionaryEntry(object sender, RoutedEventArgs e)
    {
        var source = DictSourceTextBox.Text?.Trim();
        var target = DictTargetTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            SetStatus("사전에 추가할 단어와 대체 번역어를 입력해 주세요.", true);
            return;
        }

        if (userDictionaryEntries.Any(entry => string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("이미 사전에 존재하는 원문 단어입니다.", true);
            return;
        }

        var category = DictCategoryComboBox.SelectedItem as string ?? UserDictionaryStore.UserCategory;
        var entry = new UserDictEntry(source, target, category);
        userDictionaryEntries.Add(entry);
        userDictStore.Save(userDictionaryEntries);

        // Refresh DataGrid
        UserDictionaryDataGrid.ItemsSource = null;
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;

        DictSourceTextBox.Clear();
        DictTargetTextBox.Clear();
        SetStatus($"사전에 단어 '{source}'를 추가했습니다.");
    }

    private async void FillDictionarySourceFromOcr(object sender, RoutedEventArgs e)
    {
        if (WindowComboBox.SelectedItem is not CapturableWindow window)
        {
            SetStatus("먼저 OCR할 게임 창을 선택하세요.", true);
            return;
        }

        if (OcrLanguageComboBox.SelectedItem is not OcrLanguage ocrLanguage)
        {
            SetStatus("OCR 언어를 먼저 선택하세요.", true);
            return;
        }

        if (session.IsRunning)
        {
            await StopSessionAsync();
            SetStatus("사전 OCR을 위해 번역을 잠시 정지했습니다.");
        }

        var picker = new RegionSelectionWindow(window) { Owner = this };
        SetStatus("사전에 추가할 원문 글자 영역을 드래그하세요.");
        if (picker.ShowDialog() != true || picker.Region is not { } region)
        {
            SetStatus("사전 OCR 영역 선택을 취소했습니다.");
            return;
        }

        FillDictSourceFromOcrButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await SetOverlayCaptureVisibilityAsync(false, force: true)(cts.Token);
            await Task.Delay(120, cts.Token);
            var frame = await dictionaryCaptureService.CaptureAsync(new CaptureTarget(window), FullWindowRegion, cts.Token);
            var source = await RecognizeDictionaryTextAsync(frame, region, ocrLanguage, cts.Token);

            if (string.IsNullOrWhiteSpace(source))
            {
                SetStatus("선택 영역에서 OCR 텍스트를 찾지 못했습니다.", true);
                return;
            }

            DictSourceTextBox.Text = source;
            DictTargetTextBox.Focus();
            DictTargetTextBox.SelectAll();
            SetStatus($"OCR 원문을 입력했습니다: {source}");
        }
        catch (Exception ex)
        {
            AppLog.Write("Dictionary OCR capture failed", ex);
            SetStatus($"사전 OCR 실패: {ex.Message}", true);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            FillDictSourceFromOcrButton.IsEnabled = true;
            try
            {
                await SetOverlayCaptureVisibilityAsync(true, force: true)(CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Write("Failed to restore overlay visibility after dictionary OCR", ex);
            }
        }
    }

    private static string NormalizeDictionaryOcrText(string text)
    {
        return Regex.Replace(text.Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
    }

    private async Task<string> RecognizeDictionaryTextAsync(CapturedFrame frame, CaptureRegion selectedRegion, OcrLanguage language, CancellationToken ct)
    {
        var recognized = await dictionaryOcrEngine.RecognizeAsync(frame, language, ct);
        return ExtractDictionaryTextFromSelection(recognized, selectedRegion, frame.Bitmap.PixelWidth, frame.Bitmap.PixelHeight);
    }

    private static string ExtractDictionaryTextFromSelection(OcrResult recognized, CaptureRegion selectedRegion, int frameWidth, int frameHeight)
    {
        var selectedRect = ToRect(selectedRegion.ToPixels(frameWidth, frameHeight));
        selectedRect.Inflate(4, 4);

        var selectedWords = recognized.Words
            .Where(word => Intersects(word.BoundingRect, selectedRect))
            .Select(word => ClipWordToSelection(word, selectedRect))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        var wordText = NormalizeDictionaryOcrText(string.Concat(selectedWords));
        if (!string.IsNullOrWhiteSpace(wordText))
        {
            return wordText;
        }

        var selectedLines = recognized.Lines
            .Where(line => Intersects(line.BoundingRect, selectedRect))
            .Select(line => line.Text);

        return NormalizeDictionaryOcrText(string.Join(" ", selectedLines));
    }

    private static Rect ToRect(Int32Rect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static string ClipWordToSelection(OcrWordResult word, Rect selectedRect)
    {
        var text = word.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var intersection = word.BoundingRect;
        intersection.Intersect(selectedRect);
        if (intersection.IsEmpty || word.BoundingRect.Width <= 0 || text.Length <= 1)
        {
            return intersection.IsEmpty ? string.Empty : text;
        }

        var charWidth = word.BoundingRect.Width / text.Length;
        var start = Math.Clamp((int)Math.Floor((intersection.Left - word.BoundingRect.Left) / charWidth), 0, text.Length - 1);
        var end = Math.Clamp((int)Math.Ceiling((intersection.Right - word.BoundingRect.Left) / charWidth), start + 1, text.Length);
        return text[start..end];
    }

    private static bool Intersects(Rect a, Rect b)
    {
        a.Intersect(b);
        return !a.IsEmpty && a.Width > 0 && a.Height > 0;
    }

    private void DeleteDictionaryEntry(object sender, RoutedEventArgs e)
    {
        if (UserDictionaryDataGrid.SelectedItem is not UserDictEntry selectedEntry)
        {
            SetStatus("삭제할 사전 항목을 선택해 주세요.", true);
            return;
        }

        userDictionaryEntries.Remove(selectedEntry);
        userDictStore.Save(userDictionaryEntries);

        // Refresh DataGrid
        UserDictionaryDataGrid.ItemsSource = null;
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;

        SetStatus($"사전에서 단어 '{selectedEntry.Source}'를 삭제했습니다.");
    }

    private void RestoreDefaultDictionary(object sender, RoutedEventArgs e)
    {
        int addedCount = 0;
        foreach (var defaultEntry in UserDictionaryStore.DefaultDictionary)
        {
            if (!userDictionaryEntries.Any(entry => string.Equals(entry.Source, defaultEntry.Source, StringComparison.OrdinalIgnoreCase)))
            {
                userDictionaryEntries.Add(defaultEntry);
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            userDictStore.Save(userDictionaryEntries);
            UserDictionaryDataGrid.ItemsSource = null;
            UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;
            SetStatus($"기본 사전 항목 {addedCount}개를 추가했습니다.");
        }
        else
        {
            SetStatus("모든 기본 사전 항목이 이미 사전에 존재합니다.");
        }
    }

    private void ClearDiagnosticLogs(object sender, RoutedEventArgs e)
    {
        diagnosticLogs.Clear();
        SetStatus("진단 로그를 비웠습니다.");
    }

    private void RunManualOcrParserTest(object sender, RoutedEventArgs e)
    {
        var raw = ManualOcrTestTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            SetStatus("테스트할 OCR 텍스트를 입력해 주세요.", true);
            return;
        }

        var language = OcrLanguageComboBox.SelectedItem as OcrLanguage ?? OcrLanguages[0];
        var filter = new FilterSettings(
            EnableLengthFilterCheckBox.IsChecked == true,
            (int)MinMessageLengthSlider.Value,
            (int)MaxMessageLengthSlider.Value,
            EnableNoiseFilterCheckBox.IsChecked == true,
            (int)MaxNoiseTokenCountSlider.Value,
            EnableSeparatorFilterCheckBox.IsChecked == true,
            (int)MaxSeparatorsCountSlider.Value,
            EnableSimilarityFilterCheckBox.IsChecked == true,
            SimilarityThresholdSlider.Value,
            ReplacementSimilarityThresholdSlider.Value,
            (int)SimilarityCacheSecondsSlider.Value);

        var lines = ChatLineParser.Parse(raw);
        if (lines.Count == 0)
        {
            diagnosticLogs.Insert(0, new DiagnosticLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Status = "수동 테스트",
                Source = raw,
                Rule = "ChatLineParser",
                Reason = "파싱 실패"
            });
            SetStatus("수동 테스트: 채팅 줄을 파싱하지 못했습니다.", true);
            return;
        }

        foreach (var line in lines)
        {
            var quality = ChatQualityFilter.Check(line, language, filter);
            diagnosticLogs.Insert(0, new DiagnosticLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Status = quality.Action.ToString(),
                Source = line.SourceLine,
                Rule = quality.Rule ?? "Translate",
                Reason = quality.Reason ?? "번역 대상"
            });
        }

        while (diagnosticLogs.Count > 100)
        {
            diagnosticLogs.RemoveAt(diagnosticLogs.Count - 1);
        }

        SetStatus($"수동 테스트: {lines.Count}개 줄을 분석했습니다.");
    }
}

public sealed class DiagnosticLogItem
{
    public string Time { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ApiUsage { get; set; } = string.Empty;
}
