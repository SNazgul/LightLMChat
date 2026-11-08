using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LightLMChat.Abstractions;

namespace LightLMChat.Wpf;

public partial class LightLmChatControl : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(IChatSession), typeof(LightLmChatControl));

    public static readonly DependencyProperty NoticeTextProperty = DependencyProperty.Register(
        nameof(NoticeText), typeof(string), typeof(LightLmChatControl),
        new PropertyMetadata(string.Empty, OnNoticeTextChanged));

    private CancellationTokenSource? _requestCancellation;

    public LightLmChatControl()
    {
        InitializeComponent();
    }

    public IChatSession? Session
    {
        get => (IChatSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public string NoticeText
    {
        get => (string)GetValue(NoticeTextProperty);
        set => SetValue(NoticeTextProperty, value);
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendCurrentAsync();

    private async void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_requestCancellation is not null)
            {
                e.Handled = true;
                _requestCancellation.Cancel();
            }
            return;
        }
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        await SendCurrentAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _requestCancellation?.Cancel();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_requestCancellation is not null) return;
        Messages.Clear();
        Session?.Reset();
        StatusLabel.Text = "Ready";
        Composer.Focus();
    }

    private void CopySelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextBox box } } &&
            box.SelectedText.Length > 0)
            CopyToClipboard(box.SelectedText);
    }

    private void CopyMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ChatMessage message })
            CopyToClipboard(message.Text);
    }

    private void SelectAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: TextBox box } })
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void MessageTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBox box || box.ContextMenu is not ContextMenu menu)
            return;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "copy-selection")
                item.Visibility = box.SelectedText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { /* The clipboard is busy; ignore. */ }
    }

    private static void OnNoticeTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LightLmChatControl)d;
        var text = (string)e.NewValue;
        control.NoticeLabel.Text = text;
        control.NoticeBanner.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task SendCurrentAsync()
    {
        if (_requestCancellation is not null) return;
        var text = Composer.Text.Trim();
        if (text.Length == 0) return;
        if (Session is null)
        {
            StatusLabel.Text = "No chat session is configured.";
            return;
        }

        Composer.Clear();
        Messages.Add(new ChatMessage("You", text));
        var answer = new ChatMessage("Assistant", "");
        Messages.Add(answer);
        TranscriptScroller.ScrollToEnd();

        using var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        SetBusy(true);

        try
        {
            await foreach (var update in Session.SendAsync(text, cancellation.Token).WithCancellation(cancellation.Token))
            {
                switch (update)
                {
                    case TextDeltaEvent delta:
                        answer.Text += delta.Text;
                        break;
                    case StatusEvent status:
                        StatusLabel.Text = status.Text;
                        break;
                    case ToolActivityEvent tool:
                        StatusLabel.Text = $"{tool.ToolName}: {tool.Text}";
                        break;
                }
                TranscriptScroller.ScrollToEnd();
            }
            StatusLabel.Text = "Ready";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusLabel.Text = "Canceled";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Request failed";
            Messages.Add(new ChatMessage("Error", ex.Message));
        }
        finally
        {
            if (answer.Text.Length == 0) Messages.Remove(answer);
            _requestCancellation = null;
            SetBusy(false);
            Composer.Focus();
        }
    }

    private void SetBusy(bool busy)
    {
        SendButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Composer.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        if (busy) StatusLabel.Text = "Working…";
    }
}
