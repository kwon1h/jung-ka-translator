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

    private sealed record OcrEngineChoice(OcrEngineType Engine, string Name)
    {
        public override string ToString() => Name;
    }

    private static readonly IReadOnlyList<OcrEngineChoice> OcrEngines =
    [
        new(OcrEngineType.Windows, "Windows OCR (기본)"),
        new(OcrEngineType.PaddleOCR, "PaddleOCR (OpenVINO)")
    ];

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

    private sealed record ReadinessItem(TextBlock? Target, bool IsReady, string ReadyText, string MissingText);

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

    private static readonly IReadOnlyList<ColorChoice> BackgroundColors =
    [
        new("#000000", "검은색"),
        new("#FFFFFF", "흰색"),
        new("#333333", "회색"),
        new("#2563EB", "파란색"),
        new("#EF4444", "빨간색"),
        new("#10B981", "초록색")
    ];

    private static readonly IReadOnlyList<double> StrokeThicknesses = [0.0, 0.5, 1.0];
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
        new("기본", AppSettingsDefaults.DefaultFontSize, "#FFFFFF", "#000000", AppSettingsDefaults.DefaultStrokeThickness, 0.92, "#99000000"),
        new("강조", 25, "#FFFF00", "#000000", 1.0, 0.95, "#B3000000"),
        new("원문 보호", 25, "#111111", "#FFFFFF", 1.0, 0.92, "#00000000"),
        new("밝은 배경용", 25, "#FFFFFF", "#000000", 1.0, 0.95, "#CC000000"),
        new("어두운 배경용", 25, "#111111", "#FFFFFF", AppSettingsDefaults.DefaultStrokeThickness, 0.88, "#80FFFFFF"),
        new("사용자 지정", AppSettingsDefaults.DefaultFontSize, "#FFFFFF", "#000000", AppSettingsDefaults.DefaultStrokeThickness, 0.92, "#99000000")
    ];
    private readonly IWindowSource windowSource = new Win32WindowSource();
    private readonly ICaptureService dictionaryCaptureService = new WindowCaptureService();
    private readonly WindowsOcrEngine windowsOcrEngine = new();
    private readonly PaddleOcrEngine paddleOcrEngine = new();
    private readonly IOcrEngine delegatingOcrEngine;
    private readonly IOcrEngine dictionaryOcrEngine;
    private static readonly CaptureRegion FullWindowRegion = new(0, 0, 1, 1);
    private readonly ApiKeyStore apiKeyStore = new();
    private readonly AppSettingsStore settingsStore = new();
    private static readonly HttpClient httpClient = new();
    private readonly TranslationSession session;
    private readonly ITranslationService chatTranslationService;
    private AppSettings settings;
    private ResultWindow? resultWindow;
    private OverlayWindow? overlayWindow;
    private List<CaptureRegion> selectedChatRegions = [];
    private CaptureRegion? selectedScreenRegion;
    private CaptureRegion? SelectedRegion =>
        ScreenTranslationRadioButton?.IsChecked == true
            ? selectedScreenRegion
            : selectedChatRegions.Count > 0 ? selectedChatRegions[0] : null;
    private List<CaptureRegion> excludedRegions = [];
    private CaptureRegion? activeSessionRegion;
    private TranslationMode? activeSessionMode;
    private CancellationTokenSource? sessionCancellation;
    private bool applyingOverlayPreset;
    private bool restoringSettings = true;

    private readonly UserDictionaryStore userDictStore = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DiagnosticLogItem> diagnosticLogs = new();
    private readonly List<UserDictEntry> userDictionaryEntries = new();
    private int totalTranslationRequestCount;
    private int totalTranslationCharacterCount;

    public MainWindow()
    {
        settings = settingsStore.Load();
        InitializeComponent();
        delegatingOcrEngine = new DelegatingOcrEngine(() => settings.OcrEngineType, windowsOcrEngine, paddleOcrEngine);
        dictionaryOcrEngine = delegatingOcrEngine;
        var delegator = new TranslationServiceDelegator(
            httpClient,
            () => ApiKeyPasswordBox.Password,
            () => settings
        );
        chatTranslationService = delegator;
        var cachingTranslationService = new CachingTranslationService(delegator, new ScreenTranslationCacheStore());
        session = new TranslationSession(new WindowCaptureService(), delegatingOcrEngine, cachingTranslationService);
        session.BeforeCaptureAsync = SetOverlayCaptureVisibilityAsync(false);
        session.AfterCaptureAsync = SetOverlayCaptureVisibilityAsync(true);
        session.Updated += SessionUpdated;

        OcrEngineComboBox.ItemsSource = OcrEngines;
        OcrEngineComboBox.SelectedItem = OcrEngines.FirstOrDefault(e => e.Engine == settings.OcrEngineType) ?? OcrEngines[0];

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
        RestoreRegions(settings.LastChatRegions, settings.LastChatRegion, settings.LastScreenRegion);
        RestoreExcludedRegions(settings.LastExcludedRegions, settings.LastExcludedRegion);
        UpdateRegionButtonVisual();
        DisplayModeComboBox.SelectedItem = DisplayModes.First(mode => mode.Mode == settings.DisplayMode);
        ShowOverlayInScreenShareCheckBox.IsChecked = settings.ShowOverlayInScreenShare;
        UpdateDisplayModePreview();

        // Populate and select font family
        var systemFonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(fontName => !fontName.StartsWith("HY", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s)
            .ToList();
        var preferredFont = systemFonts.FirstOrDefault(f => string.Equals(f, AppSettingsDefaults.PreferredFontFamily, StringComparison.OrdinalIgnoreCase));
        var savedFont = systemFonts.FirstOrDefault(f => string.Equals(f, settings.FontFamily, StringComparison.OrdinalIgnoreCase));
        if (savedFont is null && !string.IsNullOrWhiteSpace(settings.FontFamily))
        {
            systemFonts.Insert(0, settings.FontFamily);
            savedFont = settings.FontFamily;
        }
        FontFamilyComboBox.ItemsSource = systemFonts;
        var selectedFont = savedFont ?? preferredFont ?? AppSettingsDefaults.LegacyFontFamily;
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
        OverlayDurationSlider.Value = settings.OverlayDurationSeconds;
        OverlayDurationLabel.Text = $"{settings.OverlayDurationSeconds:0}초";

        // Populate and select background color swatches and opacity
        var (bgRgb, bgOpacity) = SplitArgbHex(settings.OverlayBackgroundColor);
        BackgroundColorsListBox.ItemsSource = BackgroundColors;
        var selectedBgColor = BackgroundColors.FirstOrDefault(c => string.Equals(c.Hex, bgRgb, StringComparison.OrdinalIgnoreCase)) ?? BackgroundColors[0];
        BackgroundColorsListBox.SelectedItem = selectedBgColor;

        // Initialize background opacity slider and label
        BackgroundOpacitySlider.Value = bgOpacity;
        BackgroundOpacityLabel.Text = $"{bgOpacity:P0}";

        // Load User Dictionary
        userDictionaryEntries = userDictStore.Load();
        UserDictionaryDataGrid.ItemsSource = userDictionaryEntries;
        DictCategoryComboBox.ItemsSource = DictionaryCategories;
        DictCategoryComboBox.SelectedItem = UserDictionaryStore.UserCategory;

        // Bind Diagnostic Log List
        DiagnosticLogsListView.ItemsSource = diagnosticLogs;

        UpdateFontPreview();

        ChatTranslationRadioButton.IsChecked = settings.TranslationMode == TranslationMode.Chat;
        ScreenTranslationRadioButton.IsChecked = settings.TranslationMode == TranslationMode.Screen;
        UpdateTranslationModeUI();
        restoringSettings = false;

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

    private void MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTranslationModeUI();
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
        UpdateStartReadiness();
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
        UpdateStartReadiness();
    }

    private void DeleteApiKey(object sender, RoutedEventArgs e)
    {
        apiKeyStore.Delete();
        ApiKeyPasswordBox.Clear();
        SetStatus("저장된 API 키를 삭제했습니다.");
        UpdateStartReadiness();
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
                UpdateStartReadiness();
            }
        }
    }

    private void SaveGoogleWebAppUrl(object sender, RoutedEventArgs e)
    {
        var url = GoogleWebAppUrlTextBox.Text;
        settings = settings with { GoogleWebAppUrl = url ?? string.Empty };
        settingsStore.Save(settings);
        SetStatus("Google Web App URL을 저장했습니다.");
        UpdateStartReadiness();
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
            selectedChatRegions.Clear();
            selectedScreenRegion = null;
            excludedRegions.Clear();
            RegionText.Text = "선택되지 않음";
            UpdateRegionButtonVisual();
            UpdateTranslationModeUI();
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
        var screenMode = ScreenTranslationRadioButton.IsChecked == true;
        DisplayModeComboBox.IsEnabled = !screenMode;
        DisplayModeComboBox.ToolTip = screenMode ? "전체화면 번역은 선택 영역 오버레이로 표시됩니다." : null;
        if (screenMode && settings.DisplayMode != TranslationDisplayMode.TransparentOverlay)
        {
            settings = settings with { DisplayMode = TranslationDisplayMode.TransparentOverlay };
            settingsStore.Save(settings);
            DisplayModeComboBox.SelectedItem = DisplayModes.First(choice => choice.Mode == TranslationDisplayMode.TransparentOverlay);
            UpdateDisplayModePreview();
        }

        SelectRegionButton.IsEnabled = true;
        if (ScreenTranslationRadioButton.IsChecked == true && selectedScreenRegion is { } screenRegion)
        {
            ShowRegion(screenRegion);
        }
        else if (ChatTranslationRadioButton.IsChecked == true && selectedChatRegions.Count > 0)
        {
            ShowRegionSummary();
        }
        else if (ScreenTranslationRadioButton.IsChecked == true)
        {
            RegionText.Text = "전체 화면 (기본)";
        }
        else
        {
            RegionText.Text = "선택되지 않음";
        }

        if (ClearRegionButton != null)
        {
            ClearRegionButton.IsEnabled = SelectedRegion is not null;
        }

        UpdateRegionButtonVisual();

        UpdateStartReadiness();
    }

    private void DisplayModeSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DisplayModeComboBox.SelectedItem is not DisplayModeChoice choice)
        {
            return;
        }

        if (ScreenTranslationRadioButton.IsChecked == true && choice.Mode != TranslationDisplayMode.TransparentOverlay)
        {
            DisplayModeComboBox.SelectedItem = DisplayModes.First(mode => mode.Mode == TranslationDisplayMode.TransparentOverlay);
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
        if (settings == null || restoringSettings) return;
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
            UpdateStartReadiness();
        }
    }

    private void OcrEngineSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (OcrEngineComboBox.SelectedItem is OcrEngineChoice choice)
        {
            settings = settings with { OcrEngineType = choice.Engine };
            settingsStore.Save(settings);
            SetStatus($"OCR 엔진이 {choice.Name}으로 변경되었습니다.");
            UpdateStartReadiness();
        }
    }

    private void TargetLanguageSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null) return;
        if (TargetLanguageComboBox.SelectedItem is TranslationLanguage targetLanguage)
        {
            settings = settings with { TargetLanguageCode = targetLanguage.Code };
            settingsStore.Save(settings);
            UpdateStartReadiness();
        }
    }

    private void SelectRegion(object sender, RoutedEventArgs e)
    {
        if (WindowComboBox.SelectedItem is not CapturableWindow window)
        {
            SetStatus("먼저 게임 창을 선택하세요.", true);
            return;
        }
        var screenMode = ScreenTranslationRadioButton.IsChecked == true;
        IReadOnlyList<CaptureRegion> included = screenMode
            ? selectedScreenRegion is { } screenRegion ? [screenRegion] : []
            : selectedChatRegions;
        var picker = new RegionSelectionWindow(window, included, excludedRegions, allowMultipleIncluded: !screenMode) { Owner = this };
        if (ShowRegionDialog(picker) == true)
        {
            if (screenMode)
            {
                selectedScreenRegion = picker.Regions.Count > 0 ? picker.Regions[0] : null;
            }
            else
            {
                selectedChatRegions = picker.Regions.ToList();
            }
            excludedRegions = picker.ExcludedRegions.ToList();
            SaveSelections(window);
            SetStatus($"영역 편집을 저장했습니다. 번역 {picker.Regions.Count}개 · 제외 {picker.ExcludedRegions.Count}개");
            UpdateTranslationModeUI();
        }
    }

    private void ClearRegion(object sender, RoutedEventArgs e)
    {
        if (ScreenTranslationRadioButton.IsChecked == true)
        {
            selectedScreenRegion = null;
            settings = settings with { LastScreenRegion = null };
        }
        else
        {
            selectedChatRegions.Clear();
            settings = settings with { LastChatRegion = null, LastRegion = null, LastChatRegions = null };
        }
        settingsStore.Save(settings);
        UpdateTranslationModeUI();
        SetStatus(settings.TranslationMode == TranslationMode.Screen
            ? "전체화면 번역 영역을 전체 화면으로 변경했습니다."
            : "번역 영역을 해제했습니다.");
    }

    private bool UpdateStartReadiness()
    {
        if (StartStopButton is null)
        {
            return false;
        }

        var items = BuildReadinessItems();
        foreach (var item in items)
        {
            if (item.Target is null)
            {
                continue;
            }

            item.Target.Text = $"{(item.IsReady ? "✓" : "•")} {(item.IsReady ? item.ReadyText : item.MissingText)}";
            item.Target.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.IsReady ? "#047857" : "#B45309"));
            item.Target.FontWeight = item.IsReady ? FontWeights.Normal : FontWeights.SemiBold;
        }

        var isReady = items.All(item => item.IsReady);
        StartStopButton.IsEnabled = session.IsRunning || isReady;
        StartStopButton.ToolTip = isReady
            ? null
            : string.Join(Environment.NewLine, items.Where(item => !item.IsReady).Select(item => item.MissingText));
        return isReady;
    }

    private bool? ShowRegionDialog(RegionSelectionWindow picker)
    {
        picker.Owner = null;
        Hide();
        try
        {
            return picker.ShowDialog();
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private IReadOnlyList<ReadinessItem> BuildReadinessItems()
    {
        var selectedWindow = WindowComboBox?.SelectedItem as CapturableWindow;
        var mode = settings.TranslationMode;
        var hasRegion = SelectedRegion is not null;
        var ocrLanguage = OcrLanguageComboBox?.SelectedItem as OcrLanguage;
        var targetLanguage = TargetLanguageComboBox?.SelectedItem as TranslationLanguage;
        var translatorReady = IsTranslatorReady(out var translatorMissingText);
        var ocrReady = IsOcrReady(ocrLanguage, out var ocrReadyText, out var ocrMissingText);
        var dictionaryReady = UserDictionaryStore.DefaultDictionary.Count > 0;

        return
        [
            new ReadinessItem(
                ReadyWindowText,
                selectedWindow is not null,
                $"게임 창 선택됨: {selectedWindow}",
                "게임 창을 선택하세요."),
            new ReadinessItem(
                ReadyRegionText,
                hasRegion,
                $"{(mode == TranslationMode.Screen ? "전체화면 번역" : "채팅 번역")} 영역 선택됨",
                $"{(mode == TranslationMode.Screen ? "전체화면 번역" : "채팅 번역")} 영역을 선택하세요."),
            new ReadinessItem(
                ReadyOcrText,
                ocrReady && ocrLanguage is not null,
                ocrReadyText,
                ocrMissingText),
            new ReadinessItem(
                ReadyTranslatorText,
                translatorReady && targetLanguage is not null,
                $"번역 서비스 준비됨: {GetSelectedTranslatorName()} → {targetLanguage?.DisplayName ?? "한국어"}",
                targetLanguage is null ? "번역 언어를 선택하세요." : translatorMissingText),
            new ReadinessItem(
                ReadyDictionaryText,
                dictionaryReady,
                $"기본 사전 준비됨: {UserDictionaryStore.DefaultDictionary.Count}개 항목",
                "기본 사전을 불러오지 못했습니다.")
        ];
    }

    private bool IsOcrReady(OcrLanguage? ocrLanguage, out string readyText, out string missingText)
    {
        if (ocrLanguage is null)
        {
            readyText = string.Empty;
            missingText = "OCR 언어를 선택하세요.";
            return false;
        }

        if (settings.OcrEngineType == OcrEngineType.PaddleOCR)
        {
            readyText = $"OCR 준비됨: PaddleOCR / {ocrLanguage.DisplayName}";
            missingText = string.Empty;
            return true;
        }

        var available = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(ocrLanguage.Tag)) is not null;
        readyText = $"OCR 준비됨: Windows OCR / {ocrLanguage.DisplayName}";
        missingText = $"{ocrLanguage.DisplayName} Windows OCR 언어 팩을 설치하세요.";
        return available;
    }

    private bool IsTranslatorReady(out string missingText)
    {
        missingText = settings.TranslatorType switch
        {
            TranslationServiceType.DeepL when string.IsNullOrWhiteSpace(ApiKeyPasswordBox?.Password) => "DeepL API 키를 입력하고 저장하세요.",
            TranslationServiceType.GoogleWebApp when !IsValidHttpUrl(GoogleWebAppUrlTextBox?.Text) => "Google Apps Script Web App URL을 입력하고 저장하세요.",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(missingText);
    }

    private string GetSelectedTranslatorName() =>
        TranslatorTypeComboBox?.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? settings.TranslatorType.ToString()
            : settings.TranslatorType.ToString();

    private static bool IsValidHttpUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private async Task<string> TranslateAndCopyChatAsync(string sourceText)
    {
        sourceText = sourceText.Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new InvalidOperationException("전송할 채팅을 입력하세요.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var translated = await chatTranslationService.TranslateAsync(
            new TranslationRequest(sourceText, "zh-CN", "ko"),
            cts.Token);

        var chatText = translated.TranslatedText.Trim();
        if (string.IsNullOrWhiteSpace(chatText))
        {
            throw new InvalidOperationException("번역 결과가 비어 있어 전송하지 않았습니다.");
        }

        Clipboard.SetText(chatText, TextDataFormat.UnicodeText);
        return chatText;
    }

    private async void ToggleSession(object sender, RoutedEventArgs e)
    {
        if (session.IsRunning)
        {
            await StopSessionAsync();
            return;
        }
        if (!UpdateStartReadiness())
        {
            SetStatus("시작 전 준비 항목을 완료하세요.", true);
            return;
        }
        if (WindowComboBox.SelectedItem is not CapturableWindow window)
        {
            SetStatus("게임 창을 먼저 선택하세요.", true);
            return;
        }

        var mode = settings.TranslationMode;
        CaptureRegion region;
        if (mode == TranslationMode.Screen)
        {
            region = SelectedRegion ?? FullWindowRegion;
        }
        else
        {
            if (selectedChatRegions.Count == 0)
            {
                SetStatus("번역 영역을 먼저 선택하세요.", true);
                return;
            }
            region = GetBoundingRegion(selectedChatRegions);
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
        activeSessionRegion = region;
        activeSessionMode = mode;
        ShowTranslationOutput(window, region, mode);

        var filterSettings = settings.ToFilterSettings();

        var userDict = userDictStore.Load();

        await session.StartAsync(new SessionOptions(
            new CaptureTarget(window), 
            region, 
            ocrLanguage, 
            targetLanguage, 
            TimeSpan.FromSeconds(1), 
            filterSettings, 
            userDict,
            mode,
            excludedRegions,
            mode == TranslationMode.Chat ? selectedChatRegions : null,
            mode == TranslationMode.Screen && settings.DisplayMode == TranslationDisplayMode.TransparentOverlay),
            sessionCancellation.Token);

        if (SelectedRegion is not null)
        {
            SaveSelections(window);
        }
        else
        {
            settings = settings with { LastWindowTitle = window.Title, LastWindowProcessName = window.ProcessName };
            settingsStore.Save(settings);
        }
        StartStopButton.Content = "번역 정지";
    }

    private async Task StopSessionAsync()
    {
        sessionCancellation?.Cancel();
        await session.StopAsync();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        activeSessionRegion = null;
        activeSessionMode = null;
        StartStopButton.Content = "번역 시작";
        overlayWindow?.ClearAll();
        UpdateStartReadiness();
    }

    private void SessionUpdated(object? sender, SessionUpdate update) => Dispatcher.Invoke(() =>
    {
        SetStatus(update.Status, update.IsError);
        resultWindow?.Apply(update);
        if (overlayWindow is not null && WindowComboBox.SelectedItem is CapturableWindow window)
        {
            var region = activeSessionRegion ?? SelectedRegion ?? FullWindowRegion;
            var mode = activeSessionMode ?? settings.TranslationMode;
            overlayWindow.PositionOver(window, region);
            overlayWindow.CurrentMode = mode;
            overlayWindow.Topmost = false;
            overlayWindow.Topmost = true;
        }
        overlayWindow?.Apply(update);

        // Handle diagnostic log recording
        var diagnosticEntry = DiagnosticLogFormatter.Create(update);
        if (diagnosticEntry is not null)
        {
            var logItem = new DiagnosticLogItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Status = diagnosticEntry.Status,
                Source = diagnosticEntry.Source,
                Rule = diagnosticEntry.Rule,
                Reason = diagnosticEntry.Reason,
                ApiUsage = diagnosticEntry.ApiUsage
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

    private async void OnClosed(object? sender, EventArgs e)
    {
        await StopSessionAsync();
        if (resultWindow is not null)
        {
            resultWindow.Closed -= ResultWindowClosed;
            resultWindow.Close();
        }
        overlayWindow?.Close();
        if (delegatingOcrEngine is IDisposable disposableEngine)
        {
            disposableEngine.Dispose();
        }
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

        resultWindow = new ResultWindow(TranslateAndCopyChatAsync);
        resultWindow.ApplyMode(settings.DisplayMode);
        resultWindow.Closed += ResultWindowClosed;
        return resultWindow;
    }

    private void ShowTranslationOutput(CapturableWindow window, CaptureRegion region, TranslationMode mode)
    {
        if (settings.DisplayMode == TranslationDisplayMode.TransparentOverlay)
        {
            resultWindow?.Close();
            resultWindow = null;
            overlayWindow ??= new OverlayWindow();
            overlayWindow.ClearAll();
            overlayWindow.CurrentMode = mode;
            overlayWindow.PositionOver(window, region);
            overlayWindow.FontFamily = new FontFamily(settings.FontFamily);
            overlayWindow.FontSize = settings.FontSize;
            overlayWindow.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.TextColor));
            overlayWindow.StrokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.OutlineColor));
            overlayWindow.StrokeThicknessValue = settings.StrokeThickness;
            overlayWindow.OverlayBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.OverlayBackgroundColor));
            overlayWindow.Opacity = settings.OverlayOpacity;
            overlayWindow.DisplayDuration = TimeSpan.FromSeconds(settings.OverlayDurationSeconds);
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

    private async void ResultWindowClosed(object? sender, EventArgs e)
    {
        resultWindow = null;
        if (session.IsRunning && settings.DisplayMode == TranslationDisplayMode.Window)
        {
            await StopSessionAsync();
            SetStatus("결과 창이 닫혀 번역을 중단했습니다.");
        }
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

    private void RestoreRegions(
        IReadOnlyList<CaptureRegion>? chatRegions,
        CaptureRegion? legacyChatRegion,
        CaptureRegion? screenRegion)
    {
        selectedChatRegions = chatRegions?
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToList() ?? [];
        if (selectedChatRegions.Count == 0 && legacyChatRegion is { Width: > 0, Height: > 0 } restoredChat)
        {
            selectedChatRegions.Add(restoredChat);
        }
        if (screenRegion is { Width: > 0, Height: > 0 } restoredScreen)
        {
            selectedScreenRegion = restoredScreen;
        }
    }

    private void RestoreExcludedRegions(IReadOnlyList<CaptureRegion>? regions, CaptureRegion? legacyRegion)
    {
        excludedRegions = regions?
            .Where(region => region.Width > 0 && region.Height > 0)
            .ToList() ?? [];
        if (excludedRegions.Count == 0 && legacyRegion is { Width: > 0, Height: > 0 } restored)
        {
            excludedRegions.Add(restored);
        }
    }

    private void ShowRegion(CaptureRegion region)
    {
        RegionText.Text = $"{region.X:P0}, {region.Y:P0} / {region.Width:P0} x {region.Height:P0}";
    }

    private void ShowRegionSummary()
    {
        RegionText.Text = $"번역 {selectedChatRegions.Count}개 · 제외 {excludedRegions.Count}개";
    }

    private void SaveSelections(CapturableWindow window)
    {
        var firstChatRegion = selectedChatRegions.Count > 0 ? selectedChatRegions[0] : (CaptureRegion?)null;
        settings = settings with
        {
            LastWindowTitle = window.Title,
            LastWindowProcessName = window.ProcessName,
            LastRegion = firstChatRegion,
            LastChatRegion = firstChatRegion,
            LastChatRegions = selectedChatRegions.Count > 0 ? selectedChatRegions.ToArray() : null,
            LastScreenRegion = selectedScreenRegion,
            LastExcludedRegion = excludedRegions.Count > 0 ? excludedRegions[0] : null,
            LastExcludedRegions = excludedRegions.Count > 0 ? excludedRegions.ToArray() : null,
            LastScreenExcludedRegion = null
        };
        settingsStore.Save(settings);
    }

    private static CaptureRegion GetBoundingRegion(IReadOnlyList<CaptureRegion> regions)
    {
        var left = regions.Min(region => region.X);
        var top = regions.Min(region => region.Y);
        var right = regions.Max(region => region.X + region.Width);
        var bottom = regions.Max(region => region.Y + region.Height);
        return new CaptureRegion(left, top, right - left, bottom - top);
    }

    private CapturableWindow? FindKartWindow(IReadOnlyList<CapturableWindow> windows)
    {
        return windows.FirstOrDefault(window =>
            (window.Title != null && window.Title.Contains("kart", StringComparison.OrdinalIgnoreCase)) ||
            (window.ProcessName != null && window.ProcessName.Contains("kart", StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateRegionButtonVisual()
    {
        if (SelectedRegion == null && ChatTranslationRadioButton.IsChecked == true)
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
        if (FontPreviewBgBorder != null && settings != null)
        {
            var (bgRgb, bgOpacity) = SplitArgbHex(settings.OverlayBackgroundColor);
            var color = (Color)ColorConverter.ConvertFromString(bgRgb);
            color.A = (byte)Math.Clamp(Math.Round(bgOpacity * 255), 0, 255);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            FontPreviewBgBorder.Background = brush;
        }
    }

    private void FontFamilySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null || restoringSettings) return;
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
        if (settings == null || restoringSettings || FontSizeLabel == null) return;
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
        if (settings == null || restoringSettings) return;
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
        if (settings == null || restoringSettings) return;
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
        if (settings == null || restoringSettings || StrokeThicknessLabel == null) return;
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

    private void BackgroundColorSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null || restoringSettings || BackgroundColorsListBox.SelectedItem is not ColorChoice color) return;

        var (_, currentOpacity) = SplitArgbHex(settings.OverlayBackgroundColor);
        var mergedHex = GetMergedHexColor(color.Hex, currentOpacity);

        settings = settings with { OverlayBackgroundColor = mergedHex, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
        settingsStore.Save(settings);
        UpdateFontPreview();
        if (overlayWindow is not null)
        {
            overlayWindow.OverlayBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mergedHex));
        }
    }

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settings == null || restoringSettings || BackgroundOpacityLabel == null) return;

        var opacity = Math.Round(e.NewValue, 2);
        BackgroundOpacityLabel.Text = $"{opacity:P0}";

        var (currentRgb, _) = SplitArgbHex(settings.OverlayBackgroundColor);
        var mergedHex = GetMergedHexColor(currentRgb, opacity);

        settings = settings with { OverlayBackgroundColor = mergedHex, OverlayPreset = applyingOverlayPreset ? settings.OverlayPreset : "사용자 지정" };
        settingsStore.Save(settings);
        UpdateFontPreview();
        if (overlayWindow is not null)
        {
            overlayWindow.OverlayBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mergedHex));
        }
    }

    private (string rgb, double opacity) SplitArgbHex(string argbHex)
    {
        if (string.IsNullOrWhiteSpace(argbHex))
        {
            return ("#000000", 0.6);
        }

        if (argbHex.StartsWith("#"))
        {
            argbHex = argbHex.Substring(1);
        }

        if (argbHex.Length == 8)
        {
            var alphaHex = argbHex.Substring(0, 2);
            var rgbHex = argbHex.Substring(2);

            if (byte.TryParse(alphaHex, System.Globalization.NumberStyles.HexNumber, null, out byte alpha))
            {
                return ($"#{rgbHex}", alpha / 255.0);
            }
            return ($"#{rgbHex}", 1.0);
        }
        else if (argbHex.Length == 6)
        {
            return ($"#{argbHex}", 1.0);
        }

        return ("#000000", 0.6);
    }

    private string GetMergedHexColor(string rgbHex, double opacity)
    {
        if (rgbHex.StartsWith("#"))
        {
            rgbHex = rgbHex.Substring(1);
        }

        if (rgbHex.Length == 8)
        {
            rgbHex = rgbHex.Substring(2);
        }
        else if (rgbHex.Length == 3)
        {
            rgbHex = $"{rgbHex[0]}{rgbHex[0]}{rgbHex[1]}{rgbHex[1]}{rgbHex[2]}{rgbHex[2]}";
        }

        byte alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        return $"#{alpha:X2}{rgbHex}";
    }

    private void OverlayPresetSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (settings == null || restoringSettings || applyingOverlayPreset || OverlayPresetComboBox?.SelectedItem is not OverlayPreset preset)
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

            var (presetBgRgb, presetBgOpacity) = SplitArgbHex(preset.BackgroundColor);
            BackgroundColorsListBox.SelectedItem = BackgroundColors.FirstOrDefault(color => string.Equals(color.Hex, presetBgRgb, StringComparison.OrdinalIgnoreCase));
            BackgroundOpacitySlider.Value = presetBgOpacity;
            BackgroundOpacityLabel.Text = $"{presetBgOpacity:P0}";
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
        if (settings == null || restoringSettings || OverlayOpacityLabel == null)
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

    private void OverlayDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settings == null || restoringSettings || OverlayDurationLabel == null)
        {
            return;
        }

        var seconds = Math.Round(e.NewValue);
        OverlayDurationLabel.Text = $"{seconds:0}초";
        settings = settings with { OverlayDurationSeconds = seconds };
        settingsStore.Save(settings);
        if (overlayWindow is not null)
        {
            overlayWindow.DisplayDuration = TimeSpan.FromSeconds(seconds);
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
        if (ShowRegionDialog(picker) != true || picker.Region is not { } region)
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
        var filter = settings.ToFilterSettings();

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
