using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OppoPodsManager.Assets.Localization;

namespace OppoPodsManager.UI.Views;

/// <summary>语言、主题、通知、耳机图案和设备名称设置。</summary>
public partial class PersonalView : UserControl, IPageView
{
    public IViewHost? Host { get; set; }
    private async void PreviewToast_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Host is not null)
                await Host.PreviewToastAsync();
        }
        catch (Exception exception)
        {
            Log?.Error("Toast", "预览通知失败。", exception);
        }
    }
    private ControlManager? ControlManager;
    private SettingsStore UiSettings = null!;
    private ApplicationLog? Log;
    private CommandDispatcher? CommandDispatcher;
    private FrontendState? FrontendState;
    private DesktopLinkService? DesktopLinks;

    private bool _initializing;
    private bool _refreshingComboBoxes;
    private readonly ObservableCollection<LanguageOption> _languageList = new();

    public PersonalView()
    {
        InitializeComponent();

        // 仅本页控件的事件接线；外壳级副作用经 Host 回退。
        CbTheme.SelectionChanged += CbTheme_Changed;
        CbLanguage.SelectionChanged += CbLanguage_Changed;
        CbToastDuration.SelectionChanged += CbToastDuration_Changed;

        RoutedEventHandler onLowBatteryChanged = (_, _) =>
        {
            if (_initializing) return;
            var on = CbLowBatteryToast.IsOn == true;
            UiSettings.SetBool("LowBatteryToast", on);
            Log?.Debug("UI", $"设置: 低电量提醒 -> {on}");
        };
        CbLowBatteryToast.Toggled += onLowBatteryChanged;

        RoutedEventHandler onConnectionToastChanged = (_, _) =>
        {
            if (_initializing) return;
            var on = CbConnectionToast.IsOn == true;
            UiSettings.SetBool("ConnectionToast", on);
            Log?.Debug("UI", $"设置: 连接弹窗 -> {on}");
        };
        CbConnectionToast.Toggled += onConnectionToastChanged;

        TbCustomName.TextChanged += TbCustomName_Changed;

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
        UiSettings = uiSettings;
        Log = log;
        CommandDispatcher = commandDispatcher;
        FrontendState = frontendState;
        DesktopLinks = desktopLinks;
        _initializing = true;
        try
        {
            InitializeLanguageSelection();
            CbTheme.SelectedIndex = NextThemeIndex();

            CbToastDuration.SelectedIndex = ReadToastDurationIndex();
            CbLowBatteryToast.IsOn = UiSettings.GetBool("LowBatteryToast", true);
            CbConnectionToast.IsOn = UiSettings.GetBool("ConnectionToast", true);

            var customName = UiSettings.GetString("CustomName");
            TbCustomName.Text = customName ?? "";

        }
        finally
        {
            _initializing = false;
        }
    }

    public void ApplySnapshot(BusinessSnapshot snapshot)
    {
        // 个性化页面无快照驱动的状态，留空。
    }

    // ---- 语言 ----
    private void CbLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingComboBoxes || _initializing || CbLanguage.SelectedItem is not LanguageOption option)
            return;
        Host?.ApplyLanguage(option);
    }

    private void InitializeLanguageSelection()
    {
        _languageList.Clear();
        foreach (var option in LanguageManager.GetAvailableLanguages())
            _languageList.Add(option);

        CbLanguage.ItemsSource = _languageList;
        var configured = UiSettings.GetString("Language");
        var selected = _languageList.FirstOrDefault(option =>
            string.Equals(option.CultureCode,
                LanguageManager.NormalizeSelectionCulture(configured),
                StringComparison.OrdinalIgnoreCase));
        CbLanguage.SelectedItem = selected ?? _languageList[0];
    }

    /// <summary>外壳在切换语言后调用：替换首项"自动"的本地化文本并恢复选中。</summary>
    internal void RefreshLanguageSelectionUi()
    {
        var autoOption = LanguageManager.GetAvailableLanguages()[0];
        _refreshingComboBoxes = true;
        try
        {
            var selIdx = CbLanguage.SelectedIndex;
            _languageList[0] = autoOption;
            if (selIdx >= 0 && selIdx < _languageList.Count)
                CbLanguage.SelectedIndex = selIdx;
        }
        finally
        {
            _refreshingComboBoxes = false;
        }
    }

    /// <summary>外壳在切换语言后调用：刷新本页受本地化影响的下拉框显示。</summary>
    internal void RefreshLocalizedComboBoxes()
    {
        _refreshingComboBoxes = true;
        RefreshSelectedIndex(CbTheme);
        RefreshSelectedIndex(CbToastDuration);
        RefreshSelectedIndex(CbLanguage);
        _refreshingComboBoxes = false;
    }

    private static void RefreshSelectedIndex(ComboBox comboBox)
    {
        var idx = comboBox.SelectedIndex;
        if (idx >= 0)
        {
            comboBox.SelectedIndex = -1;
            comboBox.SelectedIndex = idx;
        }
    }

    // ---- 主题 ----
    private void CbTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingComboBoxes) return;
        var idx = CbTheme.SelectedIndex;
        Log?.Debug("UI", $"用户操作: 切换主题 -> {idx}");
        Host?.ApplyTheme(idx);
    }

    private int NextThemeIndex()
        => UiSettings.GetString("Theme")?.ToLowerInvariant() switch
        {
            "dark" => 1,
            "light" => 2,
            _ => 0
        };

    // ---- Toast 时长 ----
    private void CbToastDuration_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingComboBoxes || _initializing) return;
        UiSettings.SetInt("ToastDuration", ToastDurationSecondsFromIndex(CbToastDuration.SelectedIndex));
        Log?.Debug("UI", $"设置: Toast 时长索引 -> {CbToastDuration.SelectedIndex}");
    }

    private int ReadToastDurationIndex()
    {
        var seconds = UiSettings.GetInt("ToastDuration", 5);
        return seconds switch { 3 => 0, 4 => 1, 6 => 3, 7 => 4, 8 => 5, _ => 2 };
    }

    private static int ToastDurationSecondsFromIndex(int index) => index switch
    {
        0 => 3,
        1 => 4,
        3 => 6,
        4 => 7,
        5 => 8,
        _ => 5
    };

    // ---- 设备名称 ----
    private void TbCustomName_Changed(object sender, TextChangedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(TbCustomName.Text) ? null : TbCustomName.Text.Trim();
        UiSettings.SetString("CustomName", name);
        Host?.SetCustomDeviceName(name);
    }
}
