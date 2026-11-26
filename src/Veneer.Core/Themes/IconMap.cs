using System.Text.Json;
using Veneer.Core.Profiles;

namespace Veneer.Core.Themes;

/// <summary>
/// The exe → SVG map read by <c>tools/svg-icons</c>. Each key becomes <c>&lt;key&gt;.png</c> in a theme's iconFolder
/// (the dock looks icons up by exe name without ".exe"); each value is an SVG path relative to the icon set folder.
/// </summary>
public static class IconMap
{
    public sealed record Entry(string Name, string SvgPath);

    public sealed record Result(IReadOnlyList<Entry> Entries, IReadOnlyList<string> Problems);

    static readonly IReadOnlySet<string> SvgExtension = new HashSet<string> { ".svg" };

    /// <summary>
    /// Reads the map. Bad JSON, bad names, paths that leave <paramref name="sourceRoot"/>, links, files under a
    /// <c>links</c> folder and missing files become problems; the good entries are still returned.
    /// </summary>
    public static Result Parse(string json, string sourceRoot)
    {
        var entries = new List<Entry>();
        var problems = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            return new Result(entries, [$"the map is not valid JSON: {e.Message}"]);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new Result(entries, ["the map must be a JSON object: { \"exe name\": \"path/to/icon.svg\" }"]);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                string name = p.Name;
                if (!FolderName.IsValid(name))
                    problems.Add($"'{name}': not a valid file name");
                else if (!seen.Add(name))
                    problems.Add($"'{name}': listed twice");
                else if (p.Value.ValueKind != JsonValueKind.String)
                    problems.Add($"'{name}': the value must be an SVG path");
                else if (p.Value.GetString()!.Split('/', '\\').Contains("links", StringComparer.OrdinalIgnoreCase))
                    problems.Add($"'{name}': files under 'links' are not allowed (they are symlinks to other icons)");
                else if (!AssetPaths.TryResolve(sourceRoot, p.Value.GetString()!, SvgExtension, out string full, out string error))
                    problems.Add($"'{name}': {error}");
                else if (!File.Exists(full))
                    problems.Add($"'{name}': {p.Value.GetString()} not found");
                else
                    entries.Add(new Entry(name, full));
            }
        }

        return new Result(entries, problems);
    }

    /// <summary>The first folder at or above <paramref name="path"/> that holds a <c>.git</c> entry, or null.</summary>
    public static string? EnclosingRepository(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
        {
            string git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
        }

        return null;
    }
}
