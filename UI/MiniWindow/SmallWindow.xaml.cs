using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;

using WinRT.Interop;
using Windows.Graphics;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.VisualAssets;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Oppo.Features;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;
using OppoPodsManager.UI.MainWindow;

namespace OppoPodsManager.UI.MiniWindow.Status;

public partial class SmallWindow : Window
{
    private readonly Action? _onDeactivated;
    private FrontendState? _frontendState;
    private ControlManager? _controlManager;
    private CommandDispatcher? _commandDispatcher;
    private IBrandManager? _nextManager;
    private OppoPodsManager.Assets.UserSettings.SettingsManager? _nextSettings;
    private IDisposable? _interactiveSurface;
    private AppWindow? _appWindow;

    private static readonly SolidColorBrush BatteryColorYellow = new(ColorHelper.FromArgb(0xFF, 0xFF, 0xCC, 0x00));
    private static readonly SolidColorBrush BatteryColorRed    = new(ColorHelper.FromArgb(0xFF, 0xFF, 0x3B, 0x30));

    private string _noiseSignature = string.Empty;
    private bool _syncingNoise;
    private DisplayArea? _displayArea;
    private bool _positioning;
    private bool _refreshPending;
    private bool _isClosed;
    private bool _gameSoundCommandPending;
    private bool _syncingFeatures;

    public SmallWindow()
    {
        InitializeWindow();
    }

    public SmallWindow(
        FrontendState frontendState,
        ControlManager controlManager,
        CommandDispatcher commandDispatcher,
        OppoPodsManager.Assets.UserSettings.SettingsManager? settings = null,
        Action? onDeactivated = null)
    {
        _onDeactivated = onDeactivated;
        _controlManager = controlManager;
        _commandDispatcher = commandDispatcher;
        _nextSettings = settings;
        InitializeWindow();
        _frontendState = frontendState;
        _nextManager = controlManager.ActiveManager;
        _frontendState.Changed += OnNextStateChanged;
        _interactiveSurface = _frontendState.AcquireInteractiveSurface();
        ApplyNextSnapshot(_frontendState.Snapshot);
    }

    private void InitializeWindow()
    {
        InitializeComponent();
        SetupAppWindow();
        ApplicationLog.Current?.Debug("UI", "SmallWindow: 打开");

        Closed += (_, _) =>
        {
            ApplicationLog.Current?.Debug("UI", "SmallWindow: 关闭");
            _isClosed = true;
            if (_frontendState is not null)
                _frontendState.Changed -= OnNextStateChanged;
            _interactiveSurface?.Dispose();
            _interactiveSurface = null;
            _nextManager = null;
            LeftImage.Source = null;
            RightImage.Source = null;
            CaseImage.Source = null;
            RootGrid.Loaded -= RootGrid_Loaded;
            SystemBackdrop = null;
            Content = null;
        };

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                try { _onDeactivated?.Invoke(); }
                catch (Exception ex) { ApplicationLog.Current?.Debug("UI", $"SmallWindow Deactivated 回调异常: {ex.Message}"); }
            }
        };

        // 功能复选框接线
        WireToggleSwitch(CbDualDevice);
        WireToggleSwitch(CbGame);
        WireToggleSwitch(CbLongPower);
        WireToggleSwitch(CbWearDetection);
        WireToggleSwitch(CbSpineHealth);
        WireToggleSwitch(CbSpatial);
        WireToggleSwitch(CbGameSound);
        WireToggleSwitch(CbBassEngine);
        WireToggleSwitch(CbVocalEnhance);
        WireToggleSwitch(CbHearingEnhance);

        LoadEarphoneImages();
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
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.IsShownInSwitchers = false;
        GetCursorPos(out var cursor);
        _displayArea = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        if (_displayArea is { } display)
            _appWindow.Move(new PointInt32(display.WorkArea.X + display.WorkArea.Width / 2, display.WorkArea.Y + display.WorkArea.Height / 2));
        RefreshAppearance();
        RootGrid.Loaded += RootGrid_Loaded;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs args) => PositionWindow();

    private void PositionWindow()
    {
        if (_isClosed || _positioning || _appWindow is null || _displayArea is null)
            return;
        _positioning = true;
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var scale = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
            var area = _displayArea.WorkArea;
            var gap = (int)Math.Round(12 * scale);
            RootGrid.Width = Math.Min(360, (area.Width - 2 * gap) / scale);
            RootGrid.MaxHeight = Math.Min(560, (area.Height - 2 * gap) / scale);
            RootGrid.Measure(new Windows.Foundation.Size(RootGrid.Width, RootGrid.MaxHeight));
            var width = (int)Math.Ceiling(RootGrid.DesiredSize.Width * scale);
            var height = (int)Math.Ceiling(RootGrid.DesiredSize.Height * scale);
            GetClientRect(hwnd, out var client);
            var outer = _appWindow.Size;
            _appWindow.Resize(new SizeInt32(width + outer.Width - client.Right, height + outer.Height - client.Bottom));
            outer = _appWindow.Size;
            _appWindow.Move(new PointInt32(area.X + area.Width - outer.Width - gap, area.Y + area.Height - outer.Height - gap));
        }
        finally { _positioning = false; }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rectangle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    private void WireToggleSwitch(ToggleSwitch cb)
    {
        cb.Toggled += FeatureCheck_Changed;

    }

    private void OnNextStateChanged(object? sender, BusinessSnapshot snapshot)
    {
        AppDispatcher.Post(() => ApplyNextSnapshot(snapshot));
    }

    private void ApplyNextSnapshot(BusinessSnapshot snapshot)
    {
        if (_isClosed)
            return;

        SetNextBattery(LeftLabel, LeftChargeBolt, snapshot.LeftBattery);
        SetNextBattery(RightLabel, RightChargeBolt, snapshot.RightBattery);
        SetNextBattery(CaseLabel, CaseChargeBolt, snapshot.CaseBattery);
        Title = DeviceText.DeviceName(snapshot.Identity?.ModelName, snapshot.DeviceName);
        DeviceTitle.Text = Title;
        ConnectionStatus.Text = TranslationCatalog.Get(snapshot.IsConnected ? "Toast_Connected" : "Status_Disconnected");
        var manager = _controlManager?.ActiveManager ?? _nextManager;
        if (manager is not null)
        {
            var presentation = manager.Presentation;
            AncCard.Visibility = (snapshot.IsConnected && presentation.SupportsNoiseCancellation) ? Visibility.Visible : Visibility.Collapsed;
            if (AncCard.Visibility == Visibility.Visible)
            {
                SyncNoiseOptions(presentation);
            }
        }
        else
        {
            AncCard.Visibility = Visibility.Collapsed;
        }
        ApplyNextFeatureState(snapshot);
        PositionWindow();
        ApplicationLog.Current?.Debug("UI", $"小窗已应用快照：revision={snapshot.Revision}，connected={snapshot.IsConnected}。");
    }

    private static void SetNextBattery(TextBlock label, FrameworkElement bolt, BatteryLevel? battery)
    {
        if (battery is { } value)
        {
            label.Text = $"{value.Percent}%";
            if (value.Percent <= 20)
                label.Foreground = value.Percent < 10 ? BatteryColorRed : BatteryColorYellow;
            else
                label.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            label.Text = "–%";
            label.ClearValue(TextBlock.ForegroundProperty);
        }
        bolt.Visibility = (battery?.IsCharging == true) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyNextFeatureState(BusinessSnapshot snapshot)
    {
        var manager = _controlManager?.ActiveManager ?? _nextManager;
        var presentation = manager?.Presentation;
        var hasFeatures = snapshot.IsConnected && presentation is not null;
        FeatureCard.Visibility = hasFeatures ? Visibility.Visible : Visibility.Collapsed;
        FeatureContentPanel.Visibility = Visibility.Collapsed;
        FeaturePlaceholderText.Visibility = !hasFeatures ? Visibility.Visible : Visibility.Collapsed;
        if (!hasFeatures)
            return;

        var visibleControls = presentation!.VisibleControls;
        var controlStates = presentation.ControlStates;
        var controlEnabledStates = presentation.ControlEnabledStates;

        SetNextFeatureCheck(CbDualDevice, visibleControls.Contains("dual-device"), controlStates, "dual-device");
        SetNextFeatureCheck(CbBassEngine, visibleControls.Contains("bass-engine"), controlStates, "bass-engine");
        SetNextFeatureCheck(CbVocalEnhance, visibleControls.Contains("voice-enhancement"), controlStates, "voice-enhancement");
        SetNextFeatureCheck(CbHearingEnhance, visibleControls.Contains("hearing-enhancement"), controlStates, "hearing-enhancement");
        SetNextFeatureCheck(CbLongPower, visibleControls.Contains("long-battery"), controlStates, "long-battery");
        SetNextFeatureCheck(CbWearDetection, visibleControls.Contains("wear-detection"), controlStates, "wear-detection");
        SetNextFeatureCheck(CbSpineHealth, visibleControls.Contains("spine-health"), controlStates, "spine-health");
        SetNextFeatureCheck(CbSpatial, visibleControls.Contains("spatial-sound"), controlStates, "spatial-sound");
        if (!_gameSoundCommandPending && snapshot.Game.SoundType is { } gameSoundType)
            SetNextFeatureCheck(CbGameSound, visibleControls.Contains("game-sound"),
                new Dictionary<string, bool> { ["game-sound"] = gameSoundType != 0 }, "game-sound");
        else
            SetNextFeatureCheck(CbGameSound, visibleControls.Contains("game-sound"), controlStates, "game-sound");
        SetNextFeatureCheck(CbGame, visibleControls.Contains("game-mode"), controlStates, "game-mode");

        var hasVisibleFeature = CbDualDevice.Visibility == Visibility.Visible
            || CbBassEngine.Visibility == Visibility.Visible
            || CbVocalEnhance.Visibility == Visibility.Visible
            || CbHearingEnhance.Visibility == Visibility.Visible
            || CbLongPower.Visibility == Visibility.Visible
            || CbWearDetection.Visibility == Visibility.Visible
            || CbSpineHealth.Visibility == Visibility.Visible
            || CbSpatial.Visibility == Visibility.Visible
            || CbGameSound.Visibility == Visibility.Visible
            || CbGame.Visibility == Visibility.Visible;
        FeatureContentPanel.Visibility = hasVisibleFeature ? Visibility.Visible : Visibility.Collapsed;
        FeaturePlaceholderText.Visibility = !hasVisibleFeature ? Visibility.Visible : Visibility.Collapsed;

        SetFeatureEnabled(CbDualDevice, controlEnabledStates, "dual-device");
        SetFeatureEnabled(CbBassEngine, controlEnabledStates, "bass-engine");
        SetFeatureEnabled(CbVocalEnhance, controlEnabledStates, "voice-enhancement");
        SetFeatureEnabled(CbHearingEnhance, controlEnabledStates, "hearing-enhancement");
        SetFeatureEnabled(CbLongPower, controlEnabledStates, "long-battery");
        SetFeatureEnabled(CbWearDetection, controlEnabledStates, "wear-detection");
        SetFeatureEnabled(CbSpineHealth, controlEnabledStates, "spine-health");
        SetFeatureEnabled(CbSpatial, controlEnabledStates, "spatial-sound");
        SetFeatureEnabled(CbGameSound, controlEnabledStates, "game-sound");
        SetFeatureEnabled(CbGame, controlEnabledStates, "game-mode");
    }

    private void SetNextFeatureCheck(ToggleSwitch box, bool visible, IReadOnlyDictionary<string, bool> states, string key)
    {
        box.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!states.TryGetValue(key, out var enabled) || box.IsOn == enabled)
            return;
        _syncingFeatures = true;
        try
        {
            box.IsOn = enabled;
        }
        finally
        {
            _syncingFeatures = false;
        }
    }

    private static void SetFeatureEnabled(ToggleSwitch box, IReadOnlyDictionary<string, bool> states, string key)
    {
        var enabled = !states.TryGetValue(key, out var value) || value;
        if (box.IsEnabled != enabled)
            box.IsEnabled = enabled;
    }

    private void FeatureCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingFeatures || _isClosed)
            return;
        if (sender is not ToggleSwitch box || box.Tag is not string key)
            return;
        SendFeatureCommand(key, box.IsOn);
    }

    private void SendFeatureCommand(string key, bool enabled)
    {
        if (_commandDispatcher is null)
            return;
        switch (key)
        {
            case "dual-device":
                _ = _commandDispatcher.RunAsync("双设备", m => m.SetDualDeviceAsync(enabled, CancellationToken.None));
                break;
            case "game-mode":
                _ = _commandDispatcher.RunAsync("游戏模式", m => m.SetGameModeAsync(enabled, CancellationToken.None));
                break;
            case "long-battery":
                _ = _commandDispatcher.RunAsync("长续航", m => m.SetLongBatteryAsync(enabled, CancellationToken.None));
                break;
            case "wear-detection":
                _ = _commandDispatcher.RunAsync("佩戴检测", m => m.SetWearDetectionAsync(enabled, CancellationToken.None));
                break;
            case "spine-health":
                _ = _commandDispatcher.RunAsync("脊柱健康", m => m.SetSpineHealthAsync(enabled, CancellationToken.None));
                break;
            case "spatial-sound":
                _ = _commandDispatcher.RunAsync("空间声场", m => m.SetSpatialSoundAsync(enabled, CancellationToken.None));
                break;
            case "game-sound":
                _ = RunGameSoundCommandAsync(enabled);
                break;
            case "bass-engine":
                _ = _commandDispatcher.RunAsync("低音引擎", m => m.SetBassEngineAsync(enabled, CancellationToken.None));
                break;
            case "voice-enhancement":
                _ = _commandDispatcher.RunAsync("人声增强", m => m.SetVoiceEnhancementAsync(enabled, CancellationToken.None));
                break;
            case "hearing-enhancement":
                _ = _commandDispatcher.RunAsync("听力增强", m => m.SetHearingEnhancementAsync(enabled, CancellationToken.None));
                break;
        }
    }

    private async Task RunGameSoundCommandAsync(bool enabled)
    {
        _gameSoundCommandPending = true;
        try
        {
            await (_commandDispatcher?.RunAsync("游戏音效", m => m.SetGameSoundEnabledAsync(enabled, CancellationToken.None))
                ?? Task.FromResult(false));
        }
        finally
        {
            _gameSoundCommandPending = false;
        }
    }

    public void RefreshAppearance()
    {
        if (_isClosed) return;
        RootGrid.RequestedTheme = _nextSettings?.Current.Theme switch
        {
            "Dark" => ElementTheme.Dark,
            "Light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
        var dark = RootGrid.RequestedTheme == ElementTheme.Dark
            || (RootGrid.RequestedTheme == ElementTheme.Default && App.CurrentWindow?.Content is FrameworkElement owner && owner.ActualTheme == ElementTheme.Dark) ? 1 : 0;
        var rounded = 2;
        var hwnd = WindowNative.GetWindowHandle(this);
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
    }
    private void LoadEarphoneImages()
    {
        LeftImage.Source = EarphoneImageProvider.GetBitmap(EarphoneSlot.HomeLeft);
        CaseImage.Source = EarphoneImageProvider.GetBitmap(EarphoneSlot.Case);
        RightImage.Source = EarphoneImageProvider.GetBitmap(EarphoneSlot.HomeRight);
    }

    public void RefreshEarphoneImages() => LoadEarphoneImages();

    private void CustomClose_Click(object sender, RoutedEventArgs e)
    {
        ApplicationLog.Current?.Debug("UI", "SmallWindow: 点击自定义关闭按钮");
        Close();
    }

    public void SafeRefresh()
    {
        if (_isClosed || _refreshPending)
            return;

        _refreshPending = true;
        AppDispatcher.Post(() =>
        {
            _refreshPending = false;
            if (_isClosed)
                return;

            if (_frontendState is not null)
                ApplyNextSnapshot(_frontendState.Snapshot);
        });
    }

    private void SyncNoiseOptions(BrandPresentation presentation)
    {
        var signature = string.Join("|", presentation.NoiseOptions.SelectMany(option => option.Children.Count > 0 ? option.Children : new[] { option })
            .Select(option => option.Key + ":" + DeviceProfileLoader.AncLabel(option.Key)));
        _syncingNoise = true;
        try
        {
            if (_noiseSignature != signature)
            {
                _noiseSignature = signature;
                NoiseModes.Items.Clear();
                foreach (var parent in presentation.NoiseOptions)
                    foreach (var option in parent.Children.Count > 0 ? parent.Children : new[] { parent })
                        NoiseModes.Items.Add(new ComboBoxItem
                        {
                            Content = parent.Children.Count > 0 ? DeviceProfileLoader.AncLabel(parent.Key) + " · " + DeviceProfileLoader.AncLabel(option.Key) : DeviceProfileLoader.AncLabel(option.Key),
                            Tag = option.Key
                        });
            }
            NoiseModes.SelectedItem = NoiseModes.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is string key && key == presentation.CurrentNoiseModeKey);
        }
        finally { _syncingNoise = false; }
    }

    private async void NoiseModes_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingNoise || _isClosed || NoiseModes.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        await (_commandDispatcher?.RunAsync("小窗降噪模式", manager => manager.SetNoiseCancellationByKeyAsync(key, CancellationToken.None)) ?? Task.FromResult(false));
        if (!_isClosed && _frontendState is not null) ApplyNextSnapshot(_frontendState.Snapshot);
    }

    internal void RefreshAncLabels()
    {
        _noiseSignature = string.Empty;
        SafeRefresh();
    }
}
