using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.VisualAssets;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Models;
using BatteryLevel = OppoPodsManager.Control.Core.Models.BatteryLevel;
using EarphoneSlot = OppoPodsManager.Assets.VisualAssets.EarphoneSlot;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void UpdateTitle()
    {
        var name = GetNextWindowTitle();
        Title = name;
        WindowTitleBar.Subtitle = name == AppConst.WindowTitle ? string.Empty : name;
        var snapshot = _frontendState?.Snapshot;
        var parts = new List<string>();
        AddNextBatteryPart(parts, "L", snapshot?.LeftBattery);
        AddNextBatteryPart(parts, "R", snapshot?.RightBattery);
        AddNextBatteryPart(parts, "C", snapshot?.CaseBattery);
    }

    private string GetNextWindowTitle()
    {
        var snapshot = _frontendState?.Snapshot;
        if (snapshot?.IsConnected != true)
            return AppConst.WindowTitle;

        var custom = (_customDeviceName ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(custom)
            ? custom
            : DeviceText.DeviceName(snapshot.Identity?.DisplayName, snapshot.DeviceName, snapshot.Identity?.ModelName);
    }

    private static void AddNextBatteryPart(List<string> parts, string channel, BatteryLevel? battery)
    {
        if (battery is { } value)
            parts.Add($"{channel}:{value.Percent}%{(value.IsCharging ? "⚡" : "")}");
    }

    private void ExportFeedback(string url)
    {
        if (_feedbackExporter is null)
            return;

        var result = _feedbackExporter.ExportToDesktop(
            AppInfo.VersionLabel,
            _frontendState?.Snapshot);
        if (!result.Succeeded)
            return;

        _desktopLinks?.TryOpen(url, "反馈链接");
        AppDispatcher.Post(async () =>
            await ShowCheckResultDialog(
                string.Format(LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_FeedbackExported), result.FileName),
                LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_FeedbackTitle)));
    }

    private void RefreshEarphoneImages()
    {
        if (HomeView is null)
            return;
        ReplaceEarphoneImage(HomeView.BatteryLeftImage, EarphoneSlot.HomeLeft);
        ReplaceEarphoneImage(HomeView.BatteryRightImage, EarphoneSlot.HomeRight);
        ReplaceEarphoneImage(HomeView.BatteryCaseImage, EarphoneSlot.Case);
        HomeView.RefreshBatteryImages();
    }

    private static void ReplaceEarphoneImage(Image image, EarphoneSlot slot)
    {
        image.Source = EarphoneImageProvider.GetBitmap(slot);
    }

    private void BuildEarphoneCustomUi()
    {
        foreach (var preview in _earphonePreviews.Values)
            preview.Source = null;

        PersonalView.EarphoneCustomContent.Children.Clear();
        _earphonePreviews.Clear();

        foreach (var slot in new[]
                 { EarphoneSlot.Case, EarphoneSlot.HomeLeft, EarphoneSlot.HomeRight, EarphoneSlot.Headphone })
        {
            var preview = new Image
            {
                Width = 52,
                Height = 52,
                Stretch = Stretch.Uniform,
                Source = EarphoneImageProvider.GetBitmap(slot)
            };
            _earphonePreviews[slot] = preview;

            var border = new Border
            {
                Width = 56,
                Height = 56,
                CornerRadius = new CornerRadius(8),
                Background = _textPanelButtonBgBrush,
                Child = preview
            };

            var menu = new MenuFlyout();
            var pickItem = new MenuFlyoutItem
            {
                Text = LanguageManager.Instance.GetString(LanguageManager.Instance.Personal_EarphoneSelect)
            };
            pickItem.Click += async (_, _) => await PickAndSaveEarphoneImage(slot);

            var resetItem = new MenuFlyoutItem
            {
                Text = LanguageManager.Instance.GetString(LanguageManager.Instance.Personal_EarphoneReset)
            };
            resetItem.Click += (_, _) =>
            {
                EarphoneImageProvider.ResetCustom(slot);
                RefreshEarphoneImages();
                BuildEarphoneCustomUi();
            };

            menu.Items.Add(pickItem);
            menu.Items.Add(resetItem);
            border.ContextFlyout = menu;
            border.Tapped += (s, e) => menu.ShowAt(border);

            var panel = new StackPanel
            {
                Width = 108,
                Spacing = 6,
                Margin = new Thickness(8, 4, 8, 4),
            };
            panel.Children.Add(border);
            panel.Children.Add(new TextBlock
            {
                Text = LanguageManager.Instance.GetString(slot switch
                {
                    EarphoneSlot.HomeLeft => LanguageManager.Instance.Personal_EarphoneLeft,
                    EarphoneSlot.HomeRight => LanguageManager.Instance.Personal_EarphoneRight,
                    EarphoneSlot.Headphone => LanguageManager.Instance.Personal_EarphoneHeadphone,
                    _ => LanguageManager.Instance.Personal_EarphoneCase
                }),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = BrushGray
            });

            PersonalView.EarphoneCustomContent.Children.Add(panel);
        }
    }

    private async Task PickAndSaveEarphoneImage(EarphoneSlot slot)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.ViewMode = PickerViewMode.Thumbnail;
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".webp");

        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;

        try
        {
            EarphoneImageProvider.SaveCustom(slot, file.Path);
            RefreshEarphoneImages();
            BuildEarphoneCustomUi();
        }
        catch (Exception ex)
        {
            _logManager?.Error("UI", $"保存自定义耳机图案失败：{slot}", ex);
        }
    }

    private void DisposeWindowImages()
    {
        if (HomeView is not null)
        {
            HomeView.BatteryLeftImage.Source = null;
            HomeView.BatteryRightImage.Source = null;
            HomeView.BatteryCaseImage.Source = null;
        }
        foreach (var preview in _earphonePreviews.Values)
            preview.Source = null;
        _earphonePreviews.Clear();
        PersonalView.EarphoneCustomContent.Children.Clear();
    }
}
