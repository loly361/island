using System;
using NAudio.CoreAudioApi;

namespace Island.Services;

/// <summary>Master volume of the default playback device.</summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;

    public VolumeService() => Refresh();

    public bool Available => _device is not null;

    public string DeviceName
    {
        get
        {
            try { return _device?.FriendlyName ?? "—"; }
            catch { return "—"; }
        }
    }

    public int Level
    {
        get
        {
            try { return _device is null ? 0 : (int)Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100); }
            catch { return 0; }
        }
        set
        {
            try
            {
                if (_device is not null)
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 100) / 100f;
            }
            catch
            {
                // Device vanished mid-drag; Refresh() picks up the new default.
            }
        }
    }

    /// <summary>Re-resolves the default device (headphones may have been plugged in).</summary>
    public void Refresh()
    {
        try
        {
            _device?.Dispose();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            _device = null;
        }
    }

    public void Dispose()
    {
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
