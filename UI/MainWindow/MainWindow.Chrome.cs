using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void AdaptToPlatform()
    {
        ApplySystemBackdrop(ReadUiBool("AcrylicBlur", false));

        if (ReadUiBool("AdvancedRender", false))
            EnableAdvancedRender();
    }

    private void ApplySystemBackdrop(bool acrylic)
    {
        if (acrylic)
        {
            if (SystemBackdrop is DesktopAcrylicBackdrop)
                return;
            try
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            catch
            {
                SystemBackdrop = new MicaBackdrop();
            }
        }
        else if (SystemBackdrop is not MicaBackdrop)
        {
            SystemBackdrop = new MicaBackdrop();
        }
    }

    private void ToggleAcrylicBlur(bool on)
    {
        ApplyAcrylicBlurSilently(on);
        ApplySystemBackdrop(on);
    }

    internal void ApplyAcrylicBlurSilently(bool on)
    {
        WriteUiBool("AcrylicBlur", on);
        _logManager?.Debug("UI", $"设置: Acrylic 模糊(静默) -> {on}");
        if (on)
            SelectBackground("default");
        UpdateBackgroundSettingsAvailability(on);
    }

    private void UpdateBackgroundSettingsAvailability(bool acrylicBlurEnabled)
    {
        var enabled = !acrylicBlurEnabled;
        PersonalView.BackgroundSettingsContent.IsHitTestVisible = enabled;
        PersonalView.BackgroundSettingsContent.Opacity = enabled ? 1 : 0.45;
    }

    private void EnableAdvancedRender()
    {
        WindowTitleBar.Visibility = Visibility.Collapsed;
        ExtendsContentIntoTitleBar = false;
    }

    private void DisableAdvancedRender()
    {
        WindowTitleBar.Visibility = Visibility.Visible;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_realClose)
            return;

        if (_shouldKeepWindowAlive?.Invoke() == true)
        {
            e.Cancel = true;
            _activeDialog?.Hide();
            sender.Hide();
            LogView?.Stop();
            return;
        }

        if (_requestApplicationExit is not null)
        {
            e.Cancel = true;
            _realClose = true;
            DisposeRuntimeUiResources();
            _requestApplicationExit();
            return;
        }

        _realClose = true;
        DisposeRuntimeUiResources();
    }

    private void DisposeRuntimeUiResources()
    {
        if (_runtimeUiDisposed)
            return;
        _runtimeUiDisposed = true;
        _realClose = true;
        HomeView?.MarkClosed();
        _activeDialog?.Hide();
        AppNavigation.SelectionChanged -= Navigation_SelectionChanged;
        AppNavigation.ItemInvoked -= Navigation_ItemInvoked;
        AppNavigation.BackRequested -= Navigation_BackRequested;
        AppNavigation.PaneOpened -= Navigation_PaneVisibilityChanged;
        AppNavigation.PaneClosed -= Navigation_PaneVisibilityChanged;
        AppNavigation.DisplayModeChanged -= Navigation_PaneVisibilityChanged;

        if (_appWindow is not null)
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _appWindow.Changed -= OnAppWindowChanged;
        }

        if (_frontendState is not null)
            _frontendState.Changed -= OnNextStateChanged;
        if (_controlManager is not null)
            _controlManager.AvailableDevicesChanged -= OnAvailableDevicesChanged;

        _interactiveSurface?.Dispose();
        _interactiveSurface = null;
        foreach (var subscription in _linguaSubs)
            subscription.Dispose();
        _linguaSubs.Clear();
        EqView?.StopDebounceTimer();
        _bgApplyDebounceTimer?.Stop();
        LogView?.Stop();
        DisposeWindowImages();
        SetBackgroundImageSource(null, "");
        _backgroundImages.Dispose();
        PageContent.Content = null;
        _pageContainers.Clear();
        SystemBackdrop = null;
        Content = null;
    }

    internal void CloseForExit()
    {
        DisposeRuntimeUiResources();
        Close();
    }
}
