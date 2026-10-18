using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OppoPodsManager.Assets.Localization;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private async Task<string?> ShowPromptDialog(string title, string defaultText = "", string hint = "")
    {
        _promptTcs = new TaskCompletionSource<string?>();
        _confirmTcs = null;

        DialogTitle.Text = title;
        DialogMessage.Text = string.IsNullOrEmpty(hint) ? LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_InputPresetName) : hint;
        DialogInput.Visibility = Visibility.Visible;
        DialogInput.Text = defaultText;
        DialogCancelBtn.Content = LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_Cancel);
        DialogCancelBtn.Background = AppPalette.BrushTransparent;
        DialogCancelBtn.Visibility = Visibility.Visible;
        DialogConfirmBtn.Content = LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_Save);
        DialogConfirmBtn.Background = AppPalette.BrushTransparent;
        DialogConfirmBtn.Visibility = Visibility.Visible;
        DialogOverlay.Visibility = Visibility.Visible;
        _logManager?.Debug("UI", $"对话框: 打开输入框 -> {title}");
        DialogInput.Focus(FocusState.Programmatic);
        DialogInput.SelectAll();

        return await _promptTcs.Task;
    }

    private async Task<bool> ShowConfirmDialog(string title, string message)
    {
        _confirmTcs = new TaskCompletionSource<bool>();
        _promptTcs = null;

        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogInput.Visibility = Visibility.Collapsed;
        DialogCancelBtn.Content = LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_Cancel);
        DialogCancelBtn.Background = AppPalette.BrushTransparent;
        DialogCancelBtn.Visibility = Visibility.Visible;
        DialogConfirmBtn.Content = LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_ConfirmDelete);
        DialogConfirmBtn.Background = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xE8, 0x11, 0x23));
        DialogConfirmBtn.Visibility = Visibility.Visible;
        DialogOverlay.Visibility = Visibility.Visible;
        _logManager?.Debug("UI", $"对话框: 打开确认框 -> {title}");

        return await _confirmTcs.Task;
    }

    private void DialogSkip_Click(object sender, RoutedEventArgs e)
    {
        if (DialogSkipBtn.Content is string label && label == "GitLab")
        {
            DialogOverlay_Close();
            _confirmTcs?.TrySetResult(false);
            ExportFeedback("https://jihulab.com/zhaoyi-ya-group/oppo-pods-manager/-/work_items/new");
            return;
        }
        DialogOverlay_Close();
        _confirmTcs?.TrySetResult(false);
    }

    private void DialogOverlay_Close()
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        DialogSkipBtn.Visibility = Visibility.Collapsed;
    }

    private void DialogCancel_Click(object sender, RoutedEventArgs e)
    {
        _logManager?.Debug("UI", "对话框: 取消");
        DialogOverlay_Close();
        _promptTcs?.TrySetResult(null);
        _confirmTcs?.TrySetResult(false);
    }

    private void DialogConfirm_Click(object sender, RoutedEventArgs e)
    {
        _logManager?.Debug("UI", "对话框: 确认");
        DialogOverlay_Close();

        if (_promptTcs != null)
        {
            var text = DialogInput.Text?.Trim();
            _promptTcs.TrySetResult(string.IsNullOrEmpty(text) ? null : text);
        }
        else if (_confirmTcs != null)
        {
            _confirmTcs.TrySetResult(true);
        }
    }

    private async Task ShowCheckResultDialog(string msg, string? title = null)
    {
        _confirmTcs = new TaskCompletionSource<bool>();
        _promptTcs = null;

        DialogTitle.Text = title ?? string.Empty;
        DialogMessage.Text = msg;
        DialogInput.Visibility = Visibility.Collapsed;
        DialogSkipBtn.Visibility = Visibility.Collapsed;
        DialogCancelBtn.Visibility = Visibility.Collapsed;
        DialogConfirmBtn.Content = LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_OK);
        DialogConfirmBtn.Background = AppPalette.BrushTransparent;
        DialogConfirmBtn.Visibility = Visibility.Visible;
        DialogOverlay.Visibility = Visibility.Visible;

        await _confirmTcs.Task;
    }

    private async Task<bool> ShowFindWarningDialog()
    {
        _confirmTcs = new TaskCompletionSource<bool>();
        _promptTcs = null;

        DialogTitle.Text = "安全警告";
        DialogMessage.Text = "警告：使用“查找耳机”功能时，请勿将耳机戴在耳朵中。\n耳机响铃音量较大，戴在耳内可能造成永久性听力损伤。";
        DialogInput.Visibility = Visibility.Collapsed;
        DialogSkipBtn.Visibility = Visibility.Collapsed;
        DialogCancelBtn.Content = "取消";
        DialogCancelBtn.Background = AppPalette.BrushTransparent;
        DialogCancelBtn.Visibility = Visibility.Visible;
        DialogConfirmBtn.Content = "我已知晓，继续";
        DialogConfirmBtn.Background = AppPalette.BrushTransparent;
        DialogConfirmBtn.Visibility = Visibility.Visible;
        DialogOverlay.Visibility = Visibility.Visible;

        return await _confirmTcs.Task;
    }
}
