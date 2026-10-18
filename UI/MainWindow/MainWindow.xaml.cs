using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Windows.Graphics;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Assets.VisualAssets;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;
using OppoPodsManager.UI.Views;

namespace OppoPodsManager.UI.MainWindow;

internal static class AppConst
{
    public const string WindowTitle = "CurryPods";
    public const string IconConnected = "ms-appx:///Assets/tuopan.ico";
    public const string IconDisconnected = "ms-appx:///Assets/tuopandis.png";
}

public partial class MainWindow : Window, IViewHost
{
    private FrontendState? _frontendState;
    private ControlManager? _controlManager;
    private CommandDispatcher? _commandDispatcher;
    private readonly SettingsStore _uiSettings;
    private IDisposable? _interactiveSurface;

    private ApplicationLog? _logManager;
    private DispatcherTimer? _bgApplyDebounceTimer;
    private ImageSource? _backgroundImageSource;
    private bool _realClose;
    private bool _runtimeUiDisposed;
    private string _currentPage = "";
    private readonly BackgroundImageManager _backgroundImages = new();
    private readonly BackgroundSelectionService _backgroundSelection;
    private string? _customDeviceName;

    // 缓存画刷
    private static SolidColorBrush BrushGreen => AppPalette.BrushGreen;
    private static SolidColorBrush BrushRed => AppPalette.BrushRed;
    private SolidColorBrush BrushGray => _isLightTheme ? _brushGrayLight : _brushGrayDark;
    private SolidColorBrush BrushWhite => _isLightTheme ? _brushDark : _brushWhiteDark;
    private static readonly SolidColorBrush _brushGrayDark = new(ColorHelper.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush _brushGrayLight = new(ColorHelper.FromArgb(0xFF, 0x55, 0x55, 0x55));
    private static readonly SolidColorBrush _brushWhiteDark = new(Colors.White);
    private static readonly SolidColorBrush _brushDark = new(ColorHelper.FromArgb(0xFF, 0x1A, 0x1A, 0x1A));
    private readonly SolidColorBrush _textPanelButtonBgBrush = new(ColorHelper.FromArgb(0x0A, 0x00, 0x00, 0x00));
    private readonly SolidColorBrush _deviceCurrentBgBrush = new(ColorHelper.FromArgb(0x12, 0x4C, 0xAF, 0x50));

    private readonly SolidColorBrush _brushLightGreenLight = new(ColorHelper.FromArgb(0xFF, 0x2E, 0x7D, 0x32));
    private readonly SolidColorBrush _brushLightGreenDark = new(ColorHelper.FromArgb(0xFF, 0x88, 0xCC, 0x88));
    private readonly SolidColorBrush _brushLightRedLight = new(ColorHelper.FromArgb(0xFF, 0xC6, 0x28, 0x28));
    private readonly SolidColorBrush _brushLightRedDark = new(ColorHelper.FromArgb(0xFF, 0xFF, 0x88, 0x88));

    private SolidColorBrush BrushLightGreen => _isLightTheme ? _brushLightGreenLight : _brushLightGreenDark;
    private SolidColorBrush BrushLightRed => _isLightTheme ? _brushLightRedLight : _brushLightRedDark;

    private bool _isLightTheme;
    private int _currentThemeIndex;
    private readonly List<IDisposable> _linguaSubs = new();
    private readonly DesktopLinkService? _desktopLinks;
    private readonly FeedbackExportService? _feedbackExporter;
    private readonly Action? _requestApplicationExit;
    private readonly Func<bool>? _shouldKeepWindowAlive;
    private AppWindow? _appWindow;

    public MainWindow() : this(null)
    {
    }

    private MainWindow(
        OppoPodsManager.Assets.UserSettings.SettingsManager? nextSettings,
        ModelCatalog? modelCatalog = null,
        CommandDispatcher? commandDispatcher = null,
        DesktopLinkService? desktopLinks = null,
        FeedbackExportService? feedbackExporter = null,
        Action? requestApplicationExit = null,
        Func<bool>? shouldKeepWindowAlive = null)
    {
        _commandDispatcher = commandDispatcher;
        _desktopLinks = desktopLinks;
        _feedbackExporter = feedbackExporter;
        _requestApplicationExit = requestApplicationExit;
        _shouldKeepWindowAlive = shouldKeepWindowAlive;
        _uiSettings = new SettingsStore(nextSettings);
        _backgroundSelection = new BackgroundSelectionService(_uiSettings, _backgroundImages);
        _logManager = ApplicationLog.Current;

        try
        {
            _logManager?.Debug("UI", "MainWindow 构造开始");

            var asmVer0 = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AppInfo.VersionLabel = asmVer0 != null
                ? $"v{asmVer0.Major}.{asmVer0.Minor}.{asmVer0.Build}"
                : "v?";

            InitializeComponent();
            Navigation_DisplayModeChanged(AppNavigation, EventArgs.Empty);
            AppDispatcher.Initialize(this.DispatcherQueue);
            SetupAppWindow();
            AdaptToPlatform();

            _logManager?.Debug("UI", "InitializeComponent OK");

            var themeIndex = NextThemeIndex();
            ApplyTheme(themeIndex);

            RefreshEarphoneImages();
            BuildEarphoneCustomUi();

            _customDeviceName = _uiSettings.GetString("CustomName");
            UpdateTitle();

            Closed += OnWindowClosed;
            ShowPage("home");
        }
        catch (Exception ex)
        {
            _logManager?.Error("UI", "MainWindow 构造", ex);
            throw;
        }
    }

    public MainWindow(
        FrontendState frontendState,
        ControlManager controlManager,
        ModelCatalog modelCatalog,
        OppoPodsManager.Assets.UserSettings.SettingsManager settings,
        ApplicationLog log,
        CommandDispatcher commandDispatcher,
        DesktopLinkService? desktopLinks = null,
        FeedbackExportService? feedbackExporter = null,
        Action? requestApplicationExit = null,
        Func<bool>? shouldKeepWindowAlive = null)
        : this(settings, modelCatalog, commandDispatcher, desktopLinks, feedbackExporter, requestApplicationExit, shouldKeepWindowAlive)
    {
        _frontendState = frontendState;
        _controlManager = controlManager;
        _controlManager.AvailableDevicesChanged += OnAvailableDevicesChanged;
        _logManager = log;
        _frontendState.Changed += OnNextStateChanged;
        _logManager.Info("UI", "主窗口已接入 Next 控制层，禁用原项目连接循环。");
        AttachViews();
        ApplyNextSnapshot(_frontendState.Snapshot);
        _ = HomeView.RefreshNextDevicesAsync();
    }

    private void SetupAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "VisualAssets", "tuopan.ico"));
        _appWindow.Resize(new SizeInt32(1100, 740));

        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centeredPosition = _appWindow.Position;
            centeredPosition.X = displayArea.WorkArea.X + (displayArea.WorkArea.Width - 1100) / 2;
            centeredPosition.Y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - 740) / 2;
            _appWindow.Move(centeredPosition);
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;
        _logManager?.Info("UI", $"SetupAppWindow 完成: HWND=0x{hwnd:X}, Title='{Title}', IsVisible={_appWindow.IsVisible}");
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange)
        {
            if (sender.IsVisible)
            {
                _interactiveSurface ??= _frontendState?.AcquireInteractiveSurface();
                if (_currentPage == "log")
                    LogView?.Start();
            }
            else
            {
                _interactiveSurface?.Dispose();
                _interactiveSurface = null;
                LogView?.Stop();
            }
        }
    }

    private void OnAvailableDevicesChanged(object? sender, DeviceOptionsChangedEventArgs args)
    {
        if (_realClose)
            return;

        AppDispatcher.Post(() => HomeView?.ApplyNextDevices(args.Devices));
    }

    private void OnNextStateChanged(object? sender, BusinessSnapshot snapshot)
    {
        if (_realClose)
            return;

        AppDispatcher.Post(() => ApplyNextSnapshot(snapshot));
    }

    private void ApplyNextSnapshot(BusinessSnapshot snapshot)
    {
        if (_realClose)
            return;

        UpdateTitle();
        HomeView?.ApplySnapshot(snapshot);
        SettingsView?.ApplySnapshot(snapshot);
        DeviceInfoView?.ApplySnapshot(snapshot);
        EqView?.ApplySnapshot(snapshot);
        SyncNextMultiDeviceList(snapshot);
        _logManager?.Debug("UI", $"快照已应用：revision={snapshot.Revision}，connected={snapshot.IsConnected}，device={snapshot.DeviceName ?? ""}。");
    }


    // ---- 耳机预览 ----
    private readonly Dictionary<EarphoneSlot, Image> _earphonePreviews = new();

    // ---- 多设备列表行引用 ----
    private readonly Dictionary<string, DeviceListRowRefs> _deviceListRows = new();

    private sealed class DeviceListRowRefs
    {
        public required Border Root { get; init; }
        public required Microsoft.UI.Xaml.Shapes.Ellipse Dot { get; init; }
        public required TextBlock NameText { get; init; }
        public required TextBlock AudioText { get; init; }
        public required TextBlock StatusText { get; init; }
    }

    private void AttachViews()
    {
        AboutView.Host = this;
        AboutView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        LogView.Host = this;
        LogView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        EqView.Host = this;
        EqView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        PersonalView.Host = this;
        PersonalView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        SettingsView.Host = this;
        SettingsView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        DeviceInfoView.Host = this;
        DeviceInfoView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
        HomeView.Host = this;
        HomeView.Attach(_controlManager, _uiSettings, _logManager, _commandDispatcher, _frontendState, _desktopLinks);
    }

    void IViewHost.RequestNavigate(string page) => ShowPage(page);
    Task IViewHost.PreviewToastAsync()
    {
        var snapshot = _frontendState?.Snapshot;
        return ToastWindow.ShowSnapshotAsync(snapshot,
            DeviceText.DeviceName(snapshot?.Identity?.ModelName, snapshot?.DeviceName),
            durationMs: Math.Clamp(_uiSettings.GetInt("ToastDuration", 5), 3, 8) * 1000);
    }
    void IViewHost.OpenUrl(string url) => _desktopLinks?.TryOpen(url, "页面链接");
    Task IViewHost.ShowCheckResultDialogAsync(string message, string? title)
        => ShowCheckResultDialog(message, title);
    Task<string?> IViewHost.ShowPromptDialogAsync(string title, string defaultText, string hint)
        => ShowPromptDialog(title, defaultText, hint);
    Task<bool> IViewHost.ShowConfirmDialogAsync(string title, string message)
        => ShowConfirmDialog(title, message);

    void IViewHost.ApplyTheme(int index)
    {
        ApplyTheme(index);
        _uiSettings.SetString("Theme", NextThemeName(index));
    }
    void IViewHost.ApplyLanguage(LanguageOption option) => ApplyLanguage(option);
    void IViewHost.RefreshCardOpacity() => RefreshCardOpacity();
    void IViewHost.SelectBackground(string key) => SelectBackground(key);
    void IViewHost.AddBackgroundImage() => _ = BtnBgAdd_Click();
    void IViewHost.ApplyBackgroundBlur()
    {
        var timer = EnsureBackgroundApplyDebounceTimer();
        timer.Stop();
        timer.Start();
    }
    void IViewHost.SetAcrylicBlur(bool on) => ToggleAcrylicBlur(on);
    void IViewHost.SetAcrylicBlurSilent(bool on) => ApplyAcrylicBlurSilently(on);
    void IViewHost.SetAdvancedRender(bool on)
    {
        if (on) EnableAdvancedRender();
        else DisableAdvancedRender();
    }
    void IViewHost.SetCustomDeviceName(string? name)
    {
        _customDeviceName = name;
        UpdateTitle();
    }
    void IViewHost.RebuildEarphoneUi() => BuildEarphoneCustomUi();
    void IViewHost.RefreshBackground()
    {
        RefreshBgThumbs();
        ApplySavedBackground();
    }

    Task IViewHost.OpenUpdatePageAsync() => OpenUpdatePageAsync();
    Task IViewHost.OpenFeedbackAsync() => ShowFeedbackDialogAsync();
    void IViewHost.ResyncMultiDeviceList() => SyncNextMultiDeviceList(_frontendState?.Snapshot);
    void IViewHost.SetEqControlsEnabled(bool enabled) => EqView?.SetControlsEnabled(enabled);
    void IViewHost.ApplyEarphoneImage(Image target, EarphoneSlot slot) => ReplaceEarphoneImage(target, slot);
    Task<bool> IViewHost.ShowFindWarningDialogAsync() => ShowFindWarningDialog();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        DisposeRuntimeUiResources();
    }
}
