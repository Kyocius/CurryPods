using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void AdaptToPlatform()
    {
        SystemBackdrop = new MicaBackdrop();
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
        Activated -= OnWindowActivated;
        _activeDialog?.Hide();
        AppNavigation.SelectionChanged -= Navigation_SelectionChanged;
        AppNavigation.ItemInvoked -= Navigation_ItemInvoked;
        AppNavigation.BackRequested -= Navigation_BackRequested;
        AppNavigation.DisplayModeChanged -= Navigation_DisplayModeChanged;

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
        LogView?.Stop();
        DisposeWindowImages();
        PageContent.Content = null;
        _pageContainers.Clear();
        _pageScrollOffsets.Clear();
        SystemBackdrop = null;
        Content = null;
    }

    internal void CloseForExit()
    {
        DisposeRuntimeUiResources();
        Close();
    }
}
