using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public sealed record OverlayChatItem(string Id, string DisplayText);

public partial class OverlayWindow : Window
{
    private const int MaxOverlayLines = 6;
    private readonly ObservableCollection<OverlayChatItem> lines = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeTimers = new();

    public TranslationMode CurrentMode { get; set; } = TranslationMode.Chat;

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
                return;
            }

            try
            {
                OverlayItems.Visibility = Visibility.Collapsed;

                // Clear the inactive canvas first (it is currently hidden, so no flicker)
                inactiveCanvas.Children.Clear();

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var dpiScale = Math.Max(1, NativeMethods.GetDpiForWindow(hwnd) / 96d);

                foreach (var screenItem in update.ScreenItems)
                {
                    var wpfX = ClampToCanvas(screenItem.BoundingRect.Left / dpiScale, ActualWidth);
                    var wpfY = ClampToCanvas(screenItem.BoundingRect.Top / dpiScale, ActualHeight);
                    var wpfWidth = screenItem.BoundingRect.Width / dpiScale;
                    var wpfHeight = screenItem.BoundingRect.Height / dpiScale;

                    if (!double.IsFinite(wpfX) || !double.IsFinite(wpfY) || 
                        !double.IsFinite(wpfWidth) || !double.IsFinite(wpfHeight) ||
                        wpfWidth <= 0 || wpfHeight <= 0)
                    {
                        continue;
                    }

                    var textBlock = new OutlinedTextBlock
                    {
                        Text = screenItem.TranslatedText,
                        FontFamily = this.FontFamily,
                        FontSize = this.FontSize,
                        Fill = this.Foreground,
                        Stroke = this.StrokeBrush,
                        StrokeThickness = this.StrokeThicknessValue,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };

                    var border = new Border
                    {
                        Background = this.OverlayBackgroundBrush,
                        Padding = new Thickness(4, 2, 4, 2),
                        CornerRadius = new CornerRadius(4),
                        Child = textBlock,
                        MinWidth = wpfWidth,
                        Height = wpfHeight,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    Canvas.SetLeft(border, wpfX);
                    Canvas.SetTop(border, wpfY);
                    inactiveCanvas.Children.Add(border);
                }

                // Swap visibility in one frame
                inactiveCanvas.Visibility = Visibility.Visible;
                activeCanvas.Visibility = Visibility.Collapsed;

                // Swap active/inactive canvas references
                var temp = activeCanvas;
                activeCanvas = inactiveCanvas;
                inactiveCanvas = temp;
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

        if (!update.IsChatLine || string.IsNullOrWhiteSpace(update.TranslatedText))
        {
            return;
        }

        var id = update.ChatLineId ?? Guid.NewGuid().ToString("N");
        var item = new OverlayChatItem(id, $"{update.Speaker}: {update.TranslatedText}");
        var existing = lines.Select((line, index) => new { line, index }).FirstOrDefault(line => line.line.Id == id);
        
        if (existing is not null)
        {
            lines[existing.index] = item;
        }
        else
        {
            RemoveContainedPreviousLines(item);
            lines.Add(item);
            while (lines.Count > MaxOverlayLines)
            {
                var oldItem = lines[0];
                lines.RemoveAt(0);
                if (activeTimers.TryRemove(oldItem.Id, out var oldCts))
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
            }
        }

        // Cancel existing timer for this item if it was updated
        if (activeTimers.TryRemove(id, out var prevCts))
        {
            prevCts.Cancel();
            prevCts.Dispose();
        }

        // Create new timer to remove this item after 5 seconds
        var cts = new CancellationTokenSource();
        activeTimers[id] = cts;
        _ = RemoveAfterDelayAsync(id, cts.Token);
    }

    private async Task RemoveAfterDelayAsync(string id, CancellationToken token)
    {
        try
        {
            await Task.Delay(5000, token);
            if (!token.IsCancellationRequested)
            {
                Dispatcher.Invoke(() =>
                {
                    var existing = lines.FirstOrDefault(l => l.Id == id);
                    if (existing != null)
                    {
                        lines.Remove(existing);
                    }
                });
                activeTimers.TryRemove(id, out _);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected cancellation
        }
    }

    private void RemoveContainedPreviousLines(OverlayChatItem next)
    {
        var nextText = NormalizeContainment(next.DisplayText);
        if (nextText.Length < 2)
        {
            return;
        }

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var previous = lines[index];
            var previousText = NormalizeContainment(previous.DisplayText);
            if (previousText.Length >= 2
                && nextText.Length > previousText.Length
                && nextText.Contains(previousText, StringComparison.Ordinal))
            {
                if (activeTimers.TryRemove(previous.Id, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                lines.RemoveAt(index);
            }
        }
    }

    private static string NormalizeContainment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
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
            lines.Clear();
            ScreenOverlayCanvas1?.Children.Clear();
            ScreenOverlayCanvas2?.Children.Clear();
            if (ScreenOverlayCanvas1 is not null) ScreenOverlayCanvas1.Visibility = Visibility.Collapsed;
            if (ScreenOverlayCanvas2 is not null) ScreenOverlayCanvas2.Visibility = Visibility.Collapsed;
            foreach (var cts in activeTimers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            activeTimers.Clear();
        });
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
}
