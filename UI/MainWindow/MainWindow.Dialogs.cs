using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private ContentDialog? _activeDialog;

    private async Task<ContentDialogResult> ShowNativeDialogAsync(ContentDialog dialog)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (_realClose || RootPanel.XamlRoot is null)
                return ContentDialogResult.None;
            dialog.XamlRoot = RootPanel.XamlRoot;
            dialog.RequestedTheme = RootPanel.ActualTheme;
            _activeDialog = dialog;
            return await dialog.ShowAsync();
        }
        finally
        {
            _activeDialog = null;
            _dialogGate.Release();
        }
    }

    private async Task<string?> ShowPromptDialog(string title, string defaultText = "", string hint = "")
    {
        var input = new TextBox
        {
            Text = defaultText,
            PlaceholderText = TranslationCatalog.Get("Dialog_PresetNamePlaceholder"),
            Header = string.IsNullOrEmpty(hint) ? TranslationCatalog.Get("Dialog_InputPresetName") : hint,
            MinWidth = 280
        };
        input.Loaded += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = TranslationCatalog.Get("Dialog_Save"),
            CloseButtonText = TranslationCatalog.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        var result = await ShowNativeDialogAsync(dialog);
        var text = input.Text.Trim();
        return result == ContentDialogResult.Primary && text.Length > 0 ? text : null;
    }

    private async Task<bool> ShowConfirmDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = TranslationCatalog.Get("Dialog_ConfirmDelete"),
            CloseButtonText = TranslationCatalog.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await ShowNativeDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowCheckResultDialog(string message, string? title = null)
    {
        await ShowNativeDialogAsync(new ContentDialog
        {
            Title = title ?? string.Empty,
            Content = message,
            CloseButtonText = TranslationCatalog.Get("Dialog_OK"),
            DefaultButton = ContentDialogButton.Close
        });
    }

    private async Task<bool> ShowFindWarningDialog()
    {
        var dialog = new ContentDialog
        {
            Title = "安全警告",
            Content = "警告：使用“查找耳机”功能时，请勿将耳机戴在耳朵中。\n耳机响铃音量较大，戴在耳内可能造成永久性听力损伤。",
            PrimaryButtonText = "我已知晓，继续",
            CloseButtonText = TranslationCatalog.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await ShowNativeDialogAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowFeedbackDialogAsync()
    {
        var result = await ShowNativeDialogAsync(new ContentDialog
        {
            Title = TranslationCatalog.Get("Dialog_FeedbackTitle"),
            Content = TranslationCatalog.Get("Dialog_FeedbackMessage"),
            PrimaryButtonText = "GitHub",
            SecondaryButtonText = "GitLab",
            CloseButtonText = TranslationCatalog.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Close
        });
        if (result == ContentDialogResult.Primary)
            ExportFeedback("https://github.com/Zhaoyi-ya/OppoPodsManager/issues/new");
        else if (result == ContentDialogResult.Secondary)
            ExportFeedback("https://jihulab.com/zhaoyi-ya-group/oppo-pods-manager/-/work_items/new");
    }
}
