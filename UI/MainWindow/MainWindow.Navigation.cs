using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.UI.Views;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    // Cache page controls so changing tabs preserves edits and scroll positions.
    // Only the selected page is attached to the visual tree.
    private readonly HomeView HomeView = new();
    private readonly EqView EqView = new();
    private readonly PersonalView PersonalView = new();
    private readonly SettingsView SettingsView = new();
    private readonly DeviceInfoView DeviceInfoView = new();
    private readonly LogView LogView = new();
    private readonly AboutView AboutView = new();
    private readonly Dictionary<string, FrameworkElement> _pageContainers = new();
    private readonly Dictionary<string, double> _pageScrollOffsets = new();
    private bool _updatingNavigation;

    private void Navigation_DisplayModeChanged(NavigationView sender, object args)
    {
        if (PageLayout is not null)
            PageLayout.Margin = sender.DisplayMode == NavigationViewDisplayMode.Minimal
                ? new Thickness(12, 56, 12, 12) : new Thickness(24);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_updatingNavigation && args.SelectedItemContainer?.Tag is string page)
            ShowPage(page);
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // A selected settings item must still return from its log subpage.
        if (args.InvokedItemContainer?.Tag is string page)
            ShowPage(page);
    }

    private void Navigation_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => ShowPage("settings");

    private void ShowPage(string page)
    {
        if (_realClose || PageContent is null)
            return;
        var item = page switch
        {
            "home" => NavHome,
            "eq" => NavEq,
            "personal" => NavPersonal,
            _ => NavSettings
        };
        // SelectionChanged also runs for programmatic navigation from page buttons.
        // Set the page before updating selection to avoid re-entering navigation.
        var changed = _currentPage != page;
        if (changed)
            _pageScrollOffsets[_currentPage] = PageScrollHost.VerticalOffset;
        _currentPage = page;
        if (!ReferenceEquals(AppNavigation.SelectedItem, item))
        {
            _updatingNavigation = true;
            try
            {
                AppNavigation.SelectedItem = item;
            }
            finally
            {
                _updatingNavigation = false;
            }
        }
        UpdatePageHeading();
        AppNavigation.IsBackButtonVisible = page is "log" or "deviceinfo" or "about"
            ? NavigationViewBackButtonVisible.Visible : NavigationViewBackButtonVisible.Collapsed;
        AppNavigation.IsBackEnabled = page is "log" or "deviceinfo" or "about";
        PageScrollHost.VerticalScrollBarVisibility = page == "log" ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PageScrollHost.VerticalScrollMode = page == "log" ? ScrollMode.Disabled : ScrollMode.Enabled;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(PageScrollHost, page + "Page");
        if (!changed)
            return;

        _logManager?.Debug("UI", $"页面切换: {page}");
        if (!_pageContainers.TryGetValue(page, out var container))
        {
            FrameworkElement view = page switch
            {
                "home" => HomeView,
                "eq" => EqView,
                "personal" => PersonalView,
                "log" => LogView,
                "about" => AboutView,
                "deviceinfo" => DeviceInfoView,
                _ => SettingsView
            };
            container = view;
            _pageContainers[page] = container;
        }
        PageContent.Content = container;
        PageScrollHost.UpdateLayout();
        PageScrollHost.ChangeView(null, _pageScrollOffsets.GetValueOrDefault(page), null, disableAnimation: true);
        if (page == "home")
            HomeView.SetAncPanelVisible(true);
        if (page == "eq")
        {
            _ = _commandDispatcher?.RunAsync("刷新自定义 EQ", manager => manager.RefreshCustomEqualizersAsync(CancellationToken.None));
            if (_frontendState is not null)
                EqView.ApplySnapshot(_frontendState.Snapshot);
        }
        if (page is "deviceinfo" or "settings")
            SettingsView.RefreshDeviceInfo();
        if (page == "log")
            LogView.Start();
        else
            LogView.Stop();
    }

    private void UpdatePageHeading()
    {
        var key = _currentPage switch
        {
            "eq" => "Eq_Title",
            "personal" => "Personal_Title",
            "settings" => "Settings_Title",
            "log" => "Log_Title",
            "about" => "Settings_About",
            "deviceinfo" => "DeviceInfo_Title",
            _ => "Sidebar_NavHome"
        };
        PageHeading.Text = TranslationCatalog.Get(key);
        PageHeading.Visibility = _currentPage == "home" ? Visibility.Collapsed : Visibility.Visible;
        PageLayout.RowSpacing = _currentPage == "home" ? 0 : 16;
    }
}
