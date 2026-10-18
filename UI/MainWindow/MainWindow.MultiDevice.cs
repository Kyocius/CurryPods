using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void SyncNextMultiDeviceList(BusinessSnapshot? snapshot)
    {
        var manager = _controlManager?.ActiveManager;
        var connected = snapshot?.IsConnected == true;
        var hiddenCount = _controlManager?.GetHiddenMultiDeviceCount() ?? 0;
        var displayState = _controlManager?.GetMultiDeviceDisplayState()
            ?? new MultiDeviceDisplayState([], []);
        var devices = displayState.VisibleDevices;

        HomeView.DeviceList.Items.Clear();
        _deviceListRows.Clear();
        HomeView.DeviceListEmptyHint.Text = !connected
            ? LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_EmptyHint)
                : devices.Count == 0
                ? LanguageManager.Instance.GetString(hiddenCount > 0
                    ? LanguageManager.Instance.MultiDevice_AllHidden
                    : LanguageManager.Instance.MultiDevice_NoOtherDevices)
                : string.Empty;
        HomeView.DeviceListEmptyHint.Visibility = (!connected || devices.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        SettingsView?.UpdateConnectionStrategyVisibility(connected && devices.Count > 0);
        if (!connected || manager is null)
            return;

        SettingsView?.SyncConnectionStrategy(manager, snapshot!.MultiDevice, displayState.ConnectedDevices);

        foreach (var device in devices)
        {
            var name = DeviceText.MultiDeviceName(device);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 3, 4, 3) };
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = device.ConnectionState switch { 2 => BrushGreen, 1 => BrushGray, _ => BrushRed }
            };
            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = device.IsCurrent ? BrushLightGreen : BrushWhite,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(dot);
            row.Children.Add(nameText);
            var border = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(4),
                Child = row,
                Background = device.IsCurrent ? _deviceCurrentBgBrush : null
            };

            var audioText = new TextBlock
            {
                Visibility = device.IsAudioActive ? Visibility.Visible : Visibility.Collapsed,
                Text = " ♪",
                Foreground = BrushGreen
            };
            var statusText = new TextBlock
            {
                Visibility = (device.ConnectionState != 2 && !device.IsCurrent) ? Visibility.Visible : Visibility.Collapsed,
                Text = GetDeviceConnectionText(device),
                Opacity = 0.4
            };
            row.Children.Add(audioText);
            row.Children.Add(statusText);
            _deviceListRows[device.Address] = new DeviceListRowRefs { Root = border, Dot = dot, NameText = nameText, AudioText = audioText, StatusText = statusText };
            var menu = new MenuFlyout();
            if (!device.IsCurrent && device.ConnectionState != 2)
                AddNextMultiDeviceMenuItem(menu, LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_Connect), MultiDeviceOperation.Connect, device.Address, name);
            else if (device.ConnectionState == 2)
                AddNextMultiDeviceMenuItem(menu, LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_Disconnect), MultiDeviceOperation.Disconnect, device.Address, name);
            if (!device.IsCurrent)
            {
                AddNextMultiDeviceMenuItem(menu, LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_Unpair), MultiDeviceOperation.Unpair, device.Address, name);
                var hide = new MenuFlyoutItem { Text = LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_Hide) };
                hide.Click += (_, _) => HideMultiDevice(device);
                menu.Items.Add(hide);
            }
            if (menu.Items.Count > 0)
                border.ContextFlyout = menu;
            HomeView.DeviceList.Items.Add(border);
        }
    }

    private void AddNextMultiDeviceMenuItem(MenuFlyout menu, string resourceKey, MultiDeviceOperation operation, string address, string name)
    {
        var item = new MenuFlyoutItem { Text = string.Format(resourceKey, name) };
        item.Click += (_, _) => _ = _commandDispatcher?.RunAsync("多设备操作", manager => manager.OperateMultiDeviceAsync(operation, address, CancellationToken.None));
        menu.Items.Add(item);
    }

    private void HideMultiDevice(ConnectedDeviceSnapshot device)
    {
        if (device.IsCurrent || string.IsNullOrWhiteSpace(device.Address)) return;
        if (_controlManager?.HideMultiDevice(device.Address) != true)
            return;
        SyncNextMultiDeviceList(_frontendState?.Snapshot);
        SettingsView?.RefreshRestoreHiddenDevicesButton();
        _logManager?.Debug("UI", $"本地隐藏多设备 addr={device.Address}");
    }

    private string GetDeviceConnectionText(ConnectedDeviceSnapshot d)
    {
        var obs = d.ConnectionState switch
        {
            2 => d.IsCurrent ? LanguageManager.Instance.MultiDevice_StatusCurrentDevice : LanguageManager.Instance.MultiDevice_StatusConnected,
            1 => LanguageManager.Instance.MultiDevice_StatusConnecting,
            _ => LanguageManager.Instance.MultiDevice_StatusDisconnected
        };
        return $" ({LanguageManager.Instance.GetString(obs)})";
    }
}
