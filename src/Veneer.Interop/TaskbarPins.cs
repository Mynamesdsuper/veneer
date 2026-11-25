using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using Veneer.Core;

namespace Veneer.Interop;

/// <summary>
/// An app pinned to the Windows taskbar: its AppUserModelID (Store apps, and desktop apps whose shortcut sets one),
/// the file its shortcut starts and that file's arguments (null for Store apps).
/// </summary>
public sealed record TaskbarPin(string? Aumid, string? TargetPath, string? Arguments);

/// <summary>Reads the Windows taskbar's pinned apps, in taskbar order. Read-only: nothing on the system changes.</summary>
public static class TaskbarPins
{
    const string TaskbandKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    static string PinnedFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>
    /// The pinned apps. Store apps have no shortcut file, so the list comes from the taskbar's own record in the
    /// registry (<c>Taskband\FavoritesResolve</c>: shell links saved one after another, each after its 4-byte size).
    /// When that cannot be read, the shortcut files of the pinned folder (desktop apps only) are the fallback.
    /// </summary>
    public static List<TaskbarPin> Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbandKey);
            if (key?.GetValue("FavoritesResolve") is byte[] data && FromRecord(data) is { Count: > 0 } pins)
                return pins;
            Log.Write("taskbar pins: no readable FavoritesResolve, using the pinned shortcut files");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"taskbar pins: registry record not read ({e.Message}), using the pinned shortcut files");
        }

        return FromFolder();
    }

    static List<TaskbarPin>? FromRecord(byte[] data)
    {
        var pins = new List<TaskbarPin>();
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            int size = BitConverter.ToInt32(data, pos);
            if (size <= 0 || size > data.Length - pos - 4)
                return null; // not the layout we know: trust none of it
            var link = NewLink();
            try
            {
                var stream = SHCreateMemStream(data[(pos + 4)..(pos + 4 + size)], (uint)size);
                try
                {
                    ((IPersistStream)link).Load(stream);
                }
                finally
                {
                    Marshal.ReleaseComObject(stream);
                }

                if (Describe(link) is { } pin)
                    pins.Add(pin);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }

            pos += 4 + size;
        }

        return pins;
    }

    static List<TaskbarPin> FromFolder()
    {
        var pins = new List<TaskbarPin>();
        try
        {
            if (!Directory.Exists(PinnedFolder))
                return pins;
            foreach (string file in Directory.EnumerateFiles(PinnedFolder, "*.lnk").Order(StringComparer.OrdinalIgnoreCase))
            {
                var link = NewLink();
                try
                {
                    ((IPersistFile)link).Load(file, 0);
                    if (Describe(link) is { } pin)
                        pins.Add(pin);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    Log.Write($"taskbar pins: {Path.GetFileName(file)} not read: {e.Message}");
                }
                finally
                {
                    Marshal.ReleaseComObject(link);
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Log.Write($"taskbar pins: pinned folder not read: {e.Message}");
        }

        return pins;
    }

    static TaskbarPin? Describe(IShellLinkW link, int depth = 0)
    {
        // The shortcut's own id (desktop apps that set one, e.g. Discord), else the target's (Store apps live in the
        // Apps folder, where every item has one).
        string? aumid = ShellItems.Aumid((Windows.Win32.UI.Shell.PropertiesSystem.IPropertyStore)link);
        if (aumid is null)
        {
            link.GetIDList(out nint pidl);
            try
            {
                if (pidl != 0 && SHGetPropertyStoreFromIDList(pidl, 0, typeof(Windows.Win32.UI.Shell.PropertiesSystem.IPropertyStore).GUID, out object store) == 0)
                {
                    try
                    {
                        aumid = ShellItems.Aumid((Windows.Win32.UI.Shell.PropertiesSystem.IPropertyStore)store);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(store);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        var text = new StringBuilder(1024);
        link.GetPath(text, text.Capacity, 0, 0);
        string? target = text.Length > 0 ? text.ToString() : null;
        text.Clear();
        link.GetArguments(text, text.Capacity);
        string? arguments = text.Length > 0 ? text.ToString() : null;

        // The taskbar's record points at the shortcut files of the pinned folder: read the shortcut itself.
        if (depth == 0 && target is not null && target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
        {
            var inner = NewLink();
            try
            {
                ((IPersistFile)inner).Load(target, 0);
                if (Describe(inner, depth + 1) is { } pin)
                    return pin with { Aumid = pin.Aumid ?? aumid };
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Write($"taskbar pins: {Path.GetFileName(target)} not read: {e.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(inner);
            }

            target = null; // a shortcut path is no app; keep the id if there is one
            arguments = null;
        }

        return aumid is null && target is null ? null : new TaskbarPin(aumid, target, arguments);
    }

    static IShellLinkW NewLink() =>
        (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        // Declared up to GetArguments, in the SDK's vtable order; the setters are never called.
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, nint findData, uint flags);

        void GetIDList(out nint pidl);

        void SetIDList(nint pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int size);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
    }

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistStream
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load(IStream stream);
    }

    [DllImport("shlwapi.dll")]
    [return: MarshalAs(UnmanagedType.Interface)]
    static extern IStream SHCreateMemStream(byte[] data, uint size);

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreFromIDList(nint pidl, int flags, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object store);
}
