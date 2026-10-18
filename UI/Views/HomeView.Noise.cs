using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OppoPodsManager.Assets.Localization;
using OppoPodsManager.Control.Core.Features;
using OppoPodsManager.Control.Core.Models;

namespace OppoPodsManager.UI.Views;

public partial class HomeView
{
    private bool _syncingNoise;
    private string _noiseSignature = string.Empty;
    private string _noiseLevelsSignature = string.Empty;
    private IReadOnlyList<NoiseOptionModel> _noiseOptions = [];
    private readonly Dictionary<string, string> _lastNoiseLevels = new();

    private void BuildNoiseOptions(IReadOnlyList<NoiseOptionModel> options)
    {
        var signature = string.Join("|", options.Select(option => option.Key + ":" + DeviceProfileLoader.AncLabel(option.Key)
            + ":" + string.Join(",", option.Children.Select(child => child.Key + ":" + DeviceProfileLoader.AncLabel(child.Key)))));
        if (_noiseSignature == signature)
            return;
        _noiseSignature = signature;
        _noiseOptions = options;
        AncModes.Items.Clear();
        foreach (var option in options)
            AncModes.Items.Add(new RadioButton
            {
                Content = DeviceProfileLoader.AncLabel(option.Key),
                Tag = option,
                MinWidth = 100
            });
        _noiseLevelsSignature = string.Empty;
    }

    private void SetNoiseLevels(NoiseOptionModel? option, string? selectedKey)
    {
        var levels = option?.Children ?? [];
        var signature = string.Join("|", levels.Select(level => level.Key + ":" + DeviceProfileLoader.AncLabel(level.Key)));
        if (_noiseLevelsSignature != signature)
        {
            _noiseLevelsSignature = signature;
            AncLevels.Items.Clear();
            foreach (var level in levels)
                AncLevels.Items.Add(new ComboBoxItem { Content = DeviceProfileLoader.AncLabel(level.Key), Tag = level });
        }
        AncLevels.Visibility = levels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AncLevels.SelectedItem = AncLevels.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is NoiseOptionModel level && level.Key == selectedKey);
    }

    private void AncModes_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingNoise || _realClose || AncModes.SelectedItem is not RadioButton { Tag: NoiseOptionModel option })
            return;
        var target = option.Children.Count == 0 ? option.Key
            : _lastNoiseLevels.TryGetValue(option.Key, out var last) && option.Children.Any(child => child.Key == last)
                ? last : option.Children[0].Key;
        _syncingNoise = true;
        try { SetNoiseLevels(option, target); }
        finally { _syncingNoise = false; }
        _ = SetNoiseModeAsync(target);
    }

    private void AncLevels_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_syncingNoise || _realClose || AncLevels.SelectedItem is not ComboBoxItem { Tag: NoiseOptionModel level })
            return;
        if (AncModes.SelectedItem is RadioButton { Tag: NoiseOptionModel parent })
            _lastNoiseLevels[parent.Key] = level.Key;
        _ = SetNoiseModeAsync(level.Key);
    }

    private async Task SetNoiseModeAsync(string key)
    {
        await (CommandDispatcher?.RunAsync("降噪模式", manager => manager.SetNoiseCancellationByKeyAsync(key, CancellationToken.None))
            ?? Task.FromResult(false));
        if (!_realClose && FrontendState is not null)
            ApplyNextNoiseSnapshot(FrontendState.Snapshot);
    }

    private void ApplyNextNoiseSnapshot(BusinessSnapshot snapshot)
    {
        var presentation = snapshot.IsConnected ? ControlManager?.ActiveManager?.Presentation : null;
        var supported = presentation?.SupportsNoiseCancellation == true && presentation.NoiseOptions.Count > 0;
        AncStatusInfo.IsOpen = !supported;
        AncStatusInfo.Message = TranslationCatalog.Get(snapshot.IsConnected ? "Anc_Unsupported" : "Anc_Placeholder");
        AncModes.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        _syncingNoise = true;
        try
        {
            if (!supported)
            {
                AncModes.SelectedIndex = -1;
                AncLevels.Visibility = Visibility.Collapsed;
                AncRealtimeHint.Visibility = Visibility.Collapsed;
                return;
            }
            BuildNoiseOptions(presentation!.NoiseOptions);
            var key = presentation.CurrentNoiseModeKey;
            var option = _noiseOptions.FirstOrDefault(option => option.Key == key || option.Children.Any(child => child.Key == key));
            AncModes.SelectedItem = AncModes.Items.OfType<RadioButton>().FirstOrDefault(item => ReferenceEquals(item.Tag, option));
            SetNoiseLevels(option, key);
            if (option is not null && option.Children.Any(child => child.Key == key))
                _lastNoiseLevels[option.Key] = key;
            AncRealtimeHint.Visibility = snapshot.Noise.Mode == NoiseMode.Smart && snapshot.Noise.SmartLevel.HasValue
                ? Visibility.Visible : Visibility.Collapsed;
            if (snapshot.Noise.SmartLevel is { } smartLevel)
                AncRealtimeHint.Text = string.Format(TranslationCatalog.Get("Anc_RealtimeHint"), DeviceText.NoiseModeName(smartLevel));
        }
        finally { _syncingNoise = false; }
    }

    internal void RefreshAncLabels()
    {
        _noiseSignature = string.Empty;
        if (FrontendState is not null)
            ApplyNextNoiseSnapshot(FrontendState.Snapshot);
    }

    private async Task RunGameSoundCommandAsync(bool enabled)
    {
        _gameSoundCommandPending = true;
        try
        {
            await (CommandDispatcher?.RunAsync("游戏音效", manager => manager.SetGameSoundEnabledAsync(enabled, CancellationToken.None))
                ?? Task.FromResult(false));
        }
        finally
        {
            _gameSoundCommandPending = false;
            if (!_realClose && FrontendState is not null)
                AppDispatcher.Post(() => ApplyNextFeatureState(FrontendState.Snapshot));
        }
    }
}
