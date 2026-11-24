namespace Veneer.Core.Profiles;

/// <summary>
/// Names that become folder names (profiles, themes chosen by name). They come from files the user can edit and from
/// the UI, so they are checked before any path is built from them: a name is one plain folder, never a path.
/// </summary>
public static class FolderName
{
    public const int MaxLength = 64;

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why <paramref name="name"/> cannot be a folder name, or null when it can.</summary>
    public static string? Problem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "the name is empty";
        if (name.Length > MaxLength)
            return $"the name is longer than {MaxLength} characters";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "the name contains a character Windows does not allow in folder names (\\ / : * ? \" < > |)";
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.StartsWith(' '))
            return "the name cannot start with a space or end with a dot or a space";
        if (Reserved.Contains(name.Split('.')[0]))
            return $"\"{name}\" is reserved by Windows";
        return null;
    }

    public static bool IsValid(string? name) => Problem(name) is null;
}
