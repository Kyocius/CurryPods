using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Oppo.Features;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;
using StatusWindow = OppoPodsManager.UI.MiniWindow.Status.SmallWindow;

namespace OppoPodsManager.UI.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly FrontendState _frontendState;
    private readonly ControlManager _controlManager;
    private readonly CommandDispatcher _commandDispatcher;
    private readonly SettingsManager _settings;
    private readonly Func<Window?> _mainWindow;
    private readonly Action _exitApplication;
    private readonly H.NotifyIcon.TaskbarIcon _icon;
    private readonly ApplicationLog _log;
    private StatusWindow? _miniWindow;
    private string _menuSignature = "";
    private bool _disposed;

    public TrayIconController(
        FrontendState frontendState,
        ControlManager controlManager,
        CommandDispatcher commandDispatcher,
        SettingsManager settings,
        ApplicationLog log,
        Func<Window?> mainWindow,
        Action exitApplication)
    {
        _frontendState = frontendState;
        _controlManager = controlManager;
        _commandDispatcher = commandDispatcher;
        _settings = settings;
        _log = log;
        _mainWindow = mainWindow;
        _exitApplication = exitApplication;

        _icon = new H.NotifyIcon.TaskbarIcon
        {
            Id = new Guid("8E2D7745-6B98-4B28-9214-4B22B2F4450D")
        };
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var iconPath = Path.Combine(baseDir, "Assets", "VisualAssets", "tuopan.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    var sysIcon = new System.Drawing.Icon(iconPath);
                    _icon.Icon = sysIcon;
                }
                catch
                {
                    _icon.IconSource = new BitmapImage(new Uri(iconPath));
                }
            }
            else
            {
                _icon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/VisualAssets/tuopan.ico"));
            }
        }
        catch (Exception ex)
        {
            log.Error("Tray", "加载托盘图标失败", ex);
        }

        _icon.LeftClickCommand = new TrayCommand(ToggleMiniWindow);
        _icon.DoubleClickCommand = new TrayCommand(ShowMainWindow);
        _icon.ToolTipText = BuildTooltip(_frontendState.Snapshot);

        RefreshMenu(_frontendState.Snapshot, force: true);
        _frontendState.Changed += OnFrontendStateChanged;
        _settings.Changed += OnSettingsChanged;

        TryCreateIcon();
    }

    private void TryCreateIcon()
    {
        try
        {
            if (!_icon.IsCreated)
            {
                _icon.ForceCreate(false);
                _log.Info("Tray", "托盘图标初始化成功。");
            }
        }
        catch (Exception ex)
        {
            var win32Err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _log.Info("Tray", $"初始化托盘图标暂未成功 (Win32Error=0x{win32Err:X}): {ex.Message}");
            AppDispatcher.Post(async () =>
            {
                await Task.Delay(1200);
                if (_disposed || _icon.IsCreated)
                    return;
                try
                {
                    _icon.ForceCreate(false);
                    _log.Info("Tray", "托盘图标延迟初始化成功。");
                }
                catch (Exception retryEx)
                {
                    var retryErr = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    _log.Error("Tray", $"托盘图标延迟初始化仍失败 (Win32Error=0x{retryErr:X})", retryEx);
                }
            });
        }
    }

    private void OnFrontendStateChanged(object? sender, BusinessSnapshot snapshot)
    {
        AppDispatcher.Post(() =>
        {
            if (_disposed)
                return;
            _icon.ToolTipText = BuildTooltip(snapshot);
            RefreshMenu(snapshot);
        });
    }

    private void RefreshMenu(BusinessSnapshot snapshot, bool force = false)
    {
        var signature = BuildMenuSignature(snapshot);
        if (!force && string.Equals(signature, _menuSignature, StringComparison.Ordinal))
            return;

        _icon.ContextFlyout = TrayMenu.Create(
            snapshot,
            ActiveDeviceManager,
            _commandDispatcher,
            ShowMainWindow,
            ToggleMiniWindow,
            _exitApplication);
        _menuSignature = signature;
    }

    private string BuildMenuSignature(BusinessSnapshot snapshot)
    {
        var presentation = ActiveDeviceManager?.Presentation;
        var controls = presentation is null
            ? ""
            : string.Join(",", presentation.VisibleControls.OrderBy(value => value)) + "|"
              + string.Join(",", presentation.ControlStates.OrderBy(pair => pair.Key)
                  .Select(pair => $"{pair.Key}={pair.Value}")) + "|"
              + string.Join(",", presentation.NoiseOptions.SelectMany(FlattenNoiseOptionsForSignature));
        return string.Join(
            "|",
            snapshot.IsConnected,
            snapshot.Noise.Mode,
            snapshot.Game.IsEnabled,
            controls);
    }

    private static IEnumerable<string> FlattenNoiseOptionsForSignature(NoiseOptionModel option)
    {
        yield return option.Key;
        foreach (var child in option.Children.SelectMany(FlattenNoiseOptionsForSignature))
            yield return child;
    }

    private static string BuildTooltip(BusinessSnapshot snapshot)
    {
        var name = DeviceText.DeviceName(snapshot.Identity?.ModelName, snapshot.DeviceName);
        if (!snapshot.IsConnected)
            return name;

        var parts = new List<string>();
        AddBattery(parts, "L", snapshot.LeftBattery);
        AddBattery(parts, "R", snapshot.RightBattery);
        AddBattery(parts, "C", snapshot.CaseBattery);
        return parts.Count == 0 ? name : $"{name}\n{string.Join(" ", parts)}";
    }

    private static void AddBattery(List<string> parts, string channel, BatteryLevel? battery)
    {
        if (battery is { } value)
            parts.Add($"{channel}:{value.Percent}%{(value.IsCharging ? "⚡" : "")}");
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        AppDispatcher.Post(() =>
        {
            if (_disposed)
                return;

            _icon.ToolTipText = BuildTooltip(_frontendState.Snapshot);
            RefreshMenu(_frontendState.Snapshot, force: true);
            if (_miniWindow is not null)
            {
                _miniWindow.RefreshAppearance();
                _miniWindow.RefreshAncLabels();
            }
        });
    }

    private void ToggleMiniWindow()
    {
        if (_disposed)
            return;
        ApplicationLog.Current?.Info("Tray", "切换状态小窗。");
        if (_miniWindow is not null)
        {
            _miniWindow.Close();
            return;
        }
        _miniWindow ??= CreateMiniWindow();
        _miniWindow.Activate();
    }

    private StatusWindow CreateMiniWindow()
    {
        var window = new StatusWindow(
            _frontendState,
            _controlManager,
            _commandDispatcher,
            _settings,
            onDeactivated: null);
        // Capture the actual window: a queued deactivation must not close its successor.
        window.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                AppDispatcher.Post(() =>
                {
                    if (ReferenceEquals(_miniWindow, window))
                        window.Close();
                });
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_miniWindow, window))
                _miniWindow = null;
        };
        return window;
    }

    private IBrandManager? ActiveDeviceManager => _controlManager.ActiveManager;

    private void ShowMainWindow()
    {
        if (_disposed)
            return;
        ApplicationLog.Current?.Info("Tray", "托盘请求显示主窗口。");
        _miniWindow?.Close();
        _miniWindow = null;
        var window = _mainWindow();
        if (window is null)
            return;

        window.AppWindow.Show();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        window.Activate();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _frontendState.Changed -= OnFrontendStateChanged;
        _settings.Changed -= OnSettingsChanged;
        _miniWindow?.Close();
        _miniWindow = null;
        // H.NotifyIcon 2.4.1's property callback rejects a null ContextFlyout.
        if (_icon.ContextFlyout is MenuFlyout menu)
            menu.Items.Clear();
        _icon.Dispose();
    }
}
