// Phase 0 spike: which API really changes the accent color on current Windows 11.
// Throwaway code. Usage:
//   Accent read
//   Accent snapshot              (saves the originals once; refuses to overwrite an existing snapshot)
//   Accent set-api RRGGBB        (uxtheme SetUserColorPreference, ordinal 122, undocumented)
//   Accent set-reg RRGGBB        (registry values + WM_SETTINGCHANGE "ImmersiveColorSet")
//   Accent restore               (registry from snapshot + SetUserColorPreference with the original, then deletes the snapshot)
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

static class Program
{
    const string Dwm = @"Software\Microsoft\Windows\DWM";
    const string ExplorerAccent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    static readonly (string Key, string Name)[] Values =
    [
        (Dwm, "AccentColor"), (Dwm, "ColorizationColor"), (Dwm, "ColorizationAfterglow"), (Dwm, "ColorPrevalence"),
        (ExplorerAccent, "AccentPalette"), (ExplorerAccent, "AccentColorMenu"), (ExplorerAccent, "StartColorMenu"),
        (@"Control Panel\Desktop", "AutoColorization"),
    ];
    static readonly string SnapshotPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Veneer-spike", "accent-snapshot.json");

    record Saved(string Key, string Name, RegistryValueKind Kind, string? Base64, long? Number);
    record Snapshot(uint PrefStart, uint PrefAccent, List<Saved> Values);

    static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "read": Read(); return 0;
            case "snapshot": return TakeSnapshot();
            case "set-api": return SetApi(Rgb(args[1]));
            case "set-reg": SetReg(Rgb(args[1])); return 0;
            case "restore": return Restore();
            default: Console.WriteLine("read | snapshot | set-api RRGGBB | set-reg RRGGBB | restore"); return 1;
        }
    }

    static (byte R, byte G, byte B) Rgb(string hex) { uint v = Convert.ToUInt32(hex, 16); return ((byte)(v >> 16), (byte)(v >> 8), (byte)v); }
    static uint ColorRef((byte R, byte G, byte B) c) => (uint)(c.R | c.G << 8 | c.B << 16); // 0x00BBGGRR

    static void Read()
    {
        foreach (var (key, name) in Values)
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            object? v = k?.GetValue(name);
            string text = v switch
            {
                byte[] b => BitConverter.ToString(b).Replace("-", ""),
                int i => $"0x{i:X8}",
                null => "(missing)",
                _ => v.ToString()!,
            };
            Console.WriteLine($"{name,-22} {text}");
        }
        GetUserColorPreference(out var p, false);
        Console.WriteLine($"{"GetUserColorPreference",-22} start=0x{p.Start:X8} accent=0x{p.Accent:X8} (COLORREF 0x00BBGGRR)");
        var a = new UISettings().GetColorValue(UIColorType.Accent);
        Console.WriteLine($"{"UISettings.Accent",-22} #{a.R:X2}{a.G:X2}{a.B:X2}");
    }

    static int TakeSnapshot()
    {
        if (File.Exists(SnapshotPath)) { Console.WriteLine("snapshot already exists, keeping the original"); return 0; }
        var saved = new List<Saved>();
        foreach (var (key, name) in Values)
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            if (k?.GetValue(name) is not { } v) continue;
            var kind = k.GetValueKind(name);
            saved.Add(v is byte[] b ? new(key, name, kind, Convert.ToBase64String(b), null) : new(key, name, kind, null, Convert.ToInt64(v)));
        }
        GetUserColorPreference(out var p, false);
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
        File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(new Snapshot(p.Start, p.Accent, saved)));
        Console.WriteLine($"snapshot saved: {saved.Count} values");
        return 0;
    }

    static int SetApi((byte R, byte G, byte B) c)
    {
        if (!File.Exists(SnapshotPath)) { Console.WriteLine("refusing: take a snapshot first"); return 1; }
        GetUserColorPreference(out var p, false);
        p.Accent = 0xFF000000 | ColorRef(c); // Windows stores it with an opaque alpha byte
        int hr = SetUserColorPreference(ref p, true);
        Console.WriteLine($"SetUserColorPreference -> 0x{hr:X8}");
        return hr;
    }

    static void SetReg((byte R, byte G, byte B) c)
    {
        if (!File.Exists(SnapshotPath)) { Console.WriteLine("refusing: take a snapshot first"); return; }
        uint abgr = 0xFF000000 | ColorRef(c);
        uint argb = 0xC4000000 | (uint)(c.R << 16 | c.G << 8 | c.B);
        using (var k = Registry.CurrentUser.OpenSubKey(Dwm, true)!)
        {
            k.SetValue("AccentColor", unchecked((int)abgr), RegistryValueKind.DWord);
            k.SetValue("ColorizationColor", unchecked((int)argb), RegistryValueKind.DWord);
            k.SetValue("ColorizationAfterglow", unchecked((int)argb), RegistryValueKind.DWord);
        }
        using (var k = Registry.CurrentUser.OpenSubKey(ExplorerAccent, true)!)
            k.SetValue("AccentColorMenu", unchecked((int)abgr), RegistryValueKind.DWord);
        Broadcast();
        Console.WriteLine("registry written + ImmersiveColorSet broadcast");
    }

    static int Restore()
    {
        if (!File.Exists(SnapshotPath)) { Console.WriteLine("no snapshot"); return 1; }
        var s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath))!;
        foreach (var v in s.Values)
        {
            using var k = Registry.CurrentUser.CreateSubKey(v.Key, true);
            object value = v.Base64 is { } b ? Convert.FromBase64String(b) : v.Kind == RegistryValueKind.QWord ? v.Number!.Value : unchecked((int)v.Number!.Value);
            k.SetValue(v.Name, value, v.Kind);
        }
        var p = new ImmersiveColorPreference { Start = s.PrefStart, Accent = s.PrefAccent };
        int hr = SetUserColorPreference(ref p, true);
        Broadcast();
        File.Delete(SnapshotPath);
        Console.WriteLine($"restored {s.Values.Count} values, SetUserColorPreference -> 0x{hr:X8}");
        return 0;
    }

    static void Broadcast() =>
        SendMessageTimeout(0xFFFF, 0x001A /* WM_SETTINGCHANGE */, 0, "ImmersiveColorSet", 0x0002 /* SMTO_ABORTIFHUNG */, 5000, out _);

    [StructLayout(LayoutKind.Sequential)] struct ImmersiveColorPreference { public uint Start, Accent; }

    [DllImport("uxtheme.dll", EntryPoint = "GetUserColorPreference")] static extern int GetUserColorPreference(out ImmersiveColorPreference p, bool forceReload);
    [DllImport("uxtheme.dll", EntryPoint = "#122")] static extern int SetUserColorPreference(ref ImmersiveColorPreference p, bool forceCommit);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(nint hwnd, uint msg, nint wp, string lp, uint flags, uint timeout, out nint result);
}
