using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Control.Core.Models;
using OppoPodsManager.Control.Subsystems.Logging;

using OppoPodsManager.Assets.UserSettings;
using OppoPodsManager.Control.Abstractions;
using OppoPodsManager.Control.Subsystems.Desktop;

namespace OppoPodsManager.UI.Views;

public sealed partial class LogView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }
    private ApplicationLog? Log;
    private readonly ObservableCollection<string> _renderedEntries = new();
    private int _renderedVersion = -1;
    private DispatcherTimer? _refreshTimer;

    public LogView()
    {
        InitializeComponent();
        LbLog.ItemsSource = _renderedEntries;
    }

    public void Attach(
        ControlManager? controlManager,
        SettingsStore uiSettings,
        ApplicationLog? log,
        CommandDispatcher? commandDispatcher,
        FrontendState? frontendState,
        DesktopLinkService? desktopLinks)
    {
        Log = log;
    }

    /// <summary>进入日志页时调用：启动实时刷新。</summary>
    public void Start()
    {
        if (_refreshTimer is null)
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, _) => RefreshLogView();
        }
        _refreshTimer.Start();
        RefreshLogView();
    }

    /// <summary>离开日志页时调用：停止刷新。</summary>
    public void Stop()
    {
        _refreshTimer?.Stop();
    }

    private void RefreshLogView()
    {
        var lines = (Log ?? ApplicationLog.Current)?.Snapshot()
            .Select(entry => entry.ToString())
            .ToArray() ?? [];
        var version = lines.Length;
        if (version == _renderedVersion && _renderedEntries.Count > 0)
            return;

        _renderedEntries.Clear();
        foreach (var line in lines)
            _renderedEntries.Add(line);
        _renderedVersion = version;

        if (_renderedEntries.Count > 0)
        {
            LbLog.ScrollIntoView(_renderedEntries[^1]);
        }
    }

    private async void BtnLogExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            savePicker.FileTypeChoices.Add("Zip Archive", new List<string> { ".zip" });
            savePicker.SuggestedFileName = $"CurryPods_logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

            // WinUI 3 desktop requires window handle initialization
            var window = App.CurrentWindow;
            if (window != null)
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);
            }

            var file = await savePicker.PickSaveFileAsync();
            if (file is null)
                return;

            var log = Log ?? ApplicationLog.Current;
            if (log is null)
                return;

            if (!log.TryExportZip(file.Path, out var exportError))
            {
                if (Host is { } host)
                    await host.ShowCheckResultDialogAsync(
                        string.Format(LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_ExportError),
                            exportError ?? string.Empty),
                        LanguageManager.Instance.GetString(LanguageManager.Instance.Log_ExportZip));
                return;
            }

            if (Host is { } host2)
                await host2.ShowCheckResultDialogAsync(
                    string.Format(LanguageManager.Instance.GetString(LanguageManager.Instance.Dialog_ExportSuccess), file.Path),
                    LanguageManager.Instance.GetString(LanguageManager.Instance.Log_ExportZip));
        }
        catch (Exception ex)
        {
            Log?.Error("UI", "导出日志文件时出错", ex);
        }
    }

    public void ApplySnapshot(BusinessSnapshot snapshot) { }
}
