using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;
using OppoPodsManager.Control.Subsystems.Notifications;

namespace OppoPodsManager.UI.Toast;

internal static class ToastManager
{
    private static readonly List<ToastWindow> _active = new();

    public static void Register(ToastWindow toast)
    {
        try
        {
            if (!_active.Contains(toast))
                _active.Add(toast);

            while (_active.Count > 1)
            {
                var old = _active[0];
                _active.RemoveAt(0);
                old.Close();
            }
        }
        catch (Exception exception)
        {
            ApplicationLog.Current?.Error("Toast", "Toast 注册失败。", exception);
        }
    }

    public static void Unregister(ToastWindow toast)
    {
        try
        {
            _active.Remove(toast);
        }
        catch (Exception exception)
        {
            ApplicationLog.Current?.Error("Toast", "Toast 注销失败。", exception);
        }
    }

    public static void CloseAll()
    {
        foreach (var toast in _active.ToArray())
            toast.Close();
        _active.Clear();
    }
}

// 监听状态快照；新通知替换旧通知，避免在屏幕底部重叠。
public sealed class ToastNotificationService : IDisposable
{
    private readonly NotificationCoordinator _notificationCoordinator;
    private readonly SettingsManager? _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public ToastNotificationService(NotificationCoordinator notificationCoordinator, SettingsManager? settings = null)
    {
        _notificationCoordinator = notificationCoordinator;
        _settings = settings;
        _notificationCoordinator.NotificationRaised += OnNotificationRaised;
    }

    // 将控制层已分类的设备通知转换为对应的 Toast 窗口类型。
    private void OnNotificationRaised(object? sender, DeviceNotificationRequest request)
    {
        if (_disposed || !IsToastEnabled(request.Kind))
        {
            ApplicationLog.Current?.Debug("Toast", $"弹窗已关闭，跳过通知：kind={request.Kind}。");
            return;
        }

        var type = request.Kind switch
        {
            DeviceNotificationKind.Connected => ToastType.Battery,
            DeviceNotificationKind.Disconnected => ToastType.Disconnected,
            DeviceNotificationKind.LowBattery => ToastType.LowBattery,
            DeviceNotificationKind.CriticalBattery => ToastType.CriticalBattery,
            _ => ToastType.Battery
        };

        var snapshot = request.Snapshot;
        ApplicationLog.Current?.Info("Toast", $"渲染设备通知：kind={request.Kind}，type={type}。");
        _ = ShowSnapshotSafeAsync(snapshot, GetDeviceName(snapshot), type, GetDuration(), _settings, _lifetime.Token);
    }

    private bool IsToastEnabled(DeviceNotificationKind kind) => kind switch
    {
        DeviceNotificationKind.LowBattery or DeviceNotificationKind.CriticalBattery
            => _settings?.Current.LowBatteryToastEnabled ?? true,
        _ => _settings?.Current.ConnectionToastEnabled ?? true
    };

    private static async Task ShowSnapshotSafeAsync(
        BusinessSnapshot snapshot,
        string deviceName,
        ToastType type,
        int durationMs,
        SettingsManager? settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await ToastWindow.ShowSnapshotAsync(snapshot, deviceName, type, durationMs, settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ApplicationLog.Current?.Error("Toast", "显示状态 Toast 失败。", exception);
        }
    }

    private static string GetDeviceName(BusinessSnapshot snapshot)
        => DeviceText.DeviceName(snapshot.Identity?.ModelName, snapshot.DeviceName);

    private int GetDuration()
        => Math.Clamp(_settings?.Current.ToastDurationSeconds ?? 5, 3, 8) * 1000;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _notificationCoordinator.NotificationRaised -= OnNotificationRaised;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
