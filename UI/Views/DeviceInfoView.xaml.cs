using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Control.Core.Models;

using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Subsystems.Desktop;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.UI.Views;

public sealed partial class DeviceInfoView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }
    private ControlManager? ControlManager;
    private CommandDispatcher? CommandDispatcher;
    private bool _suppressEqSelection;

    public DeviceInfoView()
    {
        InitializeComponent();
        CbEq.SelectionChanged += CbEq_SelectionChanged;
    }

    public void Attach(
        ControlManager? controlManager,
        SettingsStore uiSettings,
        ApplicationLog? log,
        CommandDispatcher? commandDispatcher,
        FrontendState? frontendState,
        DesktopLinkService? desktopLinks)
    {
        ControlManager = controlManager;
        CommandDispatcher = commandDispatcher;
    }

    public void ApplySnapshot(BusinessSnapshot snapshot)
    {
        var manager = ControlManager?.ActiveManager;
        _suppressEqSelection = true;
        try
        {
            CbEq.Items.Clear();
            if (manager is null)
            {
                CbEq.SelectedItem = null;
                return;
            }

            var presentation = manager.Presentation;
            foreach (var presetName in presentation.EqualizerPresets)
                CbEq.Items.Add(presetName);

            var selectedName = snapshot.Equalizer.PresetName
                ?? snapshot.EqualizerEntries.FirstOrDefault(entry => entry.IsSelected)?.Name;
            CbEq.SelectedItem = string.IsNullOrWhiteSpace(selectedName) ? null : selectedName;
        }
        finally
        {
            _suppressEqSelection = false;
        }
    }

    private void CbEq_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEqSelection)
            return;
        if (CbEq.SelectedItem is not string name)
            return;

        _ = CommandDispatcher?.RunAsync("EQ 预设", manager => manager.SetEqualizerByNameAsync(name, CancellationToken.None));
    }
}
