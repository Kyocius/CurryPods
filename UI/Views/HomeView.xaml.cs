using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.VisualAssets;
using OppoPodsManager.Control.Brands.Oppo.Features;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.UI.MainWindow;

using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Views;

/// <summary>
/// 主页视图：承载状态行、电量卡片、ANC 动态 UI、功能开关与空间音频。
/// 原 MainWindow.Device.cs / Anc.cs / Features.cs 中与此页控件相关的渲染与事件逻辑全部迁入此处；
/// 外壳 MainWindow 经 <see cref="ApplySnapshot"/> 路由快照，并经 IViewHost 提供查找耳机警告、EQ 控件启用等外壳级能力。
/// 共享画刷统一取自 <see cref="AppPalette"/>（与后续小窗复用）。
/// </summary>
public partial class HomeView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }
    private ControlManager? ControlManager;
    private SettingsStore UiSettings = null!;
    private ApplicationLog? Log;
    private CommandDispatcher? CommandDispatcher;
    private FrontendState? FrontendState;
    private DesktopLinkService? DesktopLinks;

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
    }

    // ---- 设备连接状态 ----
    private readonly List<DeviceConnectionOption> _nextDevices = new();
    private bool _suppressEarbudSelection;

    // ---- 状态/功能本地状态 ----
    private DateTime _connectionStatusStartedAt = DateTime.MinValue;
    private bool _findDeviceActive;
    private bool _wasConnected;
    private bool _gameSoundCommandPending;
    private bool _realClose;

    public HomeView()
    {
        InitializeComponent();

        // 功能开关（原 MainWindow ctor 程序化接线，现由本视图自持）
        WireToggleSwitch(CbSpatial, CbSpatial_Changed);
        WireToggleSwitch(CbGame, CbGame_Changed);
        WireToggleSwitch(CbGameSound, CbGameSound_Changed);
        WireToggleSwitch(CbDualDevice, CbDualDevice_Changed);
        WireToggleSwitch(CbBassEngine, CbBassEngine_Changed);
        WireToggleSwitch(CbVocalEnhance, CbVocalEnhance_Changed);
        WireToggleSwitch(CbHearingEnhance, CbHearingEnhance_Changed);
        WireToggleSwitch(CbLongPower, CbLongPower_Changed);
        WireToggleSwitch(CbWearDetection, CbWearDetection_Changed);
        WireToggleSwitch(CbSpineHealth, CbSpineHealth_Changed);
        CbDevice.SelectionChanged += CbDevice_Changed;


    }

    private static void WireToggleSwitch(ToggleSwitch cb, RoutedEventHandler handler)
    {
        cb.Toggled += handler;
    }

    /// <summary>外壳在窗口关闭时调用，避免异步回追访问已释放控件。</summary>
    public void MarkClosed() => _realClose = true;

    // 电池图案由外壳经 EarphoneImageProvider 注入（与设备信息页触控图同源）。
    public Image BatteryLeftImage => LeftBatteryImage;
    public Image BatteryRightImage => RightBatteryImage;
    public Image BatteryCaseImage => CaseBatteryImage;

    public void SetAncPanelVisible(bool visible) => AncPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void ApplySnapshot(BusinessSnapshot snapshot)
    {
        if (_realClose)
            return;

        UpdateNextConnectionStatus(snapshot);
        ApplyBatteryLayout(
            ControlManager?.ActiveManager?.Presentation?.BatteryLayout ?? BatteryLayout.Auto,
            snapshot);
        WearStatus.Text = string.Join("  ",
            new[]
            {
                snapshot.Wear.Left == EarWearState.Unknown ? null : DeviceText.WearState(snapshot.Wear.Left),
                snapshot.Wear.Right == EarWearState.Unknown ? null : DeviceText.WearState(snapshot.Wear.Right)
            }.Where(text => !string.IsNullOrWhiteSpace(text)));
        WearStatus.Visibility = !string.IsNullOrWhiteSpace(WearStatus.Text) ? Visibility.Visible : Visibility.Collapsed;

        ApplyNextFeatureState(snapshot);
        ApplyNextSpatialAudioSnapshot(snapshot);
        ApplyNextNoiseSnapshot(snapshot);
        BtnReconnect.Visibility = !snapshot.IsConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    #region 连接状态与设备列表（原 MainWindow.Device.cs）

    private void UpdateNextConnectionStatus(BusinessSnapshot snapshot)
    {
        StatusDot.Fill = snapshot.IsConnected ? AppPalette.BrushGreen : AppPalette.BrushRed;
        StatusText.Foreground = snapshot.IsConnected ? AppPalette.BrushLightGreen : AppPalette.BrushLightRed;

        if (!snapshot.IsConnected)
        {
            _wasConnected = false;
            _connectionStatusStartedAt = DateTime.MinValue;
            StatusText.Text = TranslationCatalog.Get("Status_Disconnected");
            return;
        }

        if (!_wasConnected)
        {
            _wasConnected = true;
            _connectionStatusStartedAt = DateTime.Now;
        }

        var manager = ControlManager?.ActiveManager;
        var modelName = manager?.Presentation.IsKnownModel == true
            ? manager.Presentation.ModelName
            : snapshot.Identity?.ModelName;
        if (!string.IsNullOrWhiteSpace(modelName)
            && !string.Equals(modelName, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            // 该文本现在是电量卡片的标题（原顶部状态条已并入卡片），因此直接显示设备名；
            // 连接状态由左侧圆点颜色表达，不再重复「已连接 —」前缀。
            StatusText.Text = modelName;
            return;
        }

        var identifyingText = TranslationCatalog.Get("Status_Identifying");
        var unidentifiedText = TranslationCatalog.Get("Status_Unidentified");
        StatusText.Text = DateTime.Now - _connectionStatusStartedAt < TimeSpan.FromSeconds(2)
            ? identifyingText
            : unidentifiedText;
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        Log?.Debug("UI", "用户操作: 刷新多耳机列表");
        if (ControlManager is null)
            return;

        try
        {
            var devices = await ControlManager.RefreshAvailableDevicesAsync(CancellationToken.None);
            AppDispatcher.Post(() => ApplyNextDevices(devices));
        }
        catch (Exception exception)
        {
            Log?.Error("UI", $"刷新设备列表失败：{exception.Message}", exception);
        }
    }

    internal void ApplyNextDevices(IReadOnlyList<DeviceConnectionOption> devices)
    {
        if (_realClose)
            return;
        var selectedId = (CbDevice.SelectedItem as DeviceConnectionOption)?.Id
            ?? ControlManager?.ActiveDeviceId;
        _nextDevices.Clear();
        _nextDevices.AddRange(devices);
        _suppressEarbudSelection = true;
        CbDevice.Items.Clear();
        foreach (var device in _nextDevices)
            CbDevice.Items.Add(device);
        CbDevice.Visibility = _nextDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnRefreshDevices.Visibility = _nextDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var selectedIndex = selectedId is null
            ? -1
            : _nextDevices.FindIndex(device => string.Equals(device.Id, selectedId, StringComparison.Ordinal));
        if (selectedIndex < 0 && _nextDevices.Count > 0 && ControlManager?.ActiveManager is null)
            selectedIndex = 0;
        CbDevice.SelectedIndex = selectedIndex;
        _suppressEarbudSelection = false;
        Log?.Debug("UI", $"设备选择列表已更新：count={_nextDevices.Count}，selected={selectedId ?? ""}。");
    }

    private void CbDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEarbudSelection || ControlManager is null
            || CbDevice.SelectedIndex < 0 || CbDevice.SelectedIndex >= _nextDevices.Count)
            return;

        var selected = _nextDevices[CbDevice.SelectedIndex];
        Log?.Info("UI", $"用户选择设备：id={selected.Id}，name={selected.DisplayName}。");
        _ = ConnectNextDeviceAsync(selected.Id);
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        Log?.Debug("UI", "用户操作: 点击重连");
        _ = ConnectNextDeviceAsync(null);
    }

    private async Task ConnectNextDeviceAsync(string? deviceId)
    {
        if (ControlManager is null)
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                await ControlManager.ConnectFirstAvailableAsync(CancellationToken.None);
            else
                await ControlManager.ConnectAsync(deviceId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            Log?.Error("UI", $"连接设备失败：{exception.Message}", exception);
        }
    }

    internal async Task RefreshNextDevicesAsync()
    {
        if (ControlManager is null)
            return;

        var devices = await ControlManager.RefreshAvailableDevicesAsync(CancellationToken.None);
        AppDispatcher.Post(() => ApplyNextDevices(devices));
    }

    #endregion

    #region 功能开关与空间音频（原 MainWindow.Features.cs）

    private void ApplyNextFeatureState(BusinessSnapshot snapshot)
    {
        var manager = ControlManager?.ActiveManager;
        var presentation = manager?.Presentation;
        var hasFeatures = snapshot.IsConnected && presentation is not null;
        FeaturePlaceholderText.Text = TranslationCatalog.Get(snapshot.IsConnected ? "Feature_Unsupported" : "Feature_Placeholder");
        FeatureContentPanel.Visibility = hasFeatures ? Visibility.Visible : Visibility.Collapsed;
        FeaturePlaceholderText.Visibility = !hasFeatures ? Visibility.Visible : Visibility.Collapsed;
        if (!hasFeatures)
        {
            BtnFindDevice.Visibility = Visibility.Collapsed;
            SetFeatureControlsEnabled(false);
            _findDeviceActive = false;
            BtnFindDevice.Content = TranslationCatalog.Get("Feature_FindDevice");
            return;
        }

        var visibleControls = presentation!.VisibleControls;
        var controlStates = presentation.ControlStates;
        var controlEnabledStates = presentation.ControlEnabledStates;
        SetFeatureControlsEnabled(controlEnabledStates);
        CbDualDevice.Visibility = visibleControls.Contains("dual-device") ? Visibility.Visible : Visibility.Collapsed;
        CbBassEngine.Visibility = visibleControls.Contains("bass-engine") ? Visibility.Visible : Visibility.Collapsed;
        CbVocalEnhance.Visibility = visibleControls.Contains("voice-enhancement") ? Visibility.Visible : Visibility.Collapsed;
        CbHearingEnhance.Visibility = visibleControls.Contains("hearing-enhancement") ? Visibility.Visible : Visibility.Collapsed;
        CbLongPower.Visibility = visibleControls.Contains("long-battery") ? Visibility.Visible : Visibility.Collapsed;
        CbWearDetection.Visibility = visibleControls.Contains("wear-detection") ? Visibility.Visible : Visibility.Collapsed;
        CbSpineHealth.Visibility = visibleControls.Contains("spine-health") ? Visibility.Visible : Visibility.Collapsed;
        CbSpatial.Visibility = visibleControls.Contains("spatial-sound") ? Visibility.Visible : Visibility.Collapsed;
        CbGameSound.Visibility = visibleControls.Contains("game-sound") ? Visibility.Visible : Visibility.Collapsed;
        CbGame.Visibility = visibleControls.Contains("game-mode") ? Visibility.Visible : Visibility.Collapsed;
        BtnFindDevice.Visibility = visibleControls.Contains("find-device") ? Visibility.Visible : Visibility.Collapsed;
        var hasVisibleFeature = CbDualDevice.Visibility == Visibility.Visible
            || CbBassEngine.Visibility == Visibility.Visible
            || CbVocalEnhance.Visibility == Visibility.Visible
            || CbHearingEnhance.Visibility == Visibility.Visible
            || CbLongPower.Visibility == Visibility.Visible
            || CbWearDetection.Visibility == Visibility.Visible
            || CbSpineHealth.Visibility == Visibility.Visible
            || CbSpatial.Visibility == Visibility.Visible
            || CbGameSound.Visibility == Visibility.Visible
            || CbGame.Visibility == Visibility.Visible
            || BtnFindDevice.Visibility == Visibility.Visible;
        FeatureContentPanel.Visibility = hasVisibleFeature ? Visibility.Visible : Visibility.Collapsed;
        FeaturePlaceholderText.Visibility = !hasVisibleFeature ? Visibility.Visible : Visibility.Collapsed;
        BtnFindDevice.IsEnabled = BtnFindDevice.Visibility == Visibility.Visible && snapshot.IsConnected;

        SetNextCheck(CbDualDevice, controlStates, "dual-device", CbDualDevice_Changed);
        SetNextCheck(CbBassEngine, controlStates, "bass-engine", CbBassEngine_Changed);
        SetNextCheck(CbVocalEnhance, controlStates, "voice-enhancement", CbVocalEnhance_Changed);
        SetNextCheck(CbHearingEnhance, controlStates, "hearing-enhancement", CbHearingEnhance_Changed);
        SetNextCheck(CbLongPower, controlStates, "long-battery", CbLongPower_Changed);
        SetNextCheck(CbWearDetection, controlStates, "wear-detection", CbWearDetection_Changed);
        SetNextCheck(CbSpineHealth, controlStates, "spine-health", CbSpineHealth_Changed);
        SetNextCheck(CbSpatial, controlStates, "spatial-sound", CbSpatial_Changed);
        if (!_gameSoundCommandPending && snapshot.Game.SoundType is { } gameSoundType)
            SetGameSoundCheckedSilent(gameSoundType != 0);
        if (controlStates.TryGetValue("game-mode", out var gameMode))
            SetGameCheckedSilent(gameMode);

        SetGameSoundEnabledSilent(GetControlEnabled(controlEnabledStates, "game-sound"));
        SetSpatialEnabledSilent(GetControlEnabled(controlEnabledStates, "spatial-sound"));
        Host?.SetEqControlsEnabled(GetControlEnabled(controlEnabledStates, "equalizer"));
    }

    private static bool GetControlEnabled(IReadOnlyDictionary<string, bool> states, string key)
        => !states.TryGetValue(key, out var enabled) || enabled;

    private void SetFeatureControlsEnabled(bool enabled)
    {
        SetControlEnabledSilent(CbDualDevice, enabled);
        SetControlEnabledSilent(CbBassEngine, enabled);
        SetControlEnabledSilent(CbVocalEnhance, enabled);
        SetControlEnabledSilent(CbHearingEnhance, enabled);
        SetControlEnabledSilent(CbLongPower, enabled);
        SetControlEnabledSilent(CbWearDetection, enabled);
        SetControlEnabledSilent(CbSpineHealth, enabled);
        SetControlEnabledSilent(CbSpatial, enabled);
        SetControlEnabledSilent(CbGameSound, enabled);
        SetControlEnabledSilent(CbGame, enabled);
        SetControlEnabledSilent(BtnFindDevice, enabled);
        Host?.SetEqControlsEnabled(enabled);
    }

    private void SetFeatureControlsEnabled(IReadOnlyDictionary<string, bool> states)
    {
        SetControlEnabledSilent(CbDualDevice, GetControlEnabled(states, "dual-device"));
        SetControlEnabledSilent(CbBassEngine, GetControlEnabled(states, "bass-engine"));
        SetControlEnabledSilent(CbVocalEnhance, GetControlEnabled(states, "voice-enhancement"));
        SetControlEnabledSilent(CbHearingEnhance, GetControlEnabled(states, "hearing-enhancement"));
        SetControlEnabledSilent(CbLongPower, GetControlEnabled(states, "long-battery"));
        SetControlEnabledSilent(CbWearDetection, GetControlEnabled(states, "wear-detection"));
        SetControlEnabledSilent(CbSpineHealth, GetControlEnabled(states, "spine-health"));
        SetControlEnabledSilent(CbGame, GetControlEnabled(states, "game-mode"));
        SetControlEnabledSilent(BtnFindDevice, GetControlEnabled(states, "find-device"));
        SetGameSoundEnabledSilent(GetControlEnabled(states, "game-sound"));
        SetSpatialEnabledSilent(GetControlEnabled(states, "spatial-sound"));
        Host?.SetEqControlsEnabled(GetControlEnabled(states, "equalizer"));
    }

    private void ApplyNextSpatialAudioSnapshot(BusinessSnapshot snapshot)
    {
        var manager = ControlManager?.ActiveManager;
        if (manager is null || !snapshot.IsConnected)
        {
            SpatialAudioPanel.Visibility = Visibility.Collapsed;
            SpatialAudioModes.Children.Clear();
            return;
        }

        var enabled = manager.Presentation.SupportsSpatialAudio;
        if (!enabled)
        {
            SpatialAudioPanel.Visibility = Visibility.Collapsed;
            SpatialAudioModes.Children.Clear();
            return;
        }

        SpatialAudioPanel.Visibility = Visibility.Visible;

        if (SpatialAudioModes.Children.Count == 0)
        {
            foreach (var (mode, key) in new[]
            {
                ("Off", "SpatialAudio_ModeOff"),
                ("Fixed", "SpatialAudio_ModeFixed"),
                ("HeadTracking", "SpatialAudio_ModeHeadTrack")
            })
            {
                var button = new RadioButton
                {
                    Content = TranslationCatalog.Get(key),
                    Tag = mode,
                    GroupName = "SpatialAudioMode",
                    Margin = new Thickness(8, 2, 8, 2)
                };
                button.Checked += SpatialAudio_Changed;
                SpatialAudioModes.Children.Add(button);
            }
        }

        var selected = snapshot.SpatialAudio.Mode switch
        {
            SpatialAudioMode.Fixed => "Fixed",
            SpatialAudioMode.HeadTracking => "HeadTracking",
            _ => "Off"
        };
        foreach (var control in SpatialAudioModes.Children)
        {
            if (control is not RadioButton button || button.Tag is not string tag)
                continue;
            button.Checked -= SpatialAudio_Changed;
            button.IsChecked = tag == selected;
            button.Checked += SpatialAudio_Changed;
        }
    }

    #region 电量布局（按实际上报的槽位推导栏数）

    // 上一次应用的「面板→耳机图案槽位」映射签名，避免每次快照都重建位图。
    private string _batteryImageSignature = "";

    /// <summary>
    /// 按设备实际上报的电量槽位渲染电量区：1 个槽位→单栏居中（头戴式 / 颈挂）、
    /// 2 个→两栏、3 个→三栏（真无线带盒）。栏数、每栏承载的槽位、标题与耳机图案全部随数据走，
    /// 不依赖型号白名单声明；<see cref="BatteryLayout.SingleBattery"/> 与
    /// <see cref="BatteryLayout.DualEarWithCase"/> 仅作为个别型号的显式覆盖。
    /// 未上报任何电量时按三栏占位，避免连接过程中布局跳变。
    /// </summary>
    private void ApplyBatteryLayout(BatteryLayout declared, BusinessSnapshot snapshot)
    {
        // 下标约定固定为 左(0) → 右(1) → 盒(2)，与下面的面板 / 标题 / 图案数组一一对应。
        var levels = new[] { snapshot.LeftBattery, snapshot.RightBattery, snapshot.CaseBattery };
        var titleKeys = new[] { "Battery_Left", "Battery_Right", "Battery_Case" };
        var defaultSlots = new[] { EarphoneSlot.HomeLeft, EarphoneSlot.HomeRight, EarphoneSlot.Case };
        var panels = new[] { LeftBatteryPanel, RightBatteryPanel, CaseBatteryPanel };
        var titles = new[] { LeftBatteryTitle, RightBatteryTitle, CaseBatteryTitle };
        var labels = new[] { LeftLabel, RightLabel, CaseLabel };
        var bolts = new[] { LeftChargeBolt, RightChargeBolt, CaseChargeBolt };
        var progresses = new[] { LeftBatteryProgress, RightBatteryProgress, CaseBatteryProgress };

        var active = new List<int>();
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i] is not null)
                active.Add(i);
        }

        // 显式声明优先；Auto 时由非空槽位数量推导（0 个 → 三栏占位，1 个 → 单栏，2/3 个 → 多栏）。
        var single = declared == BatteryLayout.SingleBattery
            || (declared == BatteryLayout.Auto && active.Count == 1);

        if (single)
        {
            // 单栏：取唯一非空槽位（无数据时用左槽占位）居中显示，图案换成整机图。
            var source = active.Count > 0 ? active[0] : 0;
            SetBatteryColumns(1);
            for (var i = 0; i < panels.Length; i++)
            {
                panels[i].Visibility = i == source ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetColumn(panels[i], 0);
            }
            titles[source].Text = TranslationCatalog.Get("Battery_Title");
            SetNextBattery(labels[source], bolts[source], progresses[source], levels[source]);
            ApplyBatteryImages(new[] { (source, EarphoneSlot.Headphone) });
            return;
        }

        // 多栏：按激活槽位顺序依次占据各列；无数据时三栏占位。
        var shown = active.Count > 0 ? active : new List<int> { 0, 1, 2 };
        SetBatteryColumns(shown.Count);
        for (var i = 0; i < panels.Length; i++)
        {
            var isShown = shown.Contains(i);
            panels[i].Visibility = isShown ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(panels[i], isShown ? shown.IndexOf(i) : 0);
            if (!isShown)
                continue;
            titles[i].Text = TranslationCatalog.Get(titleKeys[i]);
            SetNextBattery(labels[i], bolts[i], progresses[i], levels[i]);
        }
        ApplyBatteryImages(shown.Select(index => (index, defaultSlots[index])).ToArray());
    }

    private void SetBatteryColumns(int count)
    {
        if (BatteryGrid.ColumnDefinitions.Count == count)
            return;

        BatteryGrid.ColumnDefinitions.Clear();
        for (var i = 0; i < count; i++)
            BatteryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    /// <summary>用户更换自定义耳机图案后由外壳调用，强制重设电量区图案。</summary>
    public void RefreshBatteryImages()
    {
        _batteryImageSignature = "";
        if (FrontendState?.Snapshot is { } snapshot)
            ApplyBatteryLayout(
                ControlManager?.ActiveManager?.Presentation?.BatteryLayout ?? BatteryLayout.Auto,
                snapshot);
    }

    // 仅在「面板→图案槽位」映射变化时重设图片，避免每次快照都重建位图（泄漏与闪烁）。
    private void ApplyBatteryImages((int Panel, EarphoneSlot Slot)[] mapping)
    {
        var signature = string.Join(",", mapping.Select(entry => $"{entry.Panel}:{entry.Slot}"));
        if (signature == _batteryImageSignature)
            return;
        _batteryImageSignature = signature;
        if (Host is null)
            return;
        var images = new[] { LeftBatteryImage, RightBatteryImage, CaseBatteryImage };
        foreach (var (panel, slot) in mapping)
            Host.ApplyEarphoneImage(images[panel], slot);
    }

    #endregion

    private static void SetNextBattery(TextBlock label, FrameworkElement bolt, ProgressBar progress, BatteryLevel? battery)
    {
        if (battery is not { } value)
        {
            label.Text = "-%";
            bolt.Visibility = Visibility.Collapsed;
            progress.Value = 0;
            progress.Visibility = Visibility.Collapsed;
            return;
        }

        label.Text = $"{value.Percent}%";
        bolt.Visibility = value.IsCharging ? Visibility.Visible : Visibility.Collapsed;
        progress.Value = value.Percent;
        progress.Foreground = value.Percent <= 20
            ? AppPalette.BrushBatteryLow
            : value.Percent <= 60 ? AppPalette.BrushBatteryMid : AppPalette.BrushBatteryHigh;
        progress.Visibility = Visibility.Visible;
    }

    private void SpatialAudio_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && button.IsChecked == true && button.Tag is string mode)
            _ = CommandDispatcher?.RunAsync("空间音频", manager => manager.SetSpatialAudioByKeyAsync(mode, CancellationToken.None));
    }

    private void CbSpatial_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("空间声场", manager => manager.SetSpatialSoundAsync(CbSpatial.IsOn, CancellationToken.None));
    }

    private void CbGame_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("游戏模式", manager => manager.SetGameModeAsync(CbGame.IsOn, CancellationToken.None));
    }

    private void CbGameSound_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = RunGameSoundCommandAsync(CbGameSound.IsOn);
    }

    private void SetGameSoundCheckedSilent(bool value)
    {
        if (CbGameSound.IsOn == value)
            return;
        CbGameSound.Toggled -= CbGameSound_Changed;
        CbGameSound.IsOn = value;
        CbGameSound.Toggled += CbGameSound_Changed;
    }

    private static void SetControlEnabledSilent(FrameworkElement control, bool enabled)
    {
        if (control is Microsoft.UI.Xaml.Controls.Control c && c.IsEnabled != enabled)
            c.IsEnabled = enabled;
    }

    private void SetGameSoundEnabledSilent(bool enabled)
        => SetControlEnabledSilent(CbGameSound, enabled);

    private void SetSpatialEnabledSilent(bool enabled)
        => SetControlEnabledSilent(CbSpatial, enabled);

    private void SetGameCheckedSilent(bool value)
    {
        CbGame.Toggled -= CbGame_Changed;
        CbGame.IsOn = value;
        CbGame.Toggled += CbGame_Changed;
    }

    private void CbDualDevice_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("双设备", manager => manager.SetDualDeviceAsync(CbDualDevice.IsOn, CancellationToken.None));
    }

    private void CbBassEngine_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("低音引擎", manager => manager.SetBassEngineAsync(CbBassEngine.IsOn, CancellationToken.None));
    }

    private void CbVocalEnhance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("人声增强", manager => manager.SetVoiceEnhancementAsync(CbVocalEnhance.IsOn, CancellationToken.None));
    }

    private void CbHearingEnhance_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("听力增强", manager => manager.SetHearingEnhancementAsync(CbHearingEnhance.IsOn, CancellationToken.None));
    }

    private void CbLongPower_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("长续航", manager => manager.SetLongBatteryAsync(CbLongPower.IsOn, CancellationToken.None));
    }

    private void CbWearDetection_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("佩戴检测", manager => manager.SetWearDetectionAsync(CbWearDetection.IsOn, CancellationToken.None));
    }

    private void CbSpineHealth_Changed(object sender, RoutedEventArgs e)
    {
        if (!_realClose)
            _ = CommandDispatcher?.RunAsync("脊柱健康", manager => manager.SetSpineHealthAsync(CbSpineHealth.IsOn, CancellationToken.None));
    }

    private async void BtnFindDevice_Click(object sender, RoutedEventArgs e)
    {
        if (ControlManager?.ActiveManager is null)
            return;

        if (!_findDeviceActive)
        {
            if (Host is null) return;
            var confirmed = await Host.ShowFindWarningDialogAsync();
            if (!confirmed)
                return;
        }

        _findDeviceActive = !_findDeviceActive;
        BtnFindDevice.Content = _findDeviceActive
            ? TranslationCatalog.Get("Feature_StopFindDevice")
            : TranslationCatalog.Get("Feature_FindDevice");
        _ = CommandDispatcher?.RunAsync("查找耳机", manager => manager.SetFindDeviceAsync(_findDeviceActive, CancellationToken.None));
    }

    internal void RefreshSpatialAudioLabels()
    {
        foreach (var c in SpatialAudioModes.Children)
        {
            if (c is not RadioButton rb || rb.Tag is not string mode) continue;
            rb.Content = mode switch
            {
                "Fixed" => LanguageManager.Instance.GetString(LanguageManager.Instance.SpatialAudio_ModeFixed),
                "Track" => LanguageManager.Instance.GetString(LanguageManager.Instance.SpatialAudio_ModeHeadTrack),
                _ => LanguageManager.Instance.GetString(LanguageManager.Instance.SpatialAudio_ModeOff),
            };
        }
    }

    private static void SetNextCheck(
        ToggleSwitch checkBox,
        IReadOnlyDictionary<string, bool> states,
        string featureName,
        RoutedEventHandler handler)
    {
        if (states.TryGetValue(featureName, out var enabled))
        {
            checkBox.Toggled -= handler;
            checkBox.IsOn = enabled;
            checkBox.Toggled += handler;
        }
    }

    #endregion
}
