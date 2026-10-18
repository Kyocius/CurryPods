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
        SidebarFullBg.Visibility = Visibility.Visible;
    }

    private void DisableAdvancedRender()
    {
        SidebarFullBg.Visibility = Visibility.Collapsed;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_realClose)
            return;

        if (_shouldKeepWindowAlive?.Invoke() == true)
        {
            e.Cancel = true;
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
        _promptTcs?.TrySetResult(null);
        _confirmTcs?.TrySetResult(false);
        _promptTcs = null;
        _confirmTcs = null;

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
        SystemBackdrop = null;
        Content = null;
    }

    internal void CloseForExit()
    {
        DisposeRuntimeUiResources();
        Close();
    }
}
