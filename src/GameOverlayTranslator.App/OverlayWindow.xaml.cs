using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App;

public sealed record OverlayChatItem(string Id, string DisplayText);

public partial class OverlayWindow : Window
{
    private const int MaxOverlayLines = 6;
    private readonly ObservableCollection<OverlayChatItem> lines = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> activeTimers = new();

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OverlayWindow), new PropertyMetadata(Brushes.Black));

    public static readonly DependencyProperty StrokeThicknessValueProperty =
        DependencyProperty.Register(nameof(StrokeThicknessValue), typeof(double), typeof(OverlayWindow), new PropertyMetadata(3.0));

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

    public OverlayWindow()
    {
        InitializeComponent();
        OverlayItems.ItemsSource = lines;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int extendedStyle = Platform.NativeMethods.GetWindowLong(hwnd, Platform.NativeMethods.GWL_EXSTYLE);
        Platform.NativeMethods.SetWindowLong(hwnd, Platform.NativeMethods.GWL_EXSTYLE, extendedStyle | Platform.NativeMethods.WS_EX_TRANSPARENT | Platform.NativeMethods.WS_EX_NOACTIVATE);
        Platform.NativeMethods.SetWindowDisplayAffinity(hwnd, Platform.NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    public void PositionOver(CapturableWindow window, CaptureRegion region)
    {
        if (!NativeMethods.GetWindowRect(window.Handle, out var rect))
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

    public void ClearAll()
    {
        Dispatcher.Invoke(() =>
        {
            lines.Clear();
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
