using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using YALCY.Integrations.DMX;

namespace YALCY.ViewModels;

public interface IDmxChannelSetting
{
    string Label { get; set; }
    int[]? Channel { get; set; }

    // Input range for the DMX tab's boxes. Not saved, since only [DataMember] properties are serialized.
    decimal Minimum { get; }
    decimal Maximum { get; }

    // What the DMX tab's boxes bind to, one per element of Channel. Not saved.
    DmxSettingEntry[] Entries { get; }
}

// One element of a channel array, for binding. Array elements can't announce changes, so a TwoWay
// binding to Channel[i] never sees its own writes and skips writing back the value it first loaded
// (e.g. 2 -> 0 -> 2 left the setting at 0). A notifying entry keeps the binding in step.
public class DmxSettingEntry : ReactiveObject
{
    private readonly int[] _channels;
    private readonly int _index;

    private DmxSettingEntry(int[] channels, int index)
    {
        _channels = channels;
        _index = index;
    }

    public int Value
    {
        get => _channels[_index];
        set
        {
            if (_channels[_index] == value) return;
            _channels[_index] = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsOff));
        }
    }

    // For the DMX tab's text colour: 0 is shown blue, a shared address red
    public bool IsOff => Value == 0;

    private bool _isConflict;

    public bool IsConflict
    {
        get => _isConflict;
        set => this.RaiseAndSetIfChanged(ref _isConflict, value);
    }

    public static DmxSettingEntry[] For(int[]? channels) =>
        channels == null
            ? []
            : Enumerable.Range(0, channels.Length).Select(i => new DmxSettingEntry(channels, i)).ToArray();
}

public class DmxSingleSetting : ReactiveObject
{
    private string _label;
    private int _value;

    [DataMember]
    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    [DataMember]
    public int Value
    {
        get => _value;
        set
        {
            this.RaiseAndSetIfChanged(ref _value, value);
            this.RaisePropertyChanged(nameof(IsOff));
        }
    }

    // For the DMX tab's text colour: 0 is shown blue, a shared address red
    public bool IsOff => Value == 0;

    private bool _isConflict;

    public bool IsConflict
    {
        get => _isConflict;
        set => this.RaiseAndSetIfChanged(ref _isConflict, value);
    }

    public DmxSingleSetting(string label, int value)
    {
        Label = label;
        _label = label;
        Value = value;
    }
}

public class SacnAdapterOption
{
    public SacnAdapterOption(string name, string? ipAddress)
    {
        Name = name;
        IpAddress = ipAddress;
    }

    public string Name { get; }
    public string? IpAddress { get; }
}

public class DmxChannelSetting : ReactiveObject, IDmxChannelSetting
{
    private string _label;
    private int[]? _channel;

    [DataMember]
    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    [DataMember]
    public int[]? Channel
    {
        get => _channel;
        set
        {
            this.RaiseAndSetIfChanged(ref _channel, value);
            Entries = DmxSettingEntry.For(value);
            this.RaisePropertyChanged(nameof(Entries));
        }
    }

    public DmxSettingEntry[] Entries { get; private set; } = [];

    // DMX addresses; 0 turns the channel off
    public decimal Minimum => 0;
    public decimal Maximum => 512;

    public DmxChannelSetting(string label, params int[]? channels)
    {
        Label = label;
        _label = label;
        Channel = channels;
    }
}

public class DmxDimmerChannelSetting : ReactiveObject, IDmxChannelSetting
{
    private string _label;
    private int[]? _channel;
    private DmxTalker? _dmxTalker;

    [DataMember]
    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    [DataMember]
    public int[]? Channel
    {
        get => _channel;
        set
        {
            this.RaiseAndSetIfChanged(ref _channel, value);
            Entries = DmxSettingEntry.For(value);
            this.RaisePropertyChanged(nameof(Entries));
            // DmxTalker zeroes the previous addresses on its next frame
            _dmxTalker?.UpdateMasterDimmers();
        }
    }

    public void SetDmxTalker(DmxTalker? talker)
    {
        _dmxTalker = talker;
    }

    public DmxSettingEntry[] Entries { get; private set; } = [];

    // DMX addresses; 0 turns the channel off
    public decimal Minimum => 0;
    public decimal Maximum => 512;

    public DmxDimmerChannelSetting(string label, params int[]? channels)
    {
        Label = label;
        _label = label;
        Channel = channels;
    }
}

public class DmxDimmerValueSetting : ReactiveObject, IDmxChannelSetting
{
    private string _label;
    private int[]? _channel;
    private DmxTalker? _dmxTalker;

    [DataMember]
    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    [DataMember]
    public int[]? Channel
    {
        get => _channel;
        set
        {
            this.RaiseAndSetIfChanged(ref _channel, value);
            Entries = DmxSettingEntry.For(value);
            this.RaisePropertyChanged(nameof(Entries));
            _dmxTalker?.UpdateMasterDimmers();
        }
    }

    public void SetDmxTalker(DmxTalker? talker)
    {
        _dmxTalker = talker;
    }

    public DmxSettingEntry[] Entries { get; private set; } = [];

    // DMX levels are 8-bit
    public decimal Minimum => 0;
    public decimal Maximum => 255;

    public DmxDimmerValueSetting(string label, params int[]? channels)
    {
        Label = label;
        _label = label;
        Channel = channels;
    }
}

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _sacnRestartCts;
    public ObservableCollection<DmxSingleSetting> AdvancedSettingsContainer { get; set; }
    public ObservableCollection<IDmxChannelSetting> EffectsChannelSettingsContainer { get; set; }
    public ObservableCollection<IDmxChannelSetting> MasterDimmerSettingsContainer { get; set; }
    public ObservableCollection<IDmxChannelSetting> ColorChannelSettingsContainer { get; set; }
    public ObservableCollection<DmxSingleSetting> InstrumentNoteSettingsContainer { get; set; }
    public ObservableCollection<DmxSingleSetting> BroadcastSettingsContainer { get; set; }
    public ObservableCollection<SacnAdapterOption> SacnAdapterOptions { get; private set; }

    public DmxDimmerChannelSetting MasterDimmerSettings = new("Master Dimmer Channels", DmxDefaults.MasterDimmerChannels());
    public DmxDimmerValueSetting MasterDimmerValues = new("Master Dimmer Values", DmxDefaults.MasterDimmerValues());

    public DmxChannelSetting StrobeChannels = new("Strobe Channels", DmxDefaults.StrobeChannels());
    public DmxChannelSetting FogChannels = new("Fog Channels", DmxDefaults.FogChannels());
    public DmxChannelSetting RedChannels = new("Red Channels", DmxDefaults.RedChannels());
    public DmxChannelSetting BlueChannels = new("Blue Channels", DmxDefaults.BlueChannels());
    public DmxChannelSetting YellowChannels = new("Yellow Channels", DmxDefaults.YellowChannels());
    public DmxChannelSetting GreenChannels = new("Green Channels", DmxDefaults.GreenChannels());
    public DmxSingleSetting BpmChannelSetting = new("BPM Channel", DmxDefaults.BpmChannel);
    public DmxSingleSetting CueChangeChannelSetting = new("Cue Change Channel", DmxDefaults.CueChangeChannel);
    public DmxSingleSetting PostProcessingChannelSetting = new("Post-Processing Channel", DmxDefaults.PostProcessingChannel);
    public DmxSingleSetting KeyFrameChannelSetting = new("KeyFrame Channel", DmxDefaults.KeyFrameChannel);
    public DmxSingleSetting BeatLineChannelSetting = new("BeatLine Channel", DmxDefaults.BeatLineChannel);
    public DmxSingleSetting BonusEffectChannelSetting = new("Bonus Effect Channel", DmxDefaults.BonusEffectChannel);
    public DmxSingleSetting DrumNoteChannelSetting = new("Drum Note Channel", DmxDefaults.DrumNoteChannel);
    public DmxSingleSetting GuitarNoteChannelSetting = new("Guitar Note Channel", DmxDefaults.GuitarNoteChannel);
    public DmxSingleSetting BassNoteChannelSetting = new("Bass Note Channel", DmxDefaults.BassNoteChannel);
    public DmxSingleSetting CurrentSpotlightSetting = new("Current Spotlight", DmxDefaults.CurrentSpotlightChannel);
    public DmxSingleSetting CurrentSingalongSetting = new("Current Singalong", DmxDefaults.CurrentSingalongChannel);
    public DmxSingleSetting KeysNoteChannelSetting = new("Keys Note Channel", DmxDefaults.KeysNoteChannel);
    public DmxSingleSetting VocalsNoteChannelSetting = new("Vocals Note Channel", DmxDefaults.VocalsNoteChannel);
    public DmxSingleSetting Harmony0NoteChannelSetting = new("Harmony 0 Note Channel", DmxDefaults.Harmony0NoteChannel);
    public DmxSingleSetting Harmony1NoteChannelSetting = new("Harmony 1 Note Channel", DmxDefaults.Harmony1NoteChannel);
    public DmxSingleSetting Harmony2NoteChannelSetting = new("Harmony 2 Note Channel", DmxDefaults.Harmony2NoteChannel);
    public DmxSingleSetting BroadcastUniverseSetting = new("Broadcast Universe", DmxDefaults.BroadcastUniverse);
    public DmxSingleSetting CurrentSceneSetting = new("Current Scene", DmxDefaults.CurrentSceneChannel);
    public DmxSingleSetting VenueSizeSetting = new("Venue Size", DmxDefaults.VenueSizeChannel);
    public DmxSingleSetting PauseStateSetting = new("Pause State", DmxDefaults.PauseStateChannel);
    public DmxSingleSetting SongSectionSetting = new("Song Section", DmxDefaults.SongSectionChannel);

    private SacnAdapterOption? _selectedSacnAdapter;
    public SacnAdapterOption? SelectedSacnAdapter
    {
        get => _selectedSacnAdapter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSacnAdapter, value);
            SettingsManager.SacnAdapterIp = value?.IpAddress;

            if (!DmxTalker.IsEnabled) return;

            _sacnRestartCts?.Cancel();
            _sacnRestartCts = new CancellationTokenSource();
            var token = _sacnRestartCts.Token;
            // Eh, this shouldn't really be here, too noisy.
            _ = Task.Run(async () =>
            {
                try
                {
                    // ComboBox can fire multiple changes; debounce a little
                    await Task.Delay(200, token);

                    DmxTalker.EnableDmxTalker(false, sendBlackoutOnDisable: false);
                    DmxTalker.EnableDmxTalker(true);
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    private void FeedInDmxSettings()
    {
        FeedInDmxChannelSettings();
        MasterDimmerValues.Channel = SettingsManager.MasterDimmerValuesChannel;
        BroadcastUniverseSetting.Value = SettingsManager.BroadcastUniverseSettingValue;
    }

    private void FeedInDmxChannelSettings()
    {
        BpmChannelSetting.Value = SettingsManager.BpmChannelSettingValue;
        CueChangeChannelSetting.Value = SettingsManager.CueChangeChannelSettingValue;
        PostProcessingChannelSetting.Value = SettingsManager.PostProcessingChannelSettingValue;
        KeyFrameChannelSetting.Value = SettingsManager.KeyFrameChannelSettingValue;
        BeatLineChannelSetting.Value = SettingsManager.BeatLineChannelSettingValue;
        BonusEffectChannelSetting.Value = SettingsManager.BonusEffectChannelSettingValue;
        DrumNoteChannelSetting.Value = SettingsManager.DrumNoteChannelSettingValue;
        GuitarNoteChannelSetting.Value = SettingsManager.GuitarNoteChannelSettingValue;
        BassNoteChannelSetting.Value = SettingsManager.BassNoteChannelSettingValue;
        KeysNoteChannelSetting.Value = SettingsManager.KeysNoteChannelSettingValue;
        VocalsNoteChannelSetting.Value = SettingsManager.VocalsNoteChannelSettingValue;
        Harmony0NoteChannelSetting.Value = SettingsManager.Harmony0NoteChannelSettingValue;
        Harmony1NoteChannelSetting.Value = SettingsManager.Harmony1NoteChannelSettingValue;
        Harmony2NoteChannelSetting.Value = SettingsManager.Harmony2NoteChannelSettingValue;
        CurrentSpotlightSetting.Value = SettingsManager.CurrentSpotlightSettingValue;
        CurrentSingalongSetting.Value = SettingsManager.CurrentSingalongSettingValue;
        CurrentSceneSetting.Value = SettingsManager.CurrentSceneSettingValue;
        VenueSizeSetting.Value = SettingsManager.VenueSizeSettingValue;
        PauseStateSetting.Value = SettingsManager.PauseStateSettingValue;
        SongSectionSetting.Value = SettingsManager.SongSectionSettingValue;

        MasterDimmerSettings.Channel = SettingsManager.MasterDimmerSettingsChannel;
        StrobeChannels.Channel = SettingsManager.StrobeChannelsChannel;
        FogChannels.Channel = SettingsManager.FogChannelsChannel;
        RedChannels.Channel = SettingsManager.RedChannelsChannel;
        BlueChannels.Channel = SettingsManager.BlueChannelsChannel;
        YellowChannels.Channel = SettingsManager.YellowChannelsChannel;
        GreenChannels.Channel = SettingsManager.GreenChannelsChannel;
    }

    public void ResetDmxChannelsToDefaults()
    {
        SettingsManager.ResetDmxChannelsToDefaults();
        FeedInDmxSettings();
        InitializeDmxCollections();
        this.RaisePropertyChanged(nameof(EffectsChannelSettingsContainer));
        this.RaisePropertyChanged(nameof(MasterDimmerSettingsContainer));
        this.RaisePropertyChanged(nameof(ColorChannelSettingsContainer));
        this.RaisePropertyChanged(nameof(InstrumentNoteSettingsContainer));
        this.RaisePropertyChanged(nameof(AdvancedSettingsContainer));
        this.RaisePropertyChanged(nameof(BroadcastSettingsContainer));
        SettingsManager.SaveSettings(this);
    }

    // Address boxes on the DMX tab. Master dimmer values are levels and the broadcast universe
    // isn't an address, so neither can conflict.
    private IEnumerable<IDmxChannelSetting> DmxAddressGroups =>
        [MasterDimmerSettings, RedChannels, BlueChannels, YellowChannels, GreenChannels, FogChannels, StrobeChannels];

    private IEnumerable<DmxSingleSetting> DmxAddressSettings =>
        InstrumentNoteSettingsContainer.Concat(AdvancedSettingsContainer);

    // Keep the conflict highlight current as addresses change. Groups replace their entries on
    // load and Reset, so watch for that too.
    private void WatchDmxAddresses()
    {
        foreach (var setting in DmxAddressSettings)
            setting.PropertyChanged += OnDmxAddressChanged;

        foreach (var group in DmxAddressGroups)
        {
            if (group is INotifyPropertyChanged notifier)
                notifier.PropertyChanged += OnDmxAddressGroupChanged;
            WatchEntries(group);
        }

        UpdateDmxAddressConflicts();
    }

    private void WatchEntries(IDmxChannelSetting group)
    {
        foreach (var entry in group.Entries)
            entry.PropertyChanged += OnDmxAddressChanged;
    }

    private void OnDmxAddressGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IDmxChannelSetting.Entries) || sender is not IDmxChannelSetting group) return;

        WatchEntries(group);
        UpdateDmxAddressConflicts();
    }

    private void OnDmxAddressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DmxSettingEntry.Value))
            UpdateDmxAddressConflicts();
    }

    // Mark every address box whose address is used by another address box
    private void UpdateDmxAddressConflicts()
    {
        var entries = DmxAddressGroups.SelectMany(group => group.Entries).ToList();
        var settings = DmxAddressSettings.ToList();

        var uses = new Dictionary<int, int>();
        foreach (var address in entries.Select(entry => entry.Value).Concat(settings.Select(setting => setting.Value)))
        {
            if (address > 0)
                uses[address] = uses.GetValueOrDefault(address) + 1;
        }

        foreach (var entry in entries)
            entry.IsConflict = entry.Value > 0 && uses[entry.Value] > 1;
        foreach (var setting in settings)
            setting.IsConflict = setting.Value > 0 && uses[setting.Value] > 1;
    }

    private void InitializeSacnAdapterOptions()
    {
        SacnAdapterOptions = new ObservableCollection<SacnAdapterOption>
        {
            new SacnAdapterOption("Auto (first available)", null)
        };

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipProperties = adapter.GetIPProperties();
            foreach (var address in ipProperties.UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(address.Address))
                {
                    continue;
                }

                SacnAdapterOptions.Add(new SacnAdapterOption(
                    $"{adapter.Name} ({address.Address})",
                    address.Address.ToString()));
            }
        }

        var savedIp = SettingsManager.SacnAdapterIp;
        SelectedSacnAdapter = string.IsNullOrWhiteSpace(savedIp)
            ? SacnAdapterOptions[0]
            : SacnAdapterOptions.FirstOrDefault(option => option.IpAddress == savedIp) ?? SacnAdapterOptions[0];
    }

    private void InitializeDmxCollections()
    {
        EffectsChannelSettingsContainer = new ObservableCollection<IDmxChannelSetting>
        {
            FogChannels,
            StrobeChannels,
        };

        MasterDimmerSettingsContainer = new ObservableCollection<IDmxChannelSetting>
        {
            MasterDimmerSettings,
            MasterDimmerValues,
        };

        ColorChannelSettingsContainer = new ObservableCollection<IDmxChannelSetting>
        {
            RedChannels,
            BlueChannels,
            YellowChannels,
            GreenChannels,
        };

        InstrumentNoteSettingsContainer = new ObservableCollection<DmxSingleSetting>
        {
            GuitarNoteChannelSetting,
            BassNoteChannelSetting,
            DrumNoteChannelSetting,
            KeysNoteChannelSetting,
            VocalsNoteChannelSetting,
            Harmony0NoteChannelSetting,
            Harmony1NoteChannelSetting,
            Harmony2NoteChannelSetting,
        };

        AdvancedSettingsContainer = new ObservableCollection<DmxSingleSetting>
        {
            BpmChannelSetting,
            CueChangeChannelSetting,
            BeatLineChannelSetting,
            BonusEffectChannelSetting,
            KeyFrameChannelSetting,
            PostProcessingChannelSetting,
            CurrentSingalongSetting,
            CurrentSpotlightSetting,
            CurrentSceneSetting,
            VenueSizeSetting,
            PauseStateSetting,
            SongSectionSetting,
        };

        BroadcastSettingsContainer = new ObservableCollection<DmxSingleSetting>
        {
            BroadcastUniverseSetting,
        };
    }
}
