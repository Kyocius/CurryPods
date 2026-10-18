using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Windows.Graphics;
using OppoPodsManager.Assets.Localization;
using NextSettingsManager = OppoPodsManager.Assets.UserSettings.SettingsManager;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Toast;

public enum ToastType { Battery, LowBattery, CriticalBattery, Disconnected }

public partial class ToastWindow : Window
{
    private AppWindow? _appWindow;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;

    public ToastWindow()
    {
        InitializeComponent();
        SetupAppWindow();
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
            ToastManager.Unregister(this);
            SystemBackdrop = null;
            Content = null;
        };
    }

    private void SetupAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.Resize(new SizeInt32(360, 160));
    }

    public static Task ShowSnapshotAsync(BusinessSnapshot? snapshot, string deviceName,
        ToastType type = ToastType.Battery, int durationMs = 5000, NextSettingsManager? settings = null,
        CancellationToken cancellationToken = default)
    {
        return AppDispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ShowSnapshotCoreAsync(snapshot, deviceName, type, durationMs, settings, cancellationToken);
        });
    }

    private static async Task ShowSnapshotCoreAsync(BusinessSnapshot? snapshot, string deviceName,
        ToastType type, int durationMs, NextSettingsManager? settings, CancellationToken cancellationToken)
    {
        ApplicationLog.Current?.Debug("Toast", $"显示 Next 快照 Toast：type={type}，device={deviceName}。");
        var toast = new ToastWindow();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, toast._lifetime.Token);
        try
        {
            if (type == ToastType.Disconnected)
            {
                toast.BatteryPanel.Visibility = Visibility.Collapsed;
                toast.DisconnectPanel.Visibility = Visibility.Visible;
                toast.DisconnectTitle.Text = deviceName;
            }
            else
            {
                toast.TitleBlock.Text = deviceName;
                SetNextBattery(toast.LeftPct, toast.LeftBolt, snapshot?.LeftBattery);
                SetNextBattery(toast.RightPct, toast.RightBolt, snapshot?.RightBattery);
                SetNextBattery(toast.CasePct, toast.CaseBolt, snapshot?.CaseBattery);
                toast.LowBatteryOverlay.Visibility = type == ToastType.LowBattery ? Visibility.Visible : Visibility.Collapsed;
                toast.CriticalBatteryOverlay.Visibility = type == ToastType.CriticalBattery ? Visibility.Visible : Visibility.Collapsed;
            }

            toast.Reposition();
            ToastManager.Register(toast);
            toast.Activate();

            if (type is ToastType.LowBattery or ToastType.CriticalBattery)
            {
                var overlay = type == ToastType.LowBattery ? toast.LowBatteryOverlay : toast.CriticalBatteryOverlay;
                var holdMs = Math.Min(2000, Math.Max(800, durationMs - 1500));
                await Task.Delay(holdMs, lifetime.Token);
                overlay.Opacity = 0;
                await Task.Delay(Math.Max(500, durationMs - holdMs), lifetime.Token);
            }
            else
            {
                await Task.Delay(durationMs, lifetime.Token);
            }

        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (!toast._closed)
                toast.Close();
            toast._lifetime.Dispose();
        }
    }

    private static void SetNextBattery(TextBlock percentage, FrameworkElement bolt, BatteryLevel? battery)
    {
        percentage.Text = battery is { } value ? $"{value.Percent}%" : "- %";
        bolt.Visibility = battery?.IsCharging == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reposition()
    {
        if (_appWindow is null)
            return;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var wa = displayArea.WorkArea;
            var x = wa.X + wa.Width - 380;
            var y = wa.Y + wa.Height - 180;
            _appWindow.Move(new PointInt32(x, y));
        }
    }
}
