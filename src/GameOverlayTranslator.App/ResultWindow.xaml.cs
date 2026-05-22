using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public sealed record ChatResultItem(string Id, string Prefix, string Translation, string Source);

public partial class ResultWindow : Window
{
    private const int MaxChatLines = 80;
    private readonly ObservableCollection<ChatResultItem> chatLines = [];

    public ResultWindow()
    {
        InitializeComponent();
        ChatItems.ItemsSource = chatLines;
    }

    public void Apply(SessionUpdate update)
    {
        ResultStatusText.Text = update.Status;
        if (!update.IsChatLine || string.IsNullOrWhiteSpace(update.TranslatedText))
        {
            return;
        }

        var item = new ChatResultItem(update.ChatLineId ?? Guid.NewGuid().ToString("N"), $"{update.Speaker}: ", update.TranslatedText, update.SourceText ?? string.Empty);
        var existing = update.ReplacesChatLine
            ? chatLines.Select((line, index) => new { line, index }).FirstOrDefault(line => line.line.Id == item.Id)
            : null;
        if (existing is not null)
        {
            chatLines[existing.index] = item;
            return;
        }

        RemoveContainedPreviousLines(item);
        chatLines.Add(item);
        while (chatLines.Count > MaxChatLines)
        {
            chatLines.RemoveAt(0);
        }

        ChatScrollViewer.ScrollToEnd();
    }

    public void ApplyMode(TranslationDisplayMode mode)
    {
        var overlay = mode == TranslationDisplayMode.TransparentOverlay;
        Topmost = overlay;
        ShowInTaskbar = !overlay;
        OpacityPanel.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        Opacity = overlay ? OpacitySlider.Value : 1;
        Title = overlay ? "채팅 번역 오버레이" : "채팅 번역";
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityPanel.Visibility == Visibility.Visible)
        {
            Opacity = e.NewValue;
        }
    }

    private void RemoveContainedPreviousLines(ChatResultItem next)
    {
        var nextText = NormalizeContainment(next.Translation);
        if (nextText.Length < 2)
        {
            return;
        }

        for (var index = chatLines.Count - 1; index >= 0; index--)
        {
            var previous = chatLines[index];
            var previousText = NormalizeContainment(previous.Translation);
            if (previousText.Length >= 2
                && nextText.Length > previousText.Length
                && nextText.Contains(previousText, StringComparison.Ordinal))
            {
                chatLines.RemoveAt(index);
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
}
