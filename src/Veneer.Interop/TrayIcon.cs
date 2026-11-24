using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>
/// Veneer's icon in the notification area, with the executable's own icon. Only visible while the Windows taskbar
/// is (Veneer hides it): the same menu is also in the bar. Feed the owner window's messages to <see cref="WndProc"/>,
/// and call <see cref="OnShellRestarted"/> on <c>TaskbarCreated</c>: Explorer forgets every icon when it restarts.
/// </summary>
public sealed unsafe class TrayIcon : IDisposable
{
    const uint CallbackMessage = 0x8000 + 0x56; // WM_APP + 'V'
    const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    static readonly Guid Id = new("5C0F6E37-0C0B-4B4C-9E2E-56454E454552"); // stable per app, so Windows keeps the user's "show/hide" choice

    readonly HWND owner;
    readonly HICON icon;
    readonly string tip;
    bool added;
    bool useGuid = true;

    public TrayIcon(nint ownerWindow, string tooltip)
    {
        owner = (HWND)ownerWindow;
        tip = tooltip;
        HICON small;
        fixed (char* exe = Environment.ProcessPath ?? "")
            PInvoke.ExtractIconEx(exe, 0, null, &small, 1);
        icon = small;
        Add();
    }

    /// <summary>Left or right click on the icon: the app shows its menu at the mouse.</summary>
    public event Action? Clicked;

    public void OnShellRestarted()
    {
        added = false;
        Add();
    }

    /// <summary>Call for every message of the owner window. Returns true when the message was the icon's.</summary>
    public bool WndProc(int msg, nint lParam)
    {
        if (msg != CallbackMessage)
            return false;
        if ((int)lParam is WM_LBUTTONUP or WM_RBUTTONUP)
        {
            // The owner must be in front, or the menu would not close when the user clicks elsewhere.
            PInvoke.SetForegroundWindow(owner);
            Clicked?.Invoke();
        }

        return true;
    }

    void Add()
    {
        useGuid = true;
        added = TryAdd();
        if (!added)
        {
            // Left over from a previous run that died: take it over.
            var stale = Data();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &stale);
            added = TryAdd();
        }

        if (!added)
        {
            // Windows ties the GUID to the executable's path: another copy of Veneer (a Debug build, an older
            // install) owns it. Fall back to the classic window + number identity.
            useGuid = false;
            added = TryAdd();
        }

        if (!added)
            Veneer.Core.Log.Write("tray: the notification-area icon could not be added");
    }

    bool TryAdd()
    {
        var data = Data();
        data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = icon;
        data.szTip = tip;
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
    }

    NOTIFYICONDATAW Data() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = owner,
        uID = 1,
        guidItem = useGuid ? Id : default,
        uFlags = useGuid ? NOTIFY_ICON_DATA_FLAGS.NIF_GUID : 0,
    };

    public void Dispose()
    {
        if (added)
        {
            var data = Data();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            added = false;
        }

        if (!icon.IsNull)
            PInvoke.DestroyIcon(icon);
    }
}
