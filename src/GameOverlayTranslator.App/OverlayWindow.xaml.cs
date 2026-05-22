using System.Collections.ObjectModel;
using System.Windows;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App;

public sealed record OverlayChatItem(string Id, string DisplayText);

public partial class OverlayWindow : Window
{
    private const int MaxOverlayLines = 6;
    private readonly ObservableCollection<OverlayChatItem> lines = [];

    public OverlayWindow()
    {
        InitializeComponent();
        OverlayItems.ItemsSource = lines;
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
            return;
        }

        lines.Add(item);
        while (lines.Count > MaxOverlayLines)
        {
            lines.RemoveAt(0);
        }
    }

    public void SetCaptureVisibility(bool visible)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }
}
