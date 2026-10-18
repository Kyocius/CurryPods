using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Brands.Oppo.Features;
using OppoPodsManager.Control.Brands.Oppo.Models;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Tray;

// 构建托盘的最小常驻菜单，所有业务状态仍由主窗口和控制层维护。
internal static class TrayMenu
{
    public static MenuFlyout Create(
        BusinessSnapshot snapshot,
        IBrandManager? manager,
        CommandDispatcher dispatcher,
        Action showMainWindow,
        Action showMiniWindow,
        Action exitApplication)
    {
        var menu = new MenuFlyout();
        if (snapshot.IsConnected && manager is not null)
        {
            AddDeviceControls(menu, snapshot, manager, dispatcher);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var showItem = new MenuFlyoutItem { Text = TranslationCatalog.Get("Tray_ShowMain") };
        showItem.Command = new TrayCommand(() =>
        {
            ApplicationLog.Current?.Info("Tray", "点击托盘菜单：显示主窗口。");
            showMainWindow();
        });

        var miniItem = new MenuFlyoutItem { Text = TranslationCatalog.Get("Tray_ShowMini") };
        miniItem.Command = new TrayCommand(() =>
        {
            ApplicationLog.Current?.Info("Tray", "点击托盘菜单：显示小窗口。");
            showMiniWindow();
        });

        menu.Items.Add(miniItem);
        menu.Items.Add(showItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = TranslationCatalog.Get("Tray_Quit") };
        exitItem.Command = new TrayCommand(() =>
        {
            ApplicationLog.Current?.Info("Tray", "点击托盘菜单：退出应用。");
            exitApplication();
        });
        menu.Items.Add(exitItem);
        return menu;
    }

    private static void AddDeviceControls(
        MenuFlyout menu,
        BusinessSnapshot snapshot,
        IBrandManager manager,
        CommandDispatcher dispatcher)
    {
        var presentation = manager.Presentation;
        AddNoiseModes(menu, snapshot, manager, dispatcher);
        if (presentation.VisibleControls.Contains("game-mode"))
        {
            AddToggle(menu, TranslationCatalog.Get("Feature_GameMode"), snapshot.Game.IsEnabled == true,
                "托盘游戏模式",
                dispatcher,
                (active, enabled) => active.SetGameModeAsync(enabled, CancellationToken.None));
        }
        if (presentation.VisibleControls.Contains("spatial-sound"))
        {
            var enabled = presentation.ControlStates.TryGetValue("spatial-sound", out var value) && value;
            AddToggle(menu, TranslationCatalog.Get("Feature_SpatialSound"), enabled,
                "托盘空间声场",
                dispatcher,
                (active, value) => active.SetSpatialSoundAsync(value, CancellationToken.None));
        }
        if (presentation.VisibleControls.Contains("dual-device"))
        {
            var enabled = presentation.ControlStates.TryGetValue("dual-device", out var value) && value;
            AddToggle(menu, TranslationCatalog.Get("Feature_DualDevice"), enabled,
                "托盘双设备",
                dispatcher,
                (active, value) => active.SetDualDeviceAsync(value, CancellationToken.None));
        }
    }

    private static void AddNoiseModes(
        MenuFlyout menu,
        BusinessSnapshot snapshot,
        IBrandManager manager,
        CommandDispatcher dispatcher)
    {
        var modes = FlattenNoiseModes(manager.Presentation.NoiseOptions)
            .Distinct()
            .ToArray();
        foreach (var mode in modes)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = DeviceText.NoiseModeName(mode),
                IsChecked = snapshot.Noise.Mode == mode
            };
            item.Command = new TrayCommand(async () =>
            {
                ApplicationLog.Current?.Info("Tray", $"点击托盘菜单降噪：mode={mode}。");
                await dispatcher.RunAsync(
                    "托盘降噪",
                    active => active.SetNoiseCancellationAsync(mode, CancellationToken.None));
            });
            menu.Items.Add(item);
        }

        if (modes.Length > 0)
            menu.Items.Add(new MenuFlyoutSeparator());
    }

    private static IEnumerable<NoiseMode> FlattenNoiseModes(IEnumerable<NoiseOptionModel> options)
    {
        foreach (var option in options)
        {
            if (option.Children.Count == 0 && option.Mode != NoiseMode.Unknown)
                yield return option.Mode;

            foreach (var child in FlattenNoiseModes(option.Children))
                yield return child;
        }
    }

    private static void AddToggle(
        MenuFlyout menu,
        string label,
        bool enabled,
        string operation,
        CommandDispatcher dispatcher,
        Func<IBrandManager, bool, Task<bool>> setEnabled)
    {
        var item = new ToggleMenuFlyoutItem
        {
            Text = label,
            IsChecked = enabled
        };
        item.Command = new TrayCommand(async () =>
        {
            ApplicationLog.Current?.Info("Tray", $"点击托盘菜单开关：label={label}，enabled={!enabled}。");
            await dispatcher.RunAsync(operation, active => setEnabled(active, !enabled));
        });
        menu.Items.Add(item);
    }
}
