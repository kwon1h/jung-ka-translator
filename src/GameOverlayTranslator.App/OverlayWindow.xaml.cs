using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public sealed record OverlayChatItem(
    string Id,
    string DisplayText,
    double Left,
    double AnchorTop,
    double Top,
    double MinWidth,
    double MaxWidth,
    double RenderedWidth,
    double Height)
{
    public double RenderedFontSize { get; init; }
}

public partial class OverlayWindow : Window
{
    private readonly ObservableCollection<OverlayChatItem> lines = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeTimers = new();
    private CancellationTokenSource? screenTimer;
    private bool chatSnapshotMode;

    public TranslationMode CurrentMode { get; set; } = TranslationMode.Chat;
    public TimeSpan DisplayDuration { get; set; } = TimeSpan.FromSeconds(AppSettingsDefaults.DefaultOverlayDurationSeconds);

    private Canvas activeCanvas = null!;
    private Canvas inactiveCanvas = null!;

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty StrokeThicknessValueProperty =
        DependencyProperty.Register(nameof(StrokeThicknessValue), typeof(double), typeof(OverlayWindow), new PropertyMetadata(3.0));

    public static readonly DependencyProperty OverlayBackgroundBrushProperty =
        DependencyProperty.Register(nameof(OverlayBackgroundBrush), typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0))));

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThicknessValue
    {
        get => (double)GetValue(StrokeThicknessValueProperty);
        set => SetValue(StrokeThicknessValueProperty, value);
    }

    public Brush OverlayBackgroundBrush
    {
        get => (Brush)GetValue(OverlayBackgroundBrushProperty);
        set => SetValue(OverlayBackgroundBrushProperty, value);
    }

    public OverlayWindow()
    {
        InitializeComponent();
        OverlayItems.ItemsSource = lines;
        activeCanvas = ScreenOverlayCanvas1;
        inactiveCanvas = ScreenOverlayCanvas2;
    }

    public bool ExcludeFromCapture { get; set; } = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int extendedStyle = Platform.NativeMethods.GetWindowLong(hwnd, Platform.NativeMethods.GWL_EXSTYLE);
        Platform.NativeMethods.SetWindowLong(hwnd, Platform.NativeMethods.GWL_EXSTYLE, extendedStyle | Platform.NativeMethods.WS_EX_TRANSPARENT | Platform.NativeMethods.WS_EX_NOACTIVATE);
        UpdateDisplayAffinity();
    }

    public void UpdateDisplayAffinity()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        uint affinity = ExcludeFromCapture ? Platform.NativeMethods.WDA_EXCLUDEFROMCAPTURE : 0;
        if (!Platform.NativeMethods.SetWindowDisplayAffinity(hwnd, affinity))
        {
            AppLog.Write($"Failed to set window display affinity to {affinity}.");
        }
    }

    public void PositionOver(CapturableWindow window, CaptureRegion region)
    {
        if (!WindowGeometry.TryGetClientScreenRect(window.Handle, out var rect))
        {
            return;
        }

        var dpiScale = Math.Max(1, NativeMethods.GetDpiForWindow(window.Handle) / 96d);
        var pixels = region.ToPixels(rect.Width, rect.Height);
        Left = (rect.Left + pixels.X) / dpiScale;
        Top = (rect.Top + pixels.Y) / dpiScale;
        Width = pixels.Width / dpiScale;
        Height = pixels.Height / dpiScale;
    }

    public void Apply(SessionUpdate update)
    {
        if (CurrentMode == TranslationMode.Screen)
        {
            if (update.ScreenItems is null)
            {
                return;
            }

            if (update.ScreenItems.Count == 0)
            {
                if (update.FilterRule == "EnglishOnly")
                {
                    ClearScreenItems();
                }
                return;
            }

            try
            {
                OverlayItems.Visibility = Visibility.Collapsed;
                ChatBackgroundPath.Data = Geometry.Empty;

                // Clear the inactive canvas first (it is currently hidden, so no flicker)
                inactiveCanvas.Children.Clear();

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var dpiScale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd) / 96d);

                var borders = new List<Border>();
                var desiredBoxes = new List<Rect>();
                foreach (var cluster in BuildScreenClusters(update.ScreenItems, dpiScale))
                {
                    var stack = new StackPanel();
                    foreach (var renderItem in cluster.Items)
                    {
                        stack.Children.Add(new OutlinedTextBlock
                        {
                            Text = renderItem.Text,
                            FontFamily = this.FontFamily,
                            FontSize = this.FontSize,
                            Fill = this.Foreground,
                            Stroke = this.StrokeBrush,
                            StrokeThickness = this.StrokeThicknessValue,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Left
                        });
                    }

                    var x = ClampToCanvas(cluster.Bounds.Left, ActualWidth);
                    var y = ClampToCanvas(cluster.Bounds.Top, ActualHeight);
                    var availableWidth = Math.Max(1, ActualWidth - x);

                    var border = new Border
                    {
                        Background = Brushes.Transparent,
                        Padding = new Thickness(4, 2, 4, 2),
                        CornerRadius = new CornerRadius(4),
                        ClipToBounds = true,
                        Child = stack,
                        MinWidth = Math.Min(cluster.Bounds.Width, availableWidth),
                        MaxWidth = availableWidth
                    };

                    border.Measure(new Size(availableWidth, double.PositiveInfinity));
                    var width = Math.Min(availableWidth, Math.Max(border.MinWidth, border.DesiredSize.Width));
                    var height = Math.Max(cluster.Bounds.Height, border.DesiredSize.Height);
                    border.Width = width;
                    border.Height = height;

                    borders.Add(border);
                    desiredBoxes.Add(new Rect(x, y, width, height));
                }

                var placedBoxes = desiredBoxes;
                inactiveCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = OverlayBackgroundGeometry.Create(placedBoxes, 4),
                    Fill = OverlayBackgroundBrush,
                    IsHitTestVisible = false
                });
                for (var index = 0; index < borders.Count; index++)
                {
                    Canvas.SetLeft(borders[index], placedBoxes[index].Left);
                    Canvas.SetTop(borders[index], placedBoxes[index].Top);
                    inactiveCanvas.Children.Add(borders[index]);
                }

                // Swap visibility in one frame
                inactiveCanvas.Visibility = Visibility.Visible;
                activeCanvas.Visibility = Visibility.Collapsed;

                // Swap active/inactive canvas references
                var temp = activeCanvas;
                activeCanvas = inactiveCanvas;
                inactiveCanvas = temp;
                ResetScreenTimer();
            }
            catch (Exception ex)
            {
                AppLog.Write("Screen overlay rendering failed", ex);
            }
            return;
        }

        OverlayItems.Visibility = Visibility.Visible;
        ScreenOverlayCanvas1.Visibility = Visibility.Collapsed;
        ScreenOverlayCanvas2.Visibility = Visibility.Collapsed;

        if (update.ChatItems is { } chatItems)
        {
            if (!chatSnapshotMode)
            {
                ClearChatItems();
                chatSnapshotMode = true;
            }

            foreach (var chatItem in chatItems)
            {
                if (chatItem.BoundingRect is { } itemBounds
                    && !string.IsNullOrWhiteSpace(chatItem.TranslatedText))
                {
                    ApplyChatItem(chatItem.Id, chatItem.Speaker, chatItem.TranslatedText, itemBounds);
                }
            }
            return;
        }

        if (chatSnapshotMode)
        {
            return;
        }

        if (!update.IsChatLine || string.IsNullOrWhiteSpace(update.TranslatedText))
        {
            return;
        }

        if (update.BoundingRect is not { } boundingRect)
        {
            return;
        }

        ApplyChatItem(
            update.ChatLineId ?? Guid.NewGuid().ToString("N"),
            update.Speaker,
            update.TranslatedText,
            boundingRect);
    }

    private void ApplyChatItem(string id, string? speaker, string translatedText, Rect boundingRect)
    {
        var chatHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var chatDpiScale = Math.Max(1, NativeMethods.GetDpiForWindow(chatHwnd) / 96d);
        var displayText = $"{speaker}: {translatedText}";
        var left = ClampToCanvas(boundingRect.Left / chatDpiScale, ActualWidth);
        var anchorTop = ClampToCanvas(boundingRect.Top / chatDpiScale, ActualHeight);
        var maxWidth = Math.Max(1, ActualWidth - left - 4);
        var minWidth = Math.Min(Math.Max(1, boundingRect.Width / chatDpiScale), maxWidth);
        var boxSize = MeasureChatBox(displayText, minWidth, maxWidth, Math.Max(1, boundingRect.Height / chatDpiScale));
        var item = new OverlayChatItem(
            id,
            displayText,
            left,
            anchorTop,
            anchorTop,
            minWidth,
            maxWidth,
            boxSize.Width,
            boxSize.Height)
        {
            RenderedFontSize = boxSize.FontSize
        };
        var existing = lines.Select((line, index) => new { line, index }).FirstOrDefault(line => line.line.Id == id);
        
        if (existing is not null)
        {
            lines[existing.index] = item;
        }
        else
        {
            lines.Add(item);
        }

        // Cancel existing timer for this item if it was updated
        if (activeTimers.TryRemove(id, out var prevCts))
        {
            prevCts.Cancel();
            prevCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        activeTimers[id] = cts;
        _ = RemoveAfterDelayAsync(id, cts);
        LayoutChatLines();
    }

    private async Task RemoveAfterDelayAsync(string id, CancellationTokenSource timer)
    {
        try
        {
            await Task.Delay(DisplayDuration, timer.Token);
            await Dispatcher.InvokeAsync(() =>
            {
                if (timer.IsCancellationRequested
                    || !activeTimers.TryGetValue(id, out var currentTimer)
                    || !ReferenceEquals(currentTimer, timer))
                {
                    return;
                }

                var existing = lines.FirstOrDefault(line => line.Id == id);
                if (existing is not null)
                {
                    lines.Remove(existing);
                    LayoutChatLines();
                }

                if (activeTimers.TryRemove(id, out var completedTimer))
                {
                    completedTimer.Dispose();
                }
            });
        }
        catch (TaskCanceledException)
        {
            // Expected cancellation
        }
    }

    private IReadOnlyList<ScreenCluster> BuildScreenClusters(
        IReadOnlyList<ScreenTranslationItem> items,
        double dpiScale)
    {
        var clusters = new List<ScreenCluster>();
        foreach (var item in items
                     .Select(item => new ScreenRenderItem(
                         item.TranslatedText,
                         new Rect(
                             item.BoundingRect.X / dpiScale,
                             item.BoundingRect.Y / dpiScale,
                             item.BoundingRect.Width / dpiScale,
                             item.BoundingRect.Height / dpiScale)))
                     .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0)
                     .OrderBy(item => item.Bounds.Top)
                     .ThenBy(item => item.Bounds.Left))
        {
            var cluster = clusters.LastOrDefault(candidate => AreNearby(candidate.Bounds, item.Bounds));
            if (cluster is null)
            {
                clusters.Add(new ScreenCluster(item));
            }
            else
            {
                cluster.Add(item);
            }
        }
        return clusters;
    }

    private bool AreNearby(Rect first, Rect second)
    {
        var horizontalGap = Math.Max(0, Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right));
        var verticalGap = Math.Max(0, Math.Max(first.Top, second.Top) - Math.Min(first.Bottom, second.Bottom));
        return horizontalGap <= 20 && verticalGap <= Math.Max(8, FontSize * 0.8);
    }

    private ChatBoxMetrics MeasureChatBox(string text, double minWidth, double maxWidth, double sourceHeight)
    {
        const double horizontalPadding = 12;
        var strokeOffset = StrokeThicknessValue * 2;
        var textMaxWidth = Math.Max(1, maxWidth - horizontalPadding - strokeOffset);
        var textMaxHeight = Math.Max(1, sourceHeight - strokeOffset);
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var measured = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var scale = Math.Min(
            1,
            Math.Min(
                textMaxWidth / Math.Max(1, measured.Width),
                textMaxHeight / Math.Max(1, measured.Height)));
        var fittedFontSize = Math.Clamp(FontSize * scale, Math.Min(9, FontSize), FontSize);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fittedFontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = textMaxWidth,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1
        };
        var renderedWidth = Math.Clamp(
            formatted.Width + strokeOffset + horizontalPadding,
            minWidth,
            maxWidth);
        return new ChatBoxMetrics(renderedWidth, sourceHeight, fittedFontSize);
    }

    private void LayoutChatLines()
    {
        if (lines.Count == 0)
        {
            ChatBackgroundPath.Data = Geometry.Empty;
            return;
        }

        var placed = OverlayLayout.PlaceChatAtOcrRows(lines);
        for (var index = 0; index < lines.Count; index++)
        {
            if (Math.Abs(lines[index].Top - placed[index].Top) > 0.1)
            {
                lines[index] = lines[index] with { Top = placed[index].Top };
            }
        }

        ChatBackgroundPath.Data = OverlayBackgroundGeometry.Create(
            lines.Select(line => new Rect(line.Left, line.Top, line.RenderedWidth, line.Height)),
            3);
    }

    private void ResetScreenTimer()
    {
        screenTimer?.Cancel();
        screenTimer?.Dispose();
        screenTimer = new CancellationTokenSource();
        _ = ClearScreenAfterDelayAsync(screenTimer.Token);
    }

    private void ClearScreenItems()
    {
        screenTimer?.Cancel();
        screenTimer?.Dispose();
        screenTimer = null;
        ScreenOverlayCanvas1.Children.Clear();
        ScreenOverlayCanvas2.Children.Clear();
        ScreenOverlayCanvas1.Visibility = Visibility.Collapsed;
        ScreenOverlayCanvas2.Visibility = Visibility.Collapsed;
    }

    private async Task ClearScreenAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DisplayDuration, token);
            await Dispatcher.InvokeAsync(() =>
            {
                ClearScreenItems();
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static double ClampToCanvas(double value, double canvasLength)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        if (!double.IsFinite(canvasLength) || canvasLength <= 1)
        {
            return Math.Max(0, value);
        }

        return Math.Clamp(value, 0, canvasLength - 1);
    }

    public void ClearAll()
    {
        Dispatcher.Invoke(() =>
        {
            ClearChatItems();
            chatSnapshotMode = false;
            ClearScreenItems();
        });
    }

    private void ClearChatItems()
    {
        lines.Clear();
        ChatBackgroundPath.Data = Geometry.Empty;
        foreach (var cts in activeTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        activeTimers.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearAll();
        base.OnClosed(e);
    }

    public void SetCaptureVisibility(bool visible)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    private sealed record ScreenRenderItem(string Text, Rect Bounds);

    private sealed record ChatBoxMetrics(double Width, double Height, double FontSize);

    private sealed class ScreenCluster(ScreenRenderItem first)
    {
        public List<ScreenRenderItem> Items { get; } = [first];
        public Rect Bounds { get; private set; } = first.Bounds;

        public void Add(ScreenRenderItem item)
        {
            Items.Add(item);
            var bounds = Bounds;
            bounds.Union(item.Bounds);
            Bounds = bounds;
        }
    }
}

internal static class OverlayLayout
{
    public static IReadOnlyList<Rect> PlaceChatAtOcrRows(IEnumerable<OverlayChatItem> items) =>
        items.Select(line => new Rect(line.Left, line.AnchorTop, line.RenderedWidth, line.Height)).ToArray();
}

internal static class OverlayBackgroundGeometry
{
    public static Geometry Create(IEnumerable<Rect> rectangles, double cornerRadius)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var rectangle in rectangles.Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0))
        {
            group.Children.Add(new RectangleGeometry(rectangle, cornerRadius, cornerRadius));
        }

        group.Freeze();
        return group;
    }
}
