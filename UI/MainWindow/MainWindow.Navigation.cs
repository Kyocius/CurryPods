using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    private void ShowPage(string page)
    {
        if (_currentPage != page)
        {
            _logManager?.Debug("UI", $"页面切换: {_currentPage} -> {page}");
            _currentPage = page;
        }

        MainPanel.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "home")
            HomeView?.SetAncPanelVisible(true);

        EqPanel.Visibility = page == "eq" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "eq")
            _ = _commandDispatcher?.RunAsync("刷新自定义 EQ", manager => manager.RefreshCustomEqualizersAsync(CancellationToken.None));

        DeviceInfoPanel.Visibility = page == "deviceinfo" ? Visibility.Visible : Visibility.Collapsed;
        PersonalPanel.Visibility = page == "personal" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        LogPanel.Visibility = page == "log" ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;

        UpdateNavHighlight(NavHome, page == "home");
        UpdateNavHighlight(NavEq, page == "eq");
        UpdateNavHighlight(NavPersonal, page == "personal");
        UpdateNavHighlight(NavSettings, page == "settings");

        if (page != "log")
            LogView.Stop();
        if (page == "deviceinfo" || page == "settings") SettingsView?.RefreshDeviceInfo();
        if (page == "eq" && _frontendState is not null)
            EqView?.ApplySnapshot(_frontendState.Snapshot);
        if (page == "log") LogView.Start();
    }

    private static void UpdateNavHighlight(Microsoft.UI.Xaml.Controls.Button button, bool selected)
    {
        button.Background = selected ? AppPalette.BrushPrimary : AppPalette.BrushTransparent;
        button.Foreground = selected ? AppPalette.BrushWhitePure : AppPalette.BrushGray;
    }

    private void BtnViewLog_Click(object sender, RoutedEventArgs e)
    {
        _logManager?.Debug("UI", "日志面板: 打开");
        ShowPage("log");
    }

    private void NavHome_Click(object sender, RoutedEventArgs e) => ShowPage("home");
    private void NavEq_Click(object sender, RoutedEventArgs e) => ShowPage("eq");
    private void NavPersonal_Click(object sender, RoutedEventArgs e) => ShowPage("personal");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowPage("settings");
    private void About_Click(object sender, RoutedEventArgs e) => ShowPage("about");
}
