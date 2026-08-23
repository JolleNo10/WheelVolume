using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WheelVolume;

internal sealed class NAudioEndpointProvider : IAudioEndpointProvider, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;

    public NAudioEndpointProvider()
    {
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event Action? DefaultEndpointChanged;

    public IAudioEndpoint GetDefaultEndpoint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return new NAudioEndpoint(
            _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (!_disposed && flow == DataFlow.Render && role == Role.Multimedia)
            DefaultEndpointChanged?.Invoke();
    }

    public void OnDeviceAdded(string pwstrDeviceId) { }

    public void OnDeviceRemoved(string deviceId) { }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}

internal sealed class NAudioEndpoint(MMDevice device) : IAudioEndpoint
{
    private readonly MMDevice _device = device;

    public float Volume
    {
        get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        set => _device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
    }

    public bool Muted
    {
        get => _device.AudioEndpointVolume.Mute;
        set => _device.AudioEndpointVolume.Mute = value;
    }

    public void Dispose()
    {
        _device.Dispose();
    }
}
