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
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
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

    private static readonly SolidColorBrush BatteryColorWhite  = new(Colors.White);
    private static readonly SolidColorBrush BatteryColorYellow = new(ColorHelper.FromArgb(0xFF, 0xFF, 0xCC, 0x00));
    private static readonly SolidColorBrush BatteryColorRed    = new(ColorHelper.FromArgb(0xFF, 0xFF, 0x3B, 0x30));

    private readonly Dictionary<string, (Border bg, Path icon, TextBlock label)> _ancMainButtons = new();
    private readonly Dictionary<string, (Button btn, Border bg)> _ancSubButtons = new();
    private readonly Dictionary<string, string> _ancChildToMain = new();

    private List<NoiseOptionModel> _ancOptions = new();
    private string _ancMain = "", _ancLevel = "";
    private string? _ancBuiltForModel;
    private string _ancSubSignature = "";
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
            BgImage.Source = null;
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
        WireCheckBox(CbDualDevice);
        WireCheckBox(CbGame);
        WireCheckBox(CbLongPower);
        WireCheckBox(CbWearDetection);
        WireCheckBox(CbSpineHealth);
        WireCheckBox(CbSpatial);
        WireCheckBox(CbGameSound);
        WireCheckBox(CbBassEngine);
        WireCheckBox(CbVocalEnhance);
        WireCheckBox(CbHearingEnhance);

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
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.Resize(new SizeInt32(320, 440));

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var wa = displayArea.WorkArea;
            _appWindow.Move(new PointInt32(wa.X + wa.Width - 330, wa.Y + wa.Height - 450));
        }
    }

    private void WireCheckBox(CheckBox cb)
    {
        cb.Checked += FeatureCheck_Changed;
        cb.Unchecked += FeatureCheck_Changed;
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
        var manager = _controlManager?.ActiveManager ?? _nextManager;
        if (manager is not null)
        {
            var presentation = manager.Presentation;
            AncCard.Visibility = (snapshot.IsConnected && presentation.SupportsNoiseCancellation) ? Visibility.Visible : Visibility.Collapsed;
            if (AncCard.Visibility == Visibility.Visible)
            {
                BuildNextAncUi(presentation);
                if (snapshot.Noise.Mode != NoiseMode.Unknown)
                    SyncAncFromState(presentation.CurrentNoiseModeKey);
                HighlightAnc();
            }
        }
        else
        {
            AncCard.Visibility = Visibility.Collapsed;
        }
        ApplyNextFeatureState(snapshot);
        ApplicationLog.Current?.Debug("UI", $"小窗已应用快照：revision={snapshot.Revision}，connected={snapshot.IsConnected}。");
    }

    private static void SetNextBattery(TextBlock label, FrameworkElement bolt, BatteryLevel? battery)
    {
        if (battery is { } value)
        {
            label.Text = $"{value.Percent}%";
            label.Foreground = value.Percent < 10 ? BatteryColorRed
                              : value.Percent <= 20 ? BatteryColorYellow
                              : BatteryColorWhite;
        }
        else
        {
            label.Text = "–%";
            label.Foreground = BatteryColorWhite;
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

    private void SetNextFeatureCheck(CheckBox box, bool visible, IReadOnlyDictionary<string, bool> states, string key)
    {
        box.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!states.TryGetValue(key, out var enabled) || box.IsChecked == enabled)
            return;
        _syncingFeatures = true;
        try
        {
            box.IsChecked = enabled;
        }
        finally
        {
            _syncingFeatures = false;
        }
    }

    private static void SetFeatureEnabled(CheckBox box, IReadOnlyDictionary<string, bool> states, string key)
    {
        var enabled = !states.TryGetValue(key, out var value) || value;
        if (box.IsEnabled != enabled)
            box.IsEnabled = enabled;
    }

    private void FeatureCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingFeatures)
            return;
        if (sender is not CheckBox box || box.Tag is not string key || box.IsChecked is not { } enabled)
            return;
        SendFeatureCommand(key, enabled);
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
        if (_isClosed)
            return;
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
        _appWindow?.Hide();
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

    private static Geometry ParseGeometry(string pathData)
    {
        return ((Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{pathData}\"/>")).Data;
    }

    private void BuildNextAncUi(BrandPresentation presentation)
    {
        var modelKey = presentation.ModelName + "|"
            + string.Join("|", presentation.NoiseOptions.Select(option =>
                $"{option.Key}:{string.Join(',', option.Children.Select(child => child.Key))}"));
        if (_ancBuiltForModel == modelKey)
            return;

        BuildAncUi(modelKey, presentation.NoiseOptions);
    }

    private void BuildAncUi(string modelKey, IReadOnlyList<NoiseOptionModel> options)
    {
        if (_ancBuiltForModel == modelKey) return;
        _ancBuiltForModel = modelKey;
        _ancOptions = options.ToList();

        _ancMainButtons.Clear();
        _ancChildToMain.Clear();
        AncMainRow.Children.Clear();
        AncMainRow.ColumnDefinitions.Clear();
        AncSubRow.Children.Clear();
        AncSubRow.ColumnDefinitions.Clear();
        _ancSubButtons.Clear();
        _ancSubSignature = "";
        AncSubRow.Visibility = Visibility.Collapsed;

        var segmentGrid = new OppoPodsManager.UI.Controls.UniformGrid { Columns = Math.Max(1, _ancOptions.Count) };
        var capsule = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = AppPalette.BrushCircleGray,
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = segmentGrid
        };

        for (int i = 0; i < _ancOptions.Count; i++)
        {
            var opt = _ancOptions[i];
            var corner = _ancOptions.Count == 1
                ? new CornerRadius(7)
                : i == 0 ? new CornerRadius(7, 0, 0, 7)
                : i == _ancOptions.Count - 1 ? new CornerRadius(0, 7, 7, 0)
                : new CornerRadius(0);
            var (segment, icon, label) = MakeAncSegmentButton(opt, corner, 30, 15, 13);
            segmentGrid.Children.Add(segment);
            _ancMainButtons[opt.Key] = (segment, icon, label);
            foreach (var child in opt.Children) _ancChildToMain[child.Key] = opt.Key;
        }

        AncMainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AncMainRow.Children.Add(capsule);
    }

    private void PopulateAncSub(NoiseOptionModel container)
    {
        var signature = container.Key + ":" + string.Join("|", container.Children.Select(c => $"{c.Key};{DeviceProfileLoader.AncLabel(c.Key)}"));
        if (_ancSubSignature == signature)
            return;

        foreach (var (_, (btn, _)) in _ancSubButtons)
            btn.Click -= AncSub_Click;

        AncSubRow.Children.Clear();
        AncSubRow.ColumnDefinitions.Clear();
        _ancSubButtons.Clear();
        _ancSubSignature = signature;

        int col = 0;
        for (int i = 0; i < container.Children.Count; i++)
        {
            var child = container.Children[i];
            if (i > 0)
            {
                AncSubRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var sep = new Border { Width = 1, Background = AppPalette.BrushGray, Opacity = 0.12 };
                Grid.SetColumn(sep, col);
                AncSubRow.Children.Add(sep);
                col++;
            }
            var corner = container.Children.Count == 1 ? new CornerRadius(5)
                : i == 0 ? new CornerRadius(5, 0, 0, 5)
                : i == container.Children.Count - 1 ? new CornerRadius(0, 5, 5, 0)
                : new CornerRadius(0);
            var btn = new Button
            {
                Content = DeviceProfileLoader.AncLabel(child.Key), Tag = child, MinWidth = 60, Height = 26,
                BorderThickness = new Thickness(0), Padding = new Thickness(8, 0, 8, 0),
                Background = AppPalette.BrushTransparent,
                Foreground = AppPalette.BrushGray, FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            btn.Click += AncSub_Click;
            var bg = new Border { CornerRadius = corner, Padding = new Thickness(0), Background = AppPalette.BrushTransparent, Child = btn };
            AncSubRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(bg, col);
            AncSubRow.Children.Add(bg);
            _ancSubButtons[child.Key] = (btn, bg);
            col++;
        }
    }

    internal void RefreshAncLabels()
    {
        foreach (var (key, (_, _, label)) in _ancMainButtons)
        {
            var t = DeviceProfileLoader.AncLabel(key);
            label.Text = t;
            label.FontSize = t.Length > 8 ? 11 : 13;
        }
        foreach (var (key, (btn, _)) in _ancSubButtons)
            btn.Content = DeviceProfileLoader.AncLabel(key);
    }

    private (Border bg, Path icon, TextBlock label) MakeAncSegmentButton(
        NoiseOptionModel opt, CornerRadius corner, int height, int iconSize, int fontSize)
    {
        var icon = new Path
        {
            Data = ParseGeometry(AncIcons.GetAncIcon(opt.Key)),
            Width = iconSize, Height = iconSize, Fill = AppPalette.BrushGray,
            Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center
        };
        var labelText = DeviceProfileLoader.AncLabel(opt.Key);
        var label = new TextBlock
        {
            Text = labelText,
            FontSize = labelText.Length > 8 ? Math.Max(10, fontSize - 2) : fontSize,
            Foreground = AppPalette.BrushGray,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(icon);
        content.Children.Add(label);

        var btn = new Button
        {
            Content = content, Tag = opt,
            Background = AppPalette.BrushTransparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 12, 0), Height = height,
            Foreground = AppPalette.BrushGray,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        btn.Click += (_, _) =>
        {
            if (btn.Tag is NoiseOptionModel o) SwitchAncMain(o);
        };

        var bg = new Border { CornerRadius = corner, Background = AppPalette.BrushTransparent, Child = btn };
        return (bg, icon, label);
    }

    private void AncSub_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is NoiseOptionModel opt) SwitchAncSub(opt);
    }

    private void HighlightAnc()
    {
        var circleGray = AppPalette.CircleGray;
        var accent = AppPalette.Accent;
        foreach (var (key, (bg, icon, label)) in _ancMainButtons)
        {
            var active = key == _ancMain;
            bg.Background = active ? accent : AppPalette.BrushTransparent;
            icon.Fill = active ? AppPalette.BrushWhitePure : AppPalette.BrushGray;
            label.Foreground = active ? AppPalette.BrushWhitePure : AppPalette.BrushGray;
        }
        foreach (var (key, (btn, bg)) in _ancSubButtons)
        {
            var active = key == _ancLevel;
            bg.Background = active ? accent : circleGray;
            btn.Foreground = active ? AppPalette.BrushWhitePure : AppPalette.BrushGray;
        }
    }

    private void SwitchAncMain(NoiseOptionModel opt)
    {
        if (_nextManager is null && _controlManager?.ActiveManager is null) return;
        _ancMain = opt.Key;
        if (opt.Children.Count > 0)
        {
            PopulateAncSub(opt);
            AncSubRow.Visibility = Visibility.Visible;
            var target = opt.Children[0].Key;
            _ancLevel = target;
            _ = _commandDispatcher?.RunAsync("小窗 ANC 主模式", m => m.SetNoiseCancellationByKeyAsync(target, CancellationToken.None));
        }
        else
        {
            AncSubRow.Visibility = Visibility.Collapsed;
            _ancLevel = "";
            _ = _commandDispatcher?.RunAsync("小窗 ANC 主模式", m => m.SetNoiseCancellationByKeyAsync(opt.Key, CancellationToken.None));
        }
        HighlightAnc();
    }

    private void SwitchAncSub(NoiseOptionModel opt)
    {
        if (_nextManager is null && _controlManager?.ActiveManager is null) return;
        _ancLevel = opt.Key;
        _ = _commandDispatcher?.RunAsync("小窗 ANC 子模式", m => m.SetNoiseCancellationByKeyAsync(opt.Key, CancellationToken.None));
        HighlightAnc();
    }

    private void SyncAncFromState(string modeKey)
    {
        var mainOpt = _ancOptions.FirstOrDefault(o => o.Key == modeKey && o.Children.Count == 0);
        if (mainOpt != null)
        {
            _ancMain = modeKey;
            _ancLevel = "";
            AncSubRow.Visibility = Visibility.Collapsed;
            return;
        }

        if (_ancChildToMain.TryGetValue(modeKey, out var parentKey))
        {
            var container = _ancOptions.FirstOrDefault(o => o.Key == parentKey);
            if (container != null)
            {
                _ancMain = parentKey;
                _ancLevel = modeKey;
                PopulateAncSub(container);
                AncSubRow.Visibility = Visibility.Visible;
            }
        }
    }
}
