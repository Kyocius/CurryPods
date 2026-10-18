using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Control.Core.Models;

using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Views;

public sealed partial class AboutView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }

    public AboutView()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.VersionLabel + " (Windows App SDK)";
    }

    public void Attach(
        ControlManager? controlManager,
        SettingsStore uiSettings,
        ApplicationLog? log,
        CommandDispatcher? commandDispatcher,
        FrontendState? frontendState,
        DesktopLinkService? desktopLinks)
    {
    }

    private void AboutBack_Click(object sender, RoutedEventArgs e) => Host?.RequestNavigate("settings");

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.Tag is string url)
            Host?.OpenUrl(url);
    }

    public void ApplySnapshot(BusinessSnapshot snapshot) { }
}

