using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Assets.Oplus;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Communication;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Apple;
using OppoPodsManager.Control.Brands.Edifier;
using OppoPodsManager.Control.Brands.Huawei;
using OppoPodsManager.Control.Brands.Oppo;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Brands.Vivo;
using OppoPodsManager.Control.Brands.Xiaomi;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;
using OppoPodsManager.Control.Subsystems.Notifications;
using OppoPodsManager.UI.MainWindow;
using OppoPodsManager.UI.Toast;
using OppoPodsManager.UI.Tray;

namespace OppoPodsManager;

public sealed partial class App : Application
{
    public static Window? CurrentWindow { get; set; }
    public static bool IsMinimizedStartup() => false;
    private readonly FrontendState _frontendState = new();
    private ControlManager? _controlManager;
    private ModelCatalog? _modelCatalog;
    private ToastNotificationService? _toastNotifications;
    private TrayIconController? _trayIcon;
    private SettingsManager? _settings;
    private ApplicationLog? _log;
    private CommandDispatcher? _commandDispatcher;
    private NotificationCoordinator? _notificationCoordinator;
    private DesktopLinkService? _desktopLinks;
    private FeedbackExportService? _feedbackExporter;
    private CancellationTokenSource? _startupCancellation;
    private MainWindow? _mainWindow;
    private bool _exiting;
    private AppInstance? _instance;

    public App()
    {
        InitializeComponent();

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OppoPodsManager",
            "settings.json");
        _settings = new SettingsManager(settingsPath);
        LanguageManager.ApplyConfiguredCulture(_settings.Current.Language);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppDispatcher.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        _instance = AppInstance.FindOrRegisterForKey("CurryPods");
        if (!_instance.IsCurrent)
        {
            await _instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        _instance.Activated += OnInstanceActivated;
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OppoPodsManager",
            "settings.json");
        _settings ??= new SettingsManager(settingsPath);
        _log = new ApplicationLog(Path.Combine(Path.GetDirectoryName(settingsPath)!, "Logs"));
        _log.Info("App", "开始初始化桌面生命周期和设备控制器。");
        InstallGlobalExceptionHandlers();
        WindowsStartup.TrySetEnabled(_settings.Current.StartWithWindows);

        _desktopLinks = new DesktopLinkService(_log);
        _feedbackExporter = new FeedbackExportService(_log);
        var communication = CommunicationBootstrap.CreateDefault();
        _modelCatalog = DeviceModelData.LoadCatalog();
        var settingsStore = new SettingsStore(_settings);

        _controlManager = new ControlManager(
            _frontendState,
            new DeviceScanner(communication),
            [new OppoManagerFactory(_modelCatalog), new VivoManagerFactory(), new EdifierManagerFactory(), new HuaweiManagerFactory(), new XiaomiManagerFactory(), new AppleManagerFactory()],
            settingsStore);
        _controlManager.StartMonitoring();

        _commandDispatcher = new CommandDispatcher(_controlManager, _log);
        _notificationCoordinator = new NotificationCoordinator(_frontendState);
        _toastNotifications = new ToastNotificationService(_notificationCoordinator, _settings);
        _startupCancellation = new CancellationTokenSource();

        _mainWindow = CreateMainWindow();
        _mainWindow.Activate();
        _ = ConnectInitialDeviceAsync(_startupCancellation.Token);
        _log.Info("App", "主窗口创建并激活完成。");

        AppDispatcher.Post(() =>
        {
            if (_exiting)
                return;
            try
            {
                _trayIcon = new TrayIconController(
                    _frontendState,
                    _controlManager,
                    _commandDispatcher,
                    _settings,
                    _log,
                    GetOrCreateMainWindow,
                    RequestApplicationExit);
                _log.Info("App", "托盘控制器初始化完成。");
            }
            catch (Exception ex)
            {
                _log.Error("App", "托盘控制器初始化失败", ex);
            }
        });
    }

    private void OnInstanceActivated(object? sender, AppActivationArguments args)
        => AppDispatcher.Post(() =>
        {
            if (GetOrCreateMainWindow() is not { } window)
                return;
            window.AppWindow.Show();
            if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
                && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                presenter.Restore();
            window.Activate();
        });

    private void InstallGlobalExceptionHandlers()
    {
        UnhandledException += (_, e) =>
        {
            _log?.Error("App", "UI 线程未处理异常（已拦截，应用继续运行）。", e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            _log?.Error("App", "未处理的终止性异常。", e.ExceptionObject as Exception);
            _log?.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log?.Error("App", "未观察的 Task 异常（已拦截）。", e.Exception);
            e.SetObserved();
        };

        _log?.Debug("App", "全局异常兜底已安装。");
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(
            _frontendState,
            _controlManager ?? throw new InvalidOperationException("控制器尚未初始化。"),
            _modelCatalog ?? throw new InvalidOperationException("型号目录尚未初始化。"),
            _settings ?? throw new InvalidOperationException("设置管理器尚未初始化。"),
            _log ?? throw new InvalidOperationException("日志服务尚未初始化。"),
            _commandDispatcher ?? throw new InvalidOperationException("命令调度器尚未初始化。"),
            _desktopLinks,
            _feedbackExporter,
            RequestApplicationExit,
            ShouldKeepMainWindowAlive);

        window.Closed += OnMainWindowClosed;
        CurrentWindow = window;
        return window;
    }

    private void OnMainWindowClosed(object? sender, WindowEventArgs e)
    {
        if (sender is Window window)
            window.Closed -= OnMainWindowClosed;
        if (ReferenceEquals(CurrentWindow, sender))
            CurrentWindow = null;
        if (!ReferenceEquals(sender, _mainWindow))
            return;

        _mainWindow = null;
        _log?.Info("App", "主窗口已关闭，窗口资源已从托盘生命周期中释放。");
    }

    private Window? GetOrCreateMainWindow()
    {
        if (_exiting)
            return null;

        _mainWindow ??= CreateMainWindow();
        return _mainWindow;
    }

    private async void RequestApplicationExit()
    {
        if (_exiting)
            return;
        _exiting = true;
        if (_instance is not null)
            _instance.Activated -= OnInstanceActivated;
        _startupCancellation?.Cancel();
        _toastNotifications?.Dispose();
        _toastNotifications = null;
        _notificationCoordinator?.Dispose();
        _notificationCoordinator = null;
        _trayIcon?.Dispose();
        _trayIcon = null;

        try
        {
            if (_controlManager is not null)
                await _controlManager.DisposeAsync();
        }
        catch (Exception exception)
        {
            _log?.Error("App", "退出时释放设备控制器失败。", exception);
        }
        finally
        {
            _controlManager = null;
            _startupCancellation?.Dispose();
            _startupCancellation = null;
            ToastManager.CloseAll();
            _mainWindow?.CloseForExit();
            _mainWindow = null;
            CurrentWindow = null;
            _desktopLinks = null;
            _feedbackExporter = null;
            _commandDispatcher = null;
            _log?.Dispose();
            _log = null;
            Exit();
        }
    }

    private bool ShouldKeepMainWindowAlive()
        => _settings?.Current.MinimizeToTray == true;

    private async Task ConnectInitialDeviceAsync(CancellationToken cancellationToken)
    {
        if (_controlManager is null || _controlManager.ActiveManager is not null)
            return;

        try
        {
            if (await _controlManager.ConnectFirstAvailableAsync(cancellationToken))
                _log?.Info("Startup", "自动连接和设备初始读取已完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _log?.Error("Startup", "自动连接耳机失败。", exception);
        }
    }
}
