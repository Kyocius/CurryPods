using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Views;

public sealed partial class SettingsView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }
    private ControlManager? ControlManager;
    private SettingsStore UiSettings = null!;
    private ApplicationLog? Log;
    private CommandDispatcher? CommandDispatcher;
    private FrontendState? FrontendState;
    private DesktopLinkService? DesktopLinks;

    private bool _syncingConnectionStrategy;
    private string _priorityOptionsSignature = "";

    private sealed class PriorityDeviceOption
    {
        public string Address { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public bool IsAutomatic { get; init; }
        public override string ToString() => DisplayName;
    }

    public SettingsView()
    {
        InitializeComponent();
    }

    public void Attach(
        ControlManager? controlManager,
        SettingsStore uiSettings,
        ApplicationLog? log,
        CommandDispatcher? commandDispatcher,
        FrontendState? frontendState,
        DesktopLinkService? desktopLinks)
    {
        ControlManager = controlManager;
        UiSettings = uiSettings;
        Log = log;
        CommandDispatcher = commandDispatcher;
        FrontendState = frontendState;
        DesktopLinks = desktopLinks;

        CbTray.IsChecked = UiSettings.GetBool("TrayEnabled", false);
        CbAuto.IsChecked = UiSettings.GetBool("AutoStart", false);

        CbTray.Checked += CbTray_Changed;
        CbTray.Unchecked += CbTray_Changed;
        CbAuto.Checked += CbAuto_Changed;
        CbAuto.Unchecked += CbAuto_Changed;
        CbPriorityDevice.SelectionChanged += CbPriorityDevice_Changed;
    }

    public void ApplySnapshot(BusinessSnapshot snapshot)
    {
        DiDeviceName.Text = DeviceText.DeviceName(snapshot.Identity?.DisplayName, snapshot.DeviceName);
        DiFirmware.Text = snapshot.Identity?.FirmwareVersion ?? "-";
        DiCodec.Text = snapshot.Identity?.Codec ?? "-";
    }

    public void RefreshDeviceInfo()
    {
        if (FrontendState?.Snapshot is { } snapshot)
            ApplySnapshot(snapshot);
    }

    private void CbTray_Changed(object sender, RoutedEventArgs e)
    {
        var on = CbTray.IsChecked == true;
        UiSettings.SetBool("TrayEnabled", on);
        Log?.Debug("UI", $"设置: 关闭到托盘 -> {on}");
    }

    private void CbAuto_Changed(object sender, RoutedEventArgs e)
    {
        var on = CbAuto.IsChecked == true;
        UiSettings.SetBool("AutoStart", on);
        WindowsStartup.TrySetEnabled(on);
        Log?.Debug("UI", $"设置: 开机自启 -> {on}");
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Host is not null)
                await Host.OpenUpdatePageAsync();
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnFeedback_Click(object sender, RoutedEventArgs e)
        => _ = Host?.OpenFeedbackAsync();

    private void BtnViewLog_Click(object sender, RoutedEventArgs e)
        => Host?.RequestNavigate("log");

    private void About_Click(object sender, RoutedEventArgs e)
        => Host?.RequestNavigate("about");

    private void BtnRestoreHiddenDevices_Click(object sender, RoutedEventArgs e)
    {
        ControlManager?.RestoreHiddenMultiDevices();
        Host?.ResyncMultiDeviceList();
        RefreshRestoreHiddenDevicesButton();
        _ = CommandDispatcher?.RunAsync("刷新多设备列表",
            manager => manager.RefreshMultiDeviceAsync(CancellationToken.None));
        Log?.Debug("UI", "已清除本地隐藏设备策略并同步多设备状态");
    }

    internal void UpdateConnectionStrategyVisibility(bool visible)
    {
        DiConnectionStrategyCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PriorityDevicePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SyncConnectionStrategy(
        IBrandManager manager,
        MultiDeviceSnapshot multiDevice,
        IReadOnlyList<ConnectedDeviceSnapshot> connectedDevices)
    {
        var canManage = manager.Presentation.CanManageMultiDevice;
        DiConnectionStrategyCard.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        PriorityDevicePanel.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        _syncingConnectionStrategy = true;
        try
        {
            if (!canManage)
            {
                CbPriorityDevice.Items.Clear();
                CbPriorityDevice.SelectedItem = null;
                _priorityOptionsSignature = "";
                return;
            }

            var signature = string.Join("|", connectedDevices.Select(device =>
                $"{device.Address};{device.Name};{device.IsCurrent}"))
                + $"|auto={multiDevice.IsAutomaticPriority}|priority={multiDevice.PriorityDeviceAddress}";

            if (signature != _priorityOptionsSignature)
            {
                _priorityOptionsSignature = signature;
                CbPriorityDevice.Items.Clear();
                CbPriorityDevice.Items.Add(new PriorityDeviceOption
                {
                    IsAutomatic = true,
                    DisplayName = LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_Automatic)
                });
                foreach (var device in connectedDevices)
                {
                    CbPriorityDevice.Items.Add(new PriorityDeviceOption
                    {
                        Address = device.Address,
                        DisplayName = DeviceText.MultiDeviceName(device)
                    });
                }
            }

            var selected = multiDevice.IsAutomaticPriority
                ? CbPriorityDevice.Items.OfType<PriorityDeviceOption>().FirstOrDefault(option => option.IsAutomatic)
                : CbPriorityDevice.Items.OfType<PriorityDeviceOption>().FirstOrDefault(option =>
                    string.Equals(option.Address, multiDevice.PriorityDeviceAddress, StringComparison.OrdinalIgnoreCase));

            CbPriorityDevice.SelectedItem = selected;
            CbPriorityDevice.PlaceholderText = selected is null && !multiDevice.IsAutomaticPriority
                ? LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_PriorityUnavailable)
                : LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_PriorityHint);
        }
        finally
        {
            _syncingConnectionStrategy = false;
        }
    }

    internal void ResetPrioritySignature() => _priorityOptionsSignature = "";

    private void CbPriorityDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CbPriorityDevice.SelectedItem is not PriorityDeviceOption option
            || FrontendState?.Snapshot.IsConnected != true)
            return;
        if (_syncingConnectionStrategy) return;
        ApplyPrioritySelection(option);
    }

    private void ApplyPrioritySelection(PriorityDeviceOption option)
    {
        _ = CommandDispatcher?.RunAsync("多设备优先级", manager =>
            manager.SetMultiDevicePriorityAsync(
                option.IsAutomatic,
                option.IsAutomatic ? null : option.Address,
                CancellationToken.None));
    }

    internal void RefreshRestoreHiddenDevicesButton()
    {
        var count = ControlManager?.GetHiddenMultiDeviceCount() ?? 0;
        BtnRestoreHiddenDevices.IsEnabled = count > 0;
        BtnRestoreHiddenDevices.Content = count > 0
            ? string.Format(LanguageManager.Instance.GetString(LanguageManager.Instance.MultiDevice_RestoreHidden), count)
            : LanguageManager.Instance.GetString(LanguageManager.Instance.Settings_RestoreHiddenDevices);
    }
}
