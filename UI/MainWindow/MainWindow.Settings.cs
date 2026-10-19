using System;
using Microsoft.UI.Xaml;
using OppoPodsManager.Assets.Localization;

namespace OppoPodsManager.UI.MainWindow;

public partial class MainWindow
{
    internal void ApplyLanguage(LanguageOption option)
    {
        _uiSettings.SetString("Language", LanguageManager.ToStoredLanguage(option));
        LanguageManager.ApplyConfiguredCulture(option.IsAutomatic ? null : option.CultureCode);
        UpdatePageHeading();

        AppDispatcher.Post(() =>
        {
            PersonalView?.RefreshLanguageSelectionUi();
        });
        PersonalView?.RefreshLocalizedComboBoxes();
        HomeView?.RefreshAncLabels();
        HomeView?.RefreshSpatialAudioLabels();
        BuildEarphoneCustomUi();
        SettingsView?.RefreshRestoreHiddenDevicesButton();

        if (_frontendState is not null)
            ApplyNextSnapshot(_frontendState.Snapshot);
        if (_frontendState is not null)
            EqView?.ApplySnapshot(_frontendState.Snapshot);

        SettingsView?.ResetPrioritySignature();
        SyncNextMultiDeviceList(_frontendState?.Snapshot);
    }

    private void ApplyTheme(int index)
    {
        _currentThemeIndex = index;
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = index switch
            {
                1 => ElementTheme.Dark,
                2 => ElementTheme.Light,
                _ => ElementTheme.Default
            };
            _isLightTheme = root.ActualTheme == ElementTheme.Light;
        }
        else
        {
            _isLightTheme = false;
        }
        AppPalette.IsLightTheme = _isLightTheme;
        if (_frontendState is not null)
            ApplyNextSnapshot(_frontendState.Snapshot);
    }

    private void RefreshCardOpacity()
    {
        var opacity = ReadCardOpacity() / 100d;
        foreach (var entry in Application.Current.Resources.ThemeDictionaries)
        {
            if (entry.Key is "HighContrast" || entry.Value is not ResourceDictionary resources)
                continue;
            if (resources.TryGetValue("PageCardBackgroundBrush", out var resource) && resource is SolidColorBrush brush)
                brush.Opacity = opacity;
        }
    }

    private int NextThemeIndex()
        => _uiSettings.GetString("Theme")?.ToLowerInvariant() switch
        {
            "dark" => 1,
            "light" => 2,
            _ => 0
        };

    private static string NextThemeName(int index) => index switch
    {
        1 => "Dark",
        2 => "Light",
        _ => "System"
    };

    private int ReadCardOpacity()
        => Math.Clamp(_uiSettings.GetInt("CardOpacity", 90), 0, 100);

    private bool ReadUiBool(string key, bool fallback)
        => _uiSettings.GetBool(key, fallback);

    private int ReadUiInt(string key, int fallback)
        => _uiSettings.GetInt(key, fallback);

    private void WriteUiBool(string key, bool value)
        => _uiSettings.SetBool(key, value);

    private void WriteUiString(string key, string? value)
        => _uiSettings.SetString(key, value);
}
