using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public sealed record ChatResultItem(string Id, string Prefix, string Translation, string Source);

public partial class ResultWindow : Window
{
    private const int MaxChatLines = 80;
    private readonly ObservableCollection<ChatResultItem> chatLines = [];
    private readonly Func<string, Task<string>> translateAndCopyChatAsync;
    private string chatTargetLanguageName = "게임 언어";

    public ResultWindow(Func<string, Task<string>> translateAndCopyChatAsync)
    {
        this.translateAndCopyChatAsync = translateAndCopyChatAsync;
        InitializeComponent();
        ChatItems.ItemsSource = chatLines;
    }

    public void SetChatTargetLanguage(string displayName)
    {
        chatTargetLanguageName = string.IsNullOrWhiteSpace(displayName) ? "게임 언어" : displayName;
        ChatSendStatusText.Text = CreateChatSendHint(chatTargetLanguageName);
    }

    internal static string CreateChatSendHint(string targetLanguageName) =>
        $"Enter로 {targetLanguageName} 번역 후 클립보드에 복사합니다. 줄바꿈은 Shift+Enter입니다.";

    internal static string CreateChatSendProgress(string targetLanguageName) =>
        $"채팅을 {targetLanguageName}(으)로 번역해서 클립보드에 복사하는 중...";

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

    private async void SendTranslatedChat(object sender, RoutedEventArgs e)
    {
        await SendTranslatedChatAsync();
    }

    private async void ChatInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Enter || e.Key == Key.Return) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendTranslatedChatAsync();
        }
    }

    private async Task SendTranslatedChatAsync()
    {
        var sourceText = ChatInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            SetChatSendStatus("번역할 채팅을 입력하세요.", true);
            return;
        }

        SetChatSendInProgress(true);
        SetChatSendStatus(CreateChatSendProgress(chatTargetLanguageName));
        try
        {
            var copiedText = await translateAndCopyChatAsync(sourceText);
            ChatInputTextBox.Clear();
            SetChatSendStatus($"복사 완료: {copiedText}");
        }
        catch (Exception ex)
        {
            AppLog.Write("Failed to translate and copy chat from result window", ex);
            SetChatSendStatus($"채팅 번역/복사 실패: {ex.Message}", true);
        }
        finally
        {
            SetChatSendInProgress(false);
        }
    }

    private void SetChatSendInProgress(bool isInProgress)
    {
        ChatInputTextBox.IsEnabled = !isInProgress;
        SendChatButton.IsEnabled = !isInProgress;
    }

    private void SetChatSendStatus(string status, bool isError = false)
    {
        ChatSendStatusText.Text = status;
        ChatSendStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#FCA5A5" : "#B7C6C2"));
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
