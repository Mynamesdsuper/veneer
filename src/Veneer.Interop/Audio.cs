using System.Runtime.InteropServices;
using Veneer.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Veneer.Interop;

/// <summary>
/// Master volume of the default playback device, with change notifications (Core Audio callbacks, no polling).
/// Follows the default device when it changes. <see cref="Changed"/> fires on a COM thread: marshal to the UI thread.
/// </summary>
public sealed class AudioEndpoint : IDisposable
{
    readonly IMMDeviceEnumerator enumerator;
    readonly DeviceWatcher deviceWatcher;
    readonly VolumeCallback volumeCallback;
    IAudioEndpointVolume? volume;

    public AudioEndpoint()
    {
        enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        volumeCallback = new VolumeCallback(this);
        deviceWatcher = new DeviceWatcher(this);
        enumerator.RegisterEndpointNotificationCallback(deviceWatcher);
        Bind();
    }

    /// <summary>False when there is no playback device.</summary>
    public bool Available => volume is not null;

    public float Level { get; private set; }

    public bool Muted { get; private set; }

    public event Action? Changed;

    public void SetLevel(float level)
    {
        if (volume is null)
            return;
        unsafe
        {
            volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), null);
        }
    }

    public void ToggleMute()
    {
        if (volume is null)
            return;
        unsafe
        {
            volume.SetMute(!Muted, null);
        }
    }

    void Bind()
    {
        lock (this)
        {
            if (volume is not null)
            {
                try
                {
                    volume.UnregisterControlChangeNotify(volumeCallback);
                }
                catch (COMException)
                {
                }

                Marshal.ReleaseComObject(volume);
                volume = null;
            }

            try
            {
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                unsafe
                {
                    device.Activate(typeof(IAudioEndpointVolume).GUID, CLSCTX.CLSCTX_ALL, null, out object activated);
                    volume = (IAudioEndpointVolume)activated;
                }

                volume.RegisterControlChangeNotify(volumeCallback);
                Read();
            }
            catch (COMException e)
            {
                Log.Write($"no playback device: 0x{e.HResult:X8}");
                Level = 0;
                Muted = false;
            }
        }

        Changed?.Invoke();
    }

    void Read()
    {
        volume!.GetMasterVolumeLevelScalar(out float level);
        volume.GetMute(out BOOL muted);
        Level = level;
        Muted = muted;
    }

    public void Dispose()
    {
        try
        {
            enumerator.UnregisterEndpointNotificationCallback(deviceWatcher);
            volume?.UnregisterControlChangeNotify(volumeCallback);
        }
        catch (COMException)
        {
        }
    }

    sealed class VolumeCallback(AudioEndpoint owner) : IAudioEndpointVolumeCallback
    {
        public unsafe void OnNotify(AUDIO_VOLUME_NOTIFICATION_DATA* data)
        {
            owner.Level = data->fMasterVolume;
            owner.Muted = data->bMuted;
            owner.Changed?.Invoke();
        }
    }

    sealed class DeviceWatcher(AudioEndpoint owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, PCWSTR defaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
                ThreadPool.QueueUserWorkItem(_ => owner.Bind()); // never call back into Core Audio from its own callback
        }

        public void OnDeviceStateChanged(PCWSTR deviceId, DEVICE_STATE newState)
        {
        }

        public void OnDeviceAdded(PCWSTR deviceId)
        {
        }

        public void OnDeviceRemoved(PCWSTR deviceId)
        {
        }

        public void OnPropertyValueChanged(PCWSTR deviceId, PROPERTYKEY key)
        {
        }
    }
}
