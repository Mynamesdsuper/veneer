using System.Text.Json;
using Veneer.Core.Profiles;

namespace Veneer.Core.Themes;

/// <summary>An installed theme: its folder name (what profiles store), its folder and the name it gives itself.</summary>
public sealed record ThemeEntry(string Name, string Folder, string DisplayName);

/// <summary>
/// Finds themes by folder name in several roots, in order: the user's themes first, then the ones bundled with
/// Veneer, so an installed theme with the same folder name takes precedence.
/// </summary>
public static class ThemeFolders
{
    /// <summary>The folder of theme <paramref name="name"/>, or null when no root has it.</summary>
    public static string? Find(string name, IEnumerable<string> roots)
    {
        if (!FolderName.IsValid(name))
            return null;
        return roots.Select(r => Path.Combine(r, name)).FirstOrDefault(f => File.Exists(Path.Combine(f, ThemeLoader.ThemeFileName)));
    }

    public static IReadOnlyList<ThemeEntry> List(IEnumerable<string> roots)
    {
        var list = new List<ThemeEntry>();
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string folder in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(folder);
                if (!FolderName.IsValid(name) || !File.Exists(Path.Combine(folder, ThemeLoader.ThemeFileName))
                    || list.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(new ThemeEntry(name, folder, DisplayName(folder) ?? name));
            }
        }

        return list;
    }

    // The theme's own "name", read without loading the whole theme; null if the file cannot tell.
    static string? DisplayName(string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ThemeLoader.ThemeFileName)),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("name", out var n)
                && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
                ? n.GetString()
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
