using Veneer.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Veneer.Interop;

/// <summary>The Downloads folder, where the user put it (it can be moved away from the user profile).</summary>
public static class KnownFolders
{
    public static string? Downloads()
    {
        try
        {
            return SHGetKnownFolderPath(PInvoke.FOLDERID_Downloads, 0, 0);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"downloads: folder not found: {e.Message}");
            return null;
        }
    }

    // Declared by hand: the marshaller frees the returned string with CoTaskMemFree, as the API asks.
    [System.Runtime.InteropServices.DllImport("shell32.dll", PreserveSig = false)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
    static extern string SHGetKnownFolderPath(in Guid id, uint flags, nint token);
}

/// <summary>
/// The Recycle Bin for the dock: whether it holds anything, its stock icons, emptying it and sending files to it.
/// Emptying and deleting always go through Windows' own confirmation and progress, and can be undone where Windows
/// allows it (a deleted file goes to the bin, never straight away).
/// </summary>
public static unsafe class RecycleBin
{
    /// <summary>The message the shell posts to the window given to <see cref="Watch"/> when the bin may have changed.</summary>
    public const int ChangedMessage = 0x8000 + 0x57; // WM_APP + 'W'

    const int SHCNE_ALLEVENTS = 0x7FFFFFFF, SHCNE_UPDATEIMAGE = 0x00008000;

    /// <summary>True when every drive's bin is empty. Asks every drive, so it may be slow: call it off the UI thread.</summary>
    public static bool IsEmpty()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)sizeof(SHQUERYRBINFO) };
        try
        {
            PInvoke.SHQueryRecycleBin(null, ref info).ThrowOnFailure();
            return info.i64NumItems == 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"recycle bin: cannot query it: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Asks the shell to post <see cref="ChangedMessage"/> to <paramref name="hwnd"/> on any change inside the bin,
    /// and whenever shell icons are refreshed (the shell does that when the bin turns empty or full). Returns the
    /// registrations to pass to <see cref="Unwatch"/>.
    /// </summary>
    public static uint[] Watch(nint hwnd)
    {
        var ids = new List<uint>();
        ITEMIDLIST* bin = null;
        try
        {
            PInvoke.SHGetKnownFolderIDList(PInvoke.FOLDERID_RecycleBinFolder, 0, null, out bin).ThrowOnFailure();
            Register(bin, recursive: true, SHCNE_ALLEVENTS);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"recycle bin: cannot watch it: {e.Message}");
        }
        finally
        {
            if (bin is not null)
                PInvoke.ILFree(bin);
        }

        Register(null, recursive: false, SHCNE_UPDATEIMAGE);
        return [.. ids];

        void Register(ITEMIDLIST* pidl, bool recursive, int events)
        {
            var entry = new SHChangeNotifyEntry { pidl = pidl, fRecursive = recursive };
            uint id = PInvoke.SHChangeNotifyRegister((HWND)hwnd, SHCNRF_SOURCE.SHCNRF_ShellLevel | SHCNRF_SOURCE.SHCNRF_InterruptLevel, events, ChangedMessage, 1, &entry);
            if (id != 0)
                ids.Add(id);
        }
    }

    public static void Unwatch(uint[] ids)
    {
        foreach (uint id in ids)
            PInvoke.SHChangeNotifyDeregister(id);
    }

    /// <summary>The Windows Recycle Bin icon, empty or full, at about <paramref name="size"/> pixels. Free it with <see cref="FreeIcon"/>.</summary>
    public static nint Icon(bool full, int size)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)sizeof(SHSTOCKICONINFO) };
        if (PInvoke.SHGetStockIconInfo(full ? SHSTOCKICONID.SIID_RECYCLERFULL : SHSTOCKICONID.SIID_RECYCLER, SHGSI_FLAGS.SHGSI_ICONLOCATION, ref info).Failed)
            return 0;
        string file = new((char*)&info.szPath);
        // The size goes in the low word for the large icon (the high word is the small one, not wanted here).
        if (PInvoke.SHDefExtractIcon(file, info.iIcon, 0, out var large, out var small, (uint)size).Failed)
            return 0;
        small.Dispose();
        nint icon = large.DangerousGetHandle();
        large.SetHandleAsInvalid(); // the caller owns it now and frees it with FreeIcon
        return icon;
    }

    public static void FreeIcon(nint icon)
    {
        if (icon != 0)
            PInvoke.DestroyIcon((HICON)icon);
    }

    /// <summary>Opens the Recycle Bin in File Explorer.</summary>
    public static void Open() => SystemActions.Open("shell:RecycleBinFolder");

    /// <summary>
    /// Empties the bin after Windows' own confirmation (the user can still say no). Blocks until the user answers and
    /// the files are gone: call it off the UI thread.
    /// </summary>
    public static void Empty()
    {
        var result = PInvoke.SHEmptyRecycleBin(HWND.Null, null, 0);
        if (result.Failed && result.Value != unchecked((int)0x8000FFFF)) // E_UNEXPECTED: the bin was already empty
            Log.Write($"recycle bin: not emptied: 0x{result.Value:X8}");
    }

    /// <summary>
    /// Moves <paramref name="paths"/> to the bin with Windows' own progress and questions (files too large for the bin
    /// are only deleted after Windows asks). Blocks while it works: call it off the UI thread.
    /// </summary>
    public static void Delete(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        string from = string.Join('\0', paths) + "\0\0"; // the API wants a list ended by two nulls
        fixed (char* p = from)
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = PInvoke.FO_DELETE,
                pFrom = p,
                fFlags = 0x0040, // FOF_ALLOWUNDO: to the bin, not deleted for good
            };
            int result = PInvoke.SHFileOperation(ref op);
            if (result != 0)
                Log.Write($"recycle bin: delete of {paths.Count} item(s) ended with code {result}");
        }
    }
}
