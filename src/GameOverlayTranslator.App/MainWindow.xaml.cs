using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Net.Http;
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
    private readonly IWindowSource windowSource = new Win32WindowSource();
    private readonly ApiKeyStore apiKeyStore = new();
    private readonly AppSettingsStore settingsStore = new();
    private readonly ITranslationSession session;
    private AppSettings settings;
    private ResultWindow? resultWindow;
    private OverlayWindow? overlayWindow;
    private CaptureRegion? selectedRegion;
    private CancellationTokenSource? sessionCancellation;

    private readonly UserDictionaryStore userDictStore = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DiagnosticLogItem> diagnosticLogs = new();
    private readonly List<UserDictEntry> userDictionaryEntries = new();

    public MainWindow()
    {
        settings = settingsStore.Load();
        InitializeComponent();
        session = new TranslationSession(new WindowCaptureService(), new WindowsOcrEngine(), new DeepLTranslationService(new HttpClient(), () => ApiKeyPasswordBox.Password));
        session.Updated += SessionUpdated;
        OcrLanguageComboBox.ItemsSource = OcrLanguages;
        var selectedOcr = OcrLanguages.FirstOrDefault(l => string.Equals(l.Tag, settings.OcrLanguageTag, StringComparison.OrdinalIgnoreCase)) ?? OcrLanguages[0];
        OcrLanguageComboBox.SelectedItem = selectedOcr;

        TargetLanguageComboBox.ItemsSource = TargetLanguages;
        var selectedTarget = TargetLanguages.FirstOrDefault(l => string.Equals(l.Code, settings.TargetLanguageCode, StringComparison.OrdinalIgnoreCase)) ?? TargetLanguages[0];
        TargetLanguageComboBox.SelectedItem = selectedTarget;
        DisplayModeComboBox.ItemsSource = DisplayModes;
        ApiKeyPasswordBox.Password = apiKeyStore.Load() ?? string.Empty;
        RestoreRegion(settings.LastRegion);
        UpdateRegionButtonVisual();
        DisplayModeComboBox.SelectedItem = DisplayModes.First(mode => mode.Mode == settings.DisplayMode);
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

        // Load User Dictionary
        userDictionaryEntries = userDictStore.Load();
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;

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

        // Initialize translation mode RadioButtons
        if (settings.TranslationMode == TranslationMode.Screen)
        {
            ScreenTranslationRadioButton.IsChecked = true;
        }
        else
        {
            ChatTranslationRadioButton.IsChecked = true;
        }
        UpdateTranslationModeUI();

        RefreshWindows(this, new RoutedEventArgs());
        Closed += OnClosed;
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
            TimeSpan.FromMilliseconds(900), 
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
            overlayWindow.Topmost = false;
            overlayWindow.Topmost = true;
        }
        overlayWindow?.Apply(update);

        // Handle diagnostic log recording
        if (!string.IsNullOrWhiteSpace(update.OcrRawText) || 
            !string.IsNullOrWhiteSpace(update.SourceText) || 
            !string.IsNullOrWhiteSpace(update.FilterRule))
        {
            var logItem = new DiagnosticLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Status = update.Status,
                Source = update.OcrRawText ?? update.SourceText ?? string.Empty,
                Rule = update.FilterRule ?? string.Empty,
                Reason = update.FilterReason ?? string.Empty
            };
            diagnosticLogs.Insert(0, logItem);
            while (diagnosticLogs.Count > 100)
            {
                diagnosticLogs.RemoveAt(diagnosticLogs.Count - 1);
            }
        }
    });

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
            overlayWindow.PositionOver(window, region);
            overlayWindow.FontFamily = new FontFamily(settings.FontFamily);
            overlayWindow.FontSize = settings.FontSize;
            overlayWindow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.TextColor));
            overlayWindow.StrokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.OutlineColor));
            overlayWindow.StrokeThicknessValue = settings.StrokeThickness;
            overlayWindow.Show();
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
            settings = settings with { FontFamily = fontFamilyName };
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
        settings = settings with { FontSize = fontSize };
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
            settings = settings with { TextColor = color.Hex };
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
            settings = settings with { OutlineColor = color.Hex };
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
        settings = settings with { StrokeThickness = thickness };
        settingsStore.Save(settings);
        UpdateFontPreview();
        if (overlayWindow is not null)
        {
            overlayWindow.StrokeThicknessValue = thickness;
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

        var entry = new UserDictEntry(source, target);
        userDictionaryEntries.Add(entry);
        userDictStore.Save(userDictionaryEntries);

        // Refresh DataGrid
        UserDictionaryDataGrid.ItemsSource = null;
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;

        DictSourceTextBox.Clear();
        DictTargetTextBox.Clear();
        SetStatus($"사전에 단어 '{source}'를 추가했습니다.");
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
}

public sealed class DiagnosticLogItem
{
    public string Time { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
