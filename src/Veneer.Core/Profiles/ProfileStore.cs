using System.Text.Json;
using System.Text.Json.Nodes;
using Veneer.Core.Settings;

namespace Veneer.Core.Profiles;

/// <summary>
/// The user's profiles: one folder each under <c>profiles\</c> (<c>profile.json</c>, <c>overrides.json</c>,
/// <c>windows.json</c>), and the active one in <c>settings.json</c>. The <c>default</c> profile always exists.
/// Methods that change profiles return an error message for the user, or null on success.
/// </summary>
public sealed class ProfileStore(string root)
{
    public const string DefaultName = "default";
    public const string SettingsFileName = "settings.json";

    string ProfilesFolder => Path.Combine(root, "profiles");

    string SettingsPath => Path.Combine(root, SettingsFileName);

    public string Folder(string name) => Path.Combine(ProfilesFolder, name);

    public string OverridesPath(string name) => Path.Combine(Folder(name), "overrides.json");

    public string PreferencesPath(string name) => Path.Combine(Folder(name), WindowsPreferences.FileName);

    /// <summary>The profile names, sorted, <c>default</c> included even before its folder exists.</summary>
    public IReadOnlyList<string> Names()
    {
        var names = Directory.Exists(ProfilesFolder)
            ? Directory.GetDirectories(ProfilesFolder).Select(Path.GetFileName).Where(FolderName.IsValid).Select(n => n!).ToList()
            : [];
        if (!names.Contains(DefaultName, StringComparer.OrdinalIgnoreCase))
            names.Add(DefaultName);
        return names.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool Exists(string name) => Names().Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The active profile: the one in <c>settings.json</c> if it still exists, otherwise <c>default</c>.</summary>
    public string Active
    {
        get
        {
            string? name = ReadSettings()?["profile"] is JsonValue p && p.TryGetValue(out string? s) ? s : null;
            // The folder's own spelling, so the name shown matches the one on disk.
            return Names().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultName;
        }
    }

    public void SetActive(string name) => WriteSetting("profile", name);

    /// <summary>Whether the first-run offer to import the Windows taskbar's pinned apps was already made.</summary>
    public bool PinImportOffered
    {
        get => ReadSettings()?["pinImportOffered"] is JsonValue v && v.TryGetValue(out bool b) && b;
        set => WriteSetting("pinImportOffered", value);
    }

    JsonObject? ReadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"{SettingsFileName}: {e.Message}; using the defaults (profile \"{DefaultName}\")");
            return null;
        }
    }

    // Keeps the other settings; an unreadable file is replaced, as it was before there were other settings.
    void WriteSetting(string key, JsonNode? value)
    {
        var settings = ReadSettings() ?? [];
        settings[key] = value;
        AtomicFile.Write(SettingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public Profile Load(string name, List<string> warnings)
    {
        string file = Path.Combine(Folder(name), Profile.FileName);
        try
        {
            return Profile.Parse(File.Exists(file) ? File.ReadAllText(file) : null, warnings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{Profile.FileName}: {e.Message}; using the defaults");
            return new Profile();
        }
    }

    public void Save(string name, Profile profile) => AtomicFile.Write(Path.Combine(Folder(name), Profile.FileName), profile.Serialize());

    /// <summary>A new profile with the default theme and no overrides.</summary>
    public string? Create(string name)
    {
        if (CheckNew(name) is { } error)
            return error;
        Save(name, new Profile());
        return null;
    }

    /// <summary>A copy of <paramref name="source"/>: theme, overrides and Windows preferences.</summary>
    public string? Duplicate(string source, string name)
    {
        if (!Exists(source))
            return Strings.ProfileMissing(source);
        if (CheckNew(name) is { } error)
            return error;
        Directory.CreateDirectory(Folder(name));
        if (Directory.Exists(Folder(source)))
        {
            foreach (string file in Directory.GetFiles(Folder(source)))
                File.Copy(file, Path.Combine(Folder(name), Path.GetFileName(file)));
        }

        return null;
    }

    public string? Rename(string name, string newName)
    {
        if (!Exists(name))
            return Strings.ProfileMissing(name);
        bool caseOnly = string.Equals(name, newName, StringComparison.OrdinalIgnoreCase);
        if ((caseOnly ? FolderName.Problem(newName) : CheckNew(newName)) is { } error)
            return error;
        if (name == newName)
            return null;
        bool wasActive = Active == name;
        Directory.CreateDirectory(Folder(name)); // "default" may not have a folder yet
        Directory.Move(Folder(name), Folder(newName));
        if (wasActive)
            SetActive(newName);
        return null;
    }

    /// <summary>Deletes a profile and its files. The active profile and the last one cannot be deleted.</summary>
    public string? Delete(string name)
    {
        if (!Exists(name))
            return Strings.ProfileMissing(name);
        if (string.Equals(Active, name, StringComparison.OrdinalIgnoreCase))
            return Strings.ProfileDeleteActive;
        if (Names().Count == 1)
            return Strings.ProfileDeleteLast;
        if (Directory.Exists(Folder(name)))
            Directory.Delete(Folder(name), recursive: true);
        return null;
    }

    /// <summary>The profile after <paramref name="name"/> in the sorted list, wrapping around.</summary>
    public string Next(string name)
    {
        var names = Names();
        int i = names.ToList().FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return names[(i + 1) % names.Count];
    }

    string? CheckNew(string name) =>
        FolderName.Problem(name) ?? (Exists(name) ? Strings.ProfileExists(name) : null);
}
