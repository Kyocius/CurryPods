using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics;
using NextSettingsManager = OppoPodsManager.Assets.UserSettings.SettingsManager;

namespace OppoPodsManager.UI.Toast;

public enum ToastType { Battery, LowBattery, CriticalBattery, Disconnected }

public partial class ToastWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _closed;

    public ToastWindow()
    {
        InitializeComponent();
        _appWindow = AppWindow;
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsAlwaysOnTop = true;
        }
        if (App.CurrentWindow?.Content is FrameworkElement ownerRoot)
            RootLayout.RequestedTheme = ownerRoot.ActualTheme;
        var hwnd = WindowNative.GetWindowHandle(this);
        var dark = RootLayout.RequestedTheme == ElementTheme.Dark ? 1 : 0;
        var rounded = 2; // DWMWCP_ROUND: let DWM clip and shadow the whole window.
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
        RootLayout.Loaded += RootLayout_Loaded;
        Closed += (_, _) =>
        {
            _closed = true;
            RootLayout.Loaded -= RootLayout_Loaded;
            _lifetime.Cancel();
            ToastManager.Unregister(this);
            Content = null;
        };
    }

    private void RootLayout_Loaded(object sender, RoutedEventArgs args) => Reposition();

    public static Task ShowSnapshotAsync(BusinessSnapshot? snapshot, string deviceName,
        ToastType type = ToastType.Battery, int durationMs = 5000, NextSettingsManager? settings = null,
        CancellationToken cancellationToken = default)
        => AppDispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ShowSnapshotCoreAsync(snapshot, deviceName, type, durationMs, cancellationToken);
        });

    private static async Task ShowSnapshotCoreAsync(BusinessSnapshot? snapshot, string deviceName,
        ToastType type, int durationMs, CancellationToken cancellationToken)
    {
        var toast = new ToastWindow();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, toast._lifetime.Token);
        try
        {
            toast.TitleBlock.Text = deviceName;
            toast.StatusText.Text = TranslationCatalog.Get(type == ToastType.Disconnected ? "Toast_Disconnected" : "Toast_Connected");
            toast.BatteryPanel.Visibility = type == ToastType.Disconnected ? Visibility.Collapsed : Visibility.Visible;
            SetBattery(toast.LeftPct, toast.LeftBolt, snapshot?.LeftBattery);
            SetBattery(toast.RightPct, toast.RightBolt, snapshot?.RightBattery);
            SetBattery(toast.CasePct, toast.CaseBolt, snapshot?.CaseBattery);
            if (type is ToastType.LowBattery or ToastType.CriticalBattery)
            {
                var critical = type == ToastType.CriticalBattery;
                toast.BatteryWarning.Title = TranslationCatalog.Get(critical ? "Toast_CriticalBattery" : "Toast_LowBattery");
                toast.BatteryWarning.Message = TranslationCatalog.Get(critical ? "Toast_CriticalBatteryHint" : "Toast_LowBatteryHint");
                toast.BatteryWarning.Severity = critical ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
                toast.BatteryWarning.IsOpen = true;
            }
            toast.Reposition();
            ToastManager.Register(toast);
            // A connection notification must not take keyboard focus from the current app.
            toast._appWindow.Show(false);
            await Task.Delay(Math.Max(1, durationMs), lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (!toast._closed)
                toast.Close();
            toast._lifetime.Dispose();
        }
    }

    private static void SetBattery(TextBlock percentage, FrameworkElement bolt, BatteryLevel? battery)
    {
        percentage.Text = battery is { } value ? $"{value.Percent}%" : "– %";
        bolt.Visibility = battery?.IsCharging == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Reposition()
    {
        var owner = App.CurrentWindow ?? this;
        var ownerHwnd = WindowNative.GetWindowHandle(owner);
        var display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(ownerHwnd), DisplayAreaFallback.Primary);
        if (display is null)
            return;
        // AppWindow uses physical pixels; XAML measures in DIPs. Use the target monitor's scale.
        var scale = Math.Max(1, GetDpiForWindow(ownerHwnd)) / 96d;
        var area = display.WorkArea;
        var gap = (int)Math.Round(16 * scale);
        var width = Math.Min((int)Math.Round(360 * scale), Math.Max(1, area.Width - 2 * gap));
        RootLayout.Width = width / scale;
        RootLayout.Measure(new Windows.Foundation.Size(RootLayout.Width, double.PositiveInfinity));
        var height = Math.Min((int)Math.Ceiling(RootLayout.DesiredSize.Height * scale), Math.Max(1, area.Height - 2 * gap));
        // Account for the actual non-client frame after hiding the title bar.
        // ResizeClient can include caption metrics even for this presenter configuration.
        var hwnd = WindowNative.GetWindowHandle(this);
        GetClientRect(hwnd, out var client);
        var frame = _appWindow.Size;
        _appWindow.Resize(new SizeInt32(width + frame.Width - client.Right, height + frame.Height - client.Bottom));
        var size = _appWindow.Size;
        var bounds = new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + area.Height - size.Height - gap, size.Width, size.Height);
        _appWindow.Move(new PointInt32(bounds.X, bounds.Y));
        ApplicationLog.Current?.Debug("Toast", $"通知布局：bounds={bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}，scale={scale}，workArea={area.X},{area.Y},{area.Width},{area.Height}。");
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }
}
