using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Models;
namespace OppoPodsManager.UI.Views;

/// <summary>
/// 所有页面视图的接口。MainWindow 外壳在构造后通过 <see cref="Attach"/> 注入共享服务，
/// 并通过 <see cref="ApplySnapshot"/> 把控制层快照路由到对应页面。
/// </summary>
public interface IPageView
{
    IViewHost? Host { get; set; }

    void Attach(
        ControlManager? controlManager,
        SettingsStore uiSettings,
        ApplicationLog? log,
        CommandDispatcher? commandDispatcher,
        FrontendState? frontendState,
        DesktopLinkService? desktopLinks);

    /// <summary>
    /// 将控制层发布的不可变快照转换到本页视觉状态。无状态可更新的页面留空实现。
    /// </summary>
    void ApplySnapshot(BusinessSnapshot snapshot);
}

