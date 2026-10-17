using YALCY.Diagnostics;
using System;
using Dmx.Net.Controllers;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using HidSharp;
using YALCY.Integrations;
using YALCY.Integrations.StageKit;
using YALCY.Usb;
using YALCY.ViewModels;
using YALCY.Views.Components;
#if LINUX || MACOS
using System.Linq;
#endif
using Timer = System.Timers.Timer;
namespace YALCY.Integrations.Serial;

public class SerialTalker: IDisposable
{
    //private DmxTimer timer;
    private OpenDmxController controller;
    // DMX spec says 44 updates per second is the max
    private const float TargetFps = 44f;
    private const float TimeBetweenCalls = 1f / TargetFps;
    private static Timer? _timer;
    private static Timer? _checkerTimer;
    private MainWindowViewModel? _mainViewModel;
    private static bool SerialEnabled = false;
    private readonly ManualStrobeFlasher _manualStrobeFlasher = new(ex => AppLog.Write(LogLevel.Error, "Serial", $"Serial manual strobe error: {ex.Message}"));

    public void EnableSerialTalker(bool isEnabled, MainWindowViewModel? viewModel = null)
    {
        if (viewModel != null)
        {
            _mainViewModel = viewModel;
        }

        if (_mainViewModel == null)
        {
            AppLog.Write(LogLevel.Warning, "Serial", "SerialTalker: No ViewModel provided and none cached.");
            return;
        }

        SerialEnabled = isEnabled;
        if (SerialEnabled)
        {
            UsbDeviceMonitor.OnStageKitCommand += OnStageKitEvent;
            StatusFooter.UpdateStatus("Serial", IntegrationStatus.Connected);

            controller = new OpenDmxController();

            try
            {
#if LINUX || MACOS
                var devicePath = GetLinuxSerialDevicePath();
                if (string.IsNullOrWhiteSpace(devicePath))
                {
                    _mainViewModel.SerialMessage = "Error: No serial devices found for DMX output.";
                    StatusFooter.UpdateStatus("Serial", IntegrationStatus.Error,
                        "No serial device was found for DMX output. Connect the USB DMX adapter and check device permissions. Waiting for a device to be added.");
                    UsbDeviceMonitor.SerialDeviceAdded += SerialDeviceAdded;
                    return;
                }

                controller.Open(devicePath);
#else
                controller.Open(0);
#endif
                UsbDeviceMonitor.SerialDeviceAdded -= SerialDeviceAdded;  //disable the watchdog.

                _timer = new Timer(TimeBetweenCalls * 1000);
                _timer.Elapsed += (sender, e) => Sender();
                _timer.Start();
            }
            catch (Exception e)
            {
                _mainViewModel.SerialMessage = $"Error: {e.Message}";
                StatusFooter.UpdateStatus("Serial", IntegrationStatus.Error,
                    $"Could not open or start the serial DMX controller: {e.GetType().Name}: {e.Message} Check the adapter connection, driver, and whether another application is using it.");
                UsbDeviceMonitor.SerialDeviceAdded += SerialDeviceAdded;  //start the watchdog.
            }
        }
        else
        {
            Dispose();
        }
    }

    private void OnStageKitEvent(StageKitTalker.CommandId commandId, byte parameter)
    {
        if (_mainViewModel == null) return;

        if (controller.IsOpen == false)
        {
            // Don't try to reopen here - the SerialDeviceAdded watchdog handles reconnection.
            return;
        }
        {
            switch (commandId)
            {
                case StageKitTalker.CommandId.BlueLeds:
                    SetLeds(_mainViewModel.BlueChannels.Channel, parameter);
                    break;

                case StageKitTalker.CommandId.GreenLeds:
                    SetLeds(_mainViewModel.GreenChannels.Channel, parameter);
                    break;

                case StageKitTalker.CommandId.YellowLeds:
                    SetLeds(_mainViewModel.YellowChannels.Channel, parameter);
                    break;

                case StageKitTalker.CommandId.RedLeds:
                    SetLeds(_mainViewModel.RedChannels.Channel, parameter);
                    break;

                case StageKitTalker.CommandId.FogOn:
                    SetChannels(_mainViewModel.FogChannels.Channel, 255);
                    break;

                case StageKitTalker.CommandId.FogOff:
                    SetChannels(_mainViewModel.FogChannels.Channel, 0);
                    break;

                case StageKitTalker.CommandId.StrobeOff:
                    _manualStrobeFlasher.Stop(SetStrobeChannelsForManualFlashAsync);
                    SetStrobeChannels(0);
                    break;

                case StageKitTalker.CommandId.StrobeSlow:
                    HandleStrobeCommand(commandId, 4);
                    break;

                case StageKitTalker.CommandId.StrobeMedium:
                    HandleStrobeCommand(commandId, 6);
                    break;

                case StageKitTalker.CommandId.StrobeFast:
                    HandleStrobeCommand(commandId, 8);
                    break;

                case StageKitTalker.CommandId.StrobeFastest:
                    HandleStrobeCommand(commandId, 10);
                    break;

                case StageKitTalker.CommandId.DisableAll:
                    _manualStrobeFlasher.Stop(SetStrobeChannelsForManualFlashAsync);
                    ClearAllOutputChannels();
                    break;
            }
        }
    }

    private void HandleStrobeCommand(StageKitTalker.CommandId commandId, int commandMultiplier)
    {
        if (_mainViewModel == null) return;

        if (_mainViewModel.SerialStrobeMode == StrobeOutputModes.ManualFlash)
        {
            _manualStrobeFlasher.Start(
                commandId,
                Udp.UdpIntake.BeatsPerMinute.Value,
                SetStrobeChannelsForManualFlashAsync);
            return;
        }

        _manualStrobeFlasher.Stop(SetStrobeChannelsForManualFlashAsync);
        SetStrobeChannels(StrobeDmxFromBpm(Udp.UdpIntake.BeatsPerMinute.Value, commandMultiplier));
    }

    private Task SetStrobeChannelsForManualFlashAsync(bool isOn, CancellationToken cancellationToken)
    {
        var allowFlash = isOn && !UsbDeviceMonitor.IsOutputSuppressed;
        SetStrobeChannels(allowFlash ? (byte)255 : (byte)0);
        return Task.CompletedTask;
    }

    private void SetStrobeChannels(byte value)
    {
        SetChannels(_mainViewModel?.StrobeChannels.Channel, value);
    }

    private void SetChannels(int[]? channels, byte value)
    {
        if (channels == null || controller == null || !controller.IsOpen)
        {
            return;
        }

        for (int i = 0; i < channels.Length; i++)
        {
            SetChannelIfValid(channels[i], value);
        }
    }

    // Set LED pattern based on parameter bitmask
    private void SetLeds(int[]? channels, byte parameter)
    {
        if (channels == null) return;

        for (int i = 0; i < 8 && i < channels.Length; i++)
        {
            SetChannelIfValid(channels[i], (parameter & (1 << i)) != 0 ? (byte)255 : (byte)0);
        }
    }

    // Address 0 means the channel is turned off; the controller throws on anything outside 1-512
    private void SetChannelIfValid(int channel, byte value)
    {
        if (channel < 1 || channel > 512) return;
        controller.SetChannel(channel, value);
    }

    private void ClearAllOutputChannels()
    {
        if (controller == null || !controller.IsOpen)
        {
            return;
        }

        for (var channel = 1; channel <= 512; channel++)
        {
            controller.SetChannel(channel, 0);
        }
    }

    private static byte StrobeDmxFromBpm(float bpm, int speed)
    {
        var f = bpm * speed / 60.0;
        if (f <= 0) return 0;
        if (f > 25.0) f = 25.0;
        return (byte)Math.Round((f / 25.0) * 255.0);
    }

    private void Sender()
    {
        if (!SerialEnabled || !controller.IsOpen || _mainViewModel == null)
        {
            return;
        }

        var udpIntake = _mainViewModel.UdpIntake;

        if (UsbDeviceMonitor.IsOutputSuppressed)
        {
            controller.WriteSafe();
            return;
        }

        SetChannelIfValid(_mainViewModel.GuitarNoteChannelSetting.Value, udpIntake.CurrentGuitarNotes.Value);
        SetChannelIfValid(_mainViewModel.BassNoteChannelSetting.Value, udpIntake.CurrentBassNotes.Value);
        SetChannelIfValid(_mainViewModel.DrumNoteChannelSetting.Value, udpIntake.CurrentDrumNotes.Value);
        SetChannelIfValid(_mainViewModel.VocalsNoteChannelSetting.Value, (byte)udpIntake.CurrentVocalNote.Value);
        SetChannelIfValid(_mainViewModel.Harmony0NoteChannelSetting.Value, (byte)udpIntake.CurrentHarmony0Note.Value);
        SetChannelIfValid(_mainViewModel.Harmony1NoteChannelSetting.Value, (byte)udpIntake.CurrentHarmony1Note.Value);
        SetChannelIfValid(_mainViewModel.Harmony2NoteChannelSetting.Value, (byte)udpIntake.CurrentHarmony2Note.Value);

        SetChannelIfValid(_mainViewModel.BpmChannelSetting.Value, (byte)Udp.UdpIntake.BeatsPerMinute.Value);
        SetChannelIfValid(_mainViewModel.KeyFrameChannelSetting.Value, udpIntake.Keyframe.Value);
        SetChannelIfValid(_mainViewModel.VenueSizeSetting.Value, Udp.UdpIntake.Venue.Value);
        SetChannelIfValid(_mainViewModel.CueChangeChannelSetting.Value, udpIntake.LightingCue.Value);
        SetChannelIfValid(_mainViewModel.PostProcessingChannelSetting.Value, udpIntake.PostProcessing.Value);
        SetChannelIfValid(_mainViewModel.PauseStateSetting.Value, udpIntake.Paused.Value);
        SetChannelIfValid(_mainViewModel.BeatLineChannelSetting.Value, udpIntake.Beat.Value);
        SetChannelIfValid(_mainViewModel.CurrentSingalongSetting.Value, udpIntake.Singalong.Value);
        SetChannelIfValid(_mainViewModel.CurrentSpotlightSetting.Value, udpIntake.Spotlight.Value);
        SetChannelIfValid(_mainViewModel.SongSectionSetting.Value, udpIntake.CurrentSongSection.Value);
        SetChannelIfValid(_mainViewModel.BonusEffectChannelSetting.Value, (byte)(udpIntake.BonusEffect.Value ? 255 : 0));
        SetChannelIfValid(_mainViewModel.CurrentSceneSetting.Value, udpIntake.CurrentScene.Value);

        var dimmerChannels = _mainViewModel.MasterDimmerSettings.Channel;
        var dimmerValues = _mainViewModel.MasterDimmerValues.Channel;
        if (dimmerChannels != null && dimmerValues != null)
        {
            for (int i = 0; i < 8 && i < dimmerChannels.Length && i < dimmerValues.Length; i++)
            {
                SetChannelIfValid(dimmerChannels[i], (byte)dimmerValues[i]);
            }
        }

        controller.WriteSafe();
    }

    public void Dispose()
    {
        UsbDeviceMonitor.OnStageKitCommand -= OnStageKitEvent;
        _manualStrobeFlasher.Stop(SetStrobeChannelsForManualFlashAsync);
        StatusFooter.UpdateStatus("Serial", IntegrationStatus.Off);
        if (controller != null)
        {
            controller.Dispose();
        }
        _timer?.Stop();
        _timer?.Dispose();

        SerialEnabled = false;
        _timer = null;
    }

#if LINUX || MACOS
    private static string? GetLinuxSerialDevicePath()
    {
        var devices = DeviceList.Local.GetSerialDevices().ToList();
        if (devices.Count == 0)
        {
            return null;
        }

        var byIdDevice = devices.FirstOrDefault(device => device.GetFileSystemName().StartsWith("/dev/serial/by-id/", StringComparison.OrdinalIgnoreCase));
        if (byIdDevice != null)
        {
            return byIdDevice.GetFileSystemName();
        }

        var ttyDevice = devices.FirstOrDefault(device => device.GetFileSystemName().StartsWith("/dev/tty", StringComparison.OrdinalIgnoreCase));
        return ttyDevice?.GetFileSystemName() ?? devices[0].GetFileSystemName();
    }
#endif

    private void SerialDeviceAdded(SerialDevice device)
    {
        //When a serial device is added, will try to start again, if the state is enabled.
        EnableSerialTalker(SettingsManager.SerialEnabledSettingIsEnabled, _mainViewModel);
    }
}
