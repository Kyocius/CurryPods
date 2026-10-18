using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.VisualAssets;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void BtnBgLeft_Click(object sender, RoutedEventArgs e)
        => PersonalView.BgThumbScroller.ChangeView(Math.Max(0, PersonalView.BgThumbScroller.HorizontalOffset - 200), null, null);

    private void BtnBgRight_Click(object sender, RoutedEventArgs e)
        => PersonalView.BgThumbScroller.ChangeView(
            Math.Min(PersonalView.BgThumbScroller.ExtentWidth - PersonalView.BgThumbScroller.ViewportWidth,
                     PersonalView.BgThumbScroller.HorizontalOffset + 200), null, null);

    private async Task BtnBgAdd_Click()
    {
        if (PersonalView.CbAcrylicBlur.IsChecked == true)
            return;

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
        if (file != null)
        {
            if (_backgroundSelection.Add(file.Path))
                RefreshBgThumbs();
        }
    }

    private void SelectBackground(string key)
    {
        if (PersonalView.CbAcrylicBlur.IsChecked == true && key != "default")
            return;

        _logManager?.Debug("UI", key == "default" ? "背景: 选择默认背景" : "背景: 选择自定义背景");
        _backgroundSelection.Select(key);
        ApplySavedBackground();
    }

    private void ApplySavedBackground()
    {
        var key = _backgroundSelection.SelectedKey;
        if (key == "default" || !_backgroundImages.IsAvailable(key))
        {
            SetBackgroundImageSource(null, "");
            _backgroundImages.ClearBackgroundCache(keepKey: null);
            BgFullImage.Visibility = Visibility.Collapsed;
            return;
        }

        var blur = Math.Clamp(ReadUiInt("BgBlur", 0), 0, 20);
        var cacheKey = _backgroundImages.BuildCacheKey(key, GetBackgroundTargetWidth(), blur);
        try
        {
            SetBackgroundImageSource(
                _backgroundImages.GetOrCreateBitmap(key, GetBackgroundTargetWidth(), blur, cacheKey),
                cacheKey);
            _backgroundImages.ClearBackgroundCache(keepKey: cacheKey);
            BgFullImage.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            _logManager?.Error("UI", $"背景图加载失败，回退默认背景：{key}", exception);
            SetBackgroundImageSource(null, "");
            BgFullImage.Visibility = Visibility.Collapsed;
        }
    }

    private void SetBackgroundImageSource(ImageSource? source, string cacheKey)
    {
        var old = _backgroundImageSource;
        if (ReferenceEquals(old, source))
            return;

        BgFullImage.Source = source;
        _backgroundImageSource = source;
    }

    private int GetBackgroundTargetWidth()
    {
        var windowWidth = _appWindow?.Size.Width ?? 900;
        return Math.Clamp((int)Math.Ceiling(windowWidth * 1.1), 900, 1440);
    }

    private void RefreshBgThumbs()
    {
        // 清除旧历史缩略图（保留默认缩略图）
        for (int i = PersonalView.BgThumbList.Children.Count - 1; i >= 0; i--)
        {
            var c = PersonalView.BgThumbList.Children[i];
            if (c is Border b && ReferenceEquals(b, PersonalView.BgThumbDefault))
                continue;
            PersonalView.BgThumbList.Children.RemoveAt(i);
        }

        foreach (var path in _backgroundSelection.History)
        {
            var img = new Border
            {
                Width = 90,
                Height = 60,
                CornerRadius = new CornerRadius(8),
                Tag = path,
            };
            try
            {
                if (_backgroundImages.IsAvailable(path))
                    img.Background = new ImageBrush
                    {
                        ImageSource = _backgroundImages.GetOrCreateThumbnail(path),
                        Stretch = Stretch.UniformToFill
                    };
                else
                    img.Background = new SolidColorBrush(Colors.DimGray);
            }
            catch
            {
                img.Background = new SolidColorBrush(Colors.DimGray);
            }
            img.PointerPressed += (_, _) => SelectBackground(path);

            var delBtn = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 200, 40, 40)),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                Child = new TextBlock
                {
                    Text = "✕",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            delBtn.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (_backgroundSelection.Remove(path))
                    RefreshBgThumbs();
            };

            var wrapper = new Grid { Width = 90, Height = 60 };
            wrapper.Children.Add(img);
            wrapper.Children.Add(delBtn);
            wrapper.PointerEntered += (_, _) => delBtn.Visibility = Visibility.Visible;
            wrapper.PointerExited += (_, _) => delBtn.Visibility = Visibility.Collapsed;

            PersonalView.BgThumbList.Children.Add(wrapper);
        }
        SelectBackground(_backgroundSelection.SelectedKey);
    }

    private DispatcherTimer EnsureBackgroundApplyDebounceTimer()
    {
        if (_bgApplyDebounceTimer != null)
            return _bgApplyDebounceTimer;

        _bgApplyDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _bgApplyDebounceTimer.Tick += (_, _) =>
        {
            _bgApplyDebounceTimer?.Stop();
            ApplySavedBackground();
        };
        return _bgApplyDebounceTimer;
    }
}
