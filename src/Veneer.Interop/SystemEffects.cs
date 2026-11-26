using System.Runtime.InteropServices;
using Veneer.Core;
using Windows.UI.ViewManagement;

namespace Veneer.Interop;

/// <summary>
/// Watches the two Windows states that turn costly effects off: "Transparency effects" (Settings › Personalization ›
/// Colors) and energy saver (battery saver before Windows 11 24H2). Both are event-driven: Windows tells us, nothing
/// polls.
/// </summary>
public sealed class SystemEffects : IDisposable
{
    const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;

    // winnt.h (Windows SDK 10.0.26100): energy saver from 24H2 on; the older battery saver setting is still sent.
    static readonly Guid EnergySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");
    static readonly Guid PowerSavingStatus = new("e00958c0-c213-4ace-ac77-fecced2eeea5");

    readonly UISettings ui = new();
    readonly List<nint> registrations = [];
    bool transparency = true, energySaver, batterySaver;

    // Called when Transparency or EnergySaver changes: on the window's thread for energy saver, on a thread-pool
    // thread for transparency.
    readonly Action changed;

    public bool Transparency => Volatile.Read(ref transparency);

    public bool EnergySaver => energySaver || batterySaver;

    /// <param name="changed">
    /// Called when <see cref="Transparency"/> or <see cref="EnergySaver"/> changes: on the window's thread for energy
    /// saver, on a thread-pool thread for transparency.
    /// </param>
    public SystemEffects(Action changed) => this.changed = changed;

    /// <summary>
    /// Starts watching. Windows sends the current energy saver state to <paramref name="hwnd"/> right away (feed its
    /// messages to <see cref="WndProc"/>); the transparency setting is read on the thread pool, so the caller's thread
    /// never waits for it.
    /// </summary>
    public void Watch(nint hwnd)
    {
        foreach (var guid in new[] { EnergySaverStatus, PowerSavingStatus })
        {
            var guidCopy = guid;
            nint handle = RegisterPowerSettingNotification(hwnd, ref guidCopy, 0 /* DEVICE_NOTIFY_WINDOW_HANDLE */);
            if (handle == 0)
                Log.Write($"effects: power setting {guid} not watched (error {Marshal.GetLastWin32Error()})");
            else
                registrations.Add(handle);
        }

        ui.AdvancedEffectsEnabledChanged += (_, _) => ReadTransparency();
        Task.Run(ReadTransparency);
    }

    void ReadTransparency()
    {
        try
        {
            bool now = ui.AdvancedEffectsEnabled;
            if (Interlocked.Exchange(ref transparency, now) != now)
                changed();
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            Log.Write($"effects: transparency setting not readable: {e.Message}");
        }
    }

    /// <summary>Feed the window's messages here. True when the message was an energy saver change.</summary>
    public unsafe bool WndProc(int msg, nint wParam, nint lParam)
    {
        if (msg != WM_POWERBROADCAST || wParam != PBT_POWERSETTINGCHANGE || lParam == 0)
            return false;
        var setting = (PowerBroadcastSetting*)lParam;
        if (setting->DataLength < sizeof(uint))
            return false;
        bool on = setting->Data != 0; // energy saver: 0 off, 1 standard, 2 high savings; battery saver: 0 or 1
        bool before = EnergySaver;
        if (setting->PowerSetting == EnergySaverStatus)
            energySaver = on;
        else if (setting->PowerSetting == PowerSavingStatus)
            batterySaver = on;
        else
            return false;
        if (EnergySaver != before)
            changed();
        return true;
    }

    public void Dispose()
    {
        foreach (nint handle in registrations)
            UnregisterPowerSettingNotification(handle);
        registrations.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data; // the first 4 bytes of the data; both settings send a DWORD
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern nint RegisterPowerSettingNotification(nint recipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnregisterPowerSettingNotification(nint handle);
}
