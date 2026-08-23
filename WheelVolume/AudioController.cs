using System.Runtime.InteropServices;

namespace WheelVolume;

internal readonly record struct AudioState(int VolumePercent, bool Muted);

internal interface IAudioEndpoint : IDisposable
{
    float Volume { get; set; }
    bool Muted { get; set; }
}

internal interface IAudioEndpointProvider : IDisposable
{
    event Action? DefaultEndpointChanged;

    IAudioEndpoint GetDefaultEndpoint();
}

internal sealed class AudioController : IDisposable
{
    private readonly object _sync = new();
    private readonly IAudioEndpointProvider _endpointProvider;
    private IAudioEndpoint? _endpoint;
    private bool _disposed;

    public AudioController(IAudioEndpointProvider endpointProvider)
    {
        _endpointProvider = endpointProvider;
        _endpointProvider.DefaultEndpointChanged += HandleDefaultEndpointChanged;
    }

    public AudioState ChangeVolume(int wheelSteps, float volumeStep)
    {
        return UseEndpoint(
            endpoint =>
            {
                endpoint.Volume = Math.Clamp(
                    endpoint.Volume + (wheelSteps * volumeStep),
                    0.0f,
                    1.0f
                );

                return GetState(endpoint);
            }
        );
    }

    public AudioState ToggleMute()
    {
        return UseEndpoint(
            endpoint =>
            {
                endpoint.Muted = !endpoint.Muted;
                return GetState(endpoint);
            }
        );
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _endpointProvider.DefaultEndpointChanged -= HandleDefaultEndpointChanged;
            DisposeEndpoint();
            _endpointProvider.Dispose();
        }
    }

    private AudioState UseEndpoint(Func<IAudioEndpoint, AudioState> operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                return operation(GetEndpoint());
            }
            catch (COMException)
            {
                DisposeEndpoint();
                return operation(GetEndpoint());
            }
        }
    }

    private IAudioEndpoint GetEndpoint()
    {
        return _endpoint ??= _endpointProvider.GetDefaultEndpoint();
    }

    private void HandleDefaultEndpointChanged()
    {
        lock (_sync)
        {
            if (!_disposed)
                DisposeEndpoint();
        }
    }

    private void DisposeEndpoint()
    {
        _endpoint?.Dispose();
        _endpoint = null;
    }

    private static AudioState GetState(IAudioEndpoint endpoint)
    {
        return new AudioState(
            (int)Math.Round(endpoint.Volume * 100),
            endpoint.Muted
        );
    }
}
