using System.Text.Json;
using Microsoft.Win32;
using Veneer.Core;

namespace Veneer.Interop;

/// <summary>
/// "Start with Windows": a value named Veneer in <c>HKCU\...\Run</c>, off by default. Before the first change the
/// value as it was (or its absence) is saved in the state folder; turning it off puts that back.
/// </summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Veneer";

    sealed record Snapshot(string? Value);

    static string SnapshotPath => Path.Combine(AppPaths.State, "autostart.json");

    /// <summary>The command Windows runs at sign-in: this executable.</summary>
    static string Command => $"\"{Environment.ProcessPath}\"";

    static string? Current
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    public static bool IsEnabled => string.Equals(Current, Command, StringComparison.OrdinalIgnoreCase);

    public static void Set(bool enabled)
    {
        if (enabled == IsEnabled)
            return;
        if (enabled)
        {
            if (!File.Exists(SnapshotPath))
                AtomicFile.Write(SnapshotPath, JsonSerializer.Serialize(new Snapshot(Current))); // not ours: IsEnabled was false

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, Command, RegistryValueKind.String);
            Log.Write("autostart: on");
            return;
        }

        Restore();
    }

    /// <summary>Puts the value back as it was before Veneer changed it, and forgets the snapshot.</summary>
    public static void Restore()
    {
        string? original = null;
        try
        {
            if (File.Exists(SnapshotPath))
                original = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath))?.Value;
        }
        catch (JsonException e)
        {
            Log.Write($"autostart: snapshot damaged ({e.Message}); removing Veneer's value only");
        }

        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (original is null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            else
                key.SetValue(ValueName, original, RegistryValueKind.String);
        }

        File.Delete(SnapshotPath);
        Log.Write("autostart: off");
    }
}
