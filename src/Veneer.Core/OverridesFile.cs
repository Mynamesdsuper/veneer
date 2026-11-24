using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Veneer.Core;

/// <summary>
/// Writes single values into the user's overrides file (same shape as a theme, see docs/theme-format.md).
/// Every other value in the file is kept. A file that cannot be parsed is never overwritten. JSON comments cannot
/// survive a rewrite, so a file with comments is first copied to <c>overrides.json.bak</c>.
/// </summary>
public static class OverridesFile
{
    static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    static readonly JsonDocumentOptions NoComments = ReadOptions with { CommentHandling = JsonCommentHandling.Disallow };
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Sets <c>surfaces[id=<paramref name="surfaceId"/>].<paramref name="key"/></c>; null removes the key (the theme's
    /// value applies again). A dotted key (<c>magnification.maxScale</c>) sets one value inside an object, so the
    /// theme's other values in that object still apply. Objects left empty by a removal are removed too.
    /// </summary>
    public static bool TrySetSurfaceValue(string path, string surfaceId, string key, JsonNode? value, out string? error) =>
        TryEdit(path, surfaces => value is null ? RemoveValue(surfaces, surfaceId, key) : SetValue(surfaces, surfaceId, key, value), out error);

    /// <summary>Removes every customization of surface <paramref name="surfaceId"/> (e.g. one the theme no longer has).</summary>
    public static bool TryRemoveSurface(string path, string surfaceId, out string? error) =>
        TryEdit(path, surfaces =>
        {
            var matches = Surfaces(surfaces, surfaceId).ToList();
            foreach (var s in matches)
                surfaces.Remove(s);
            return matches.Count > 0;
        }, out error);

    // edit returns whether it changed anything: a no-op never rewrites the file (and never strips its comments).
    static bool TryEdit(string path, Func<JsonArray, bool> edit, out string? error)
    {
        // The theme loader may be reading the file right now (hot reload): retry a sharing violation a few times.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return TryEditOnce(path, edit, out error);
            }
            catch (IOException e) when (attempt < 3)
            {
                Log.Write($"overrides busy ({e.Message}), retrying");
                Thread.Sleep(100 * attempt);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error = e.Message;
                return false;
            }
        }
    }

    static bool TryEditOnce(string path, Func<JsonArray, bool> edit, out string? error)
    {
        error = null;
        JsonObject root = [];
        string? text = null;
        if (File.Exists(path))
        {
            text = File.ReadAllText(path);
            if (JsonNode.Parse(text, documentOptions: ReadOptions) is not JsonObject parsed)
            {
                error = "the overrides file is not a JSON object; it was left untouched";
                return false;
            }

            root = parsed;
        }

        if (root["surfaces"] is not JsonArray surfaces)
        {
            if (root["surfaces"] is not null)
            {
                error = "\"surfaces\" in the overrides file is not an array; it was left untouched";
                return false;
            }

            root["surfaces"] = surfaces = [];
        }

        if (!edit(surfaces))
            return true;
        if (text is not null && HasComments(text))
            File.Copy(path, path + ".bak", overwrite: true);
        Write(path, root.ToJsonString(WriteOptions));
        return true;
    }

    static string? IdOf(JsonObject surface) =>
        surface["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    static IEnumerable<JsonObject> Surfaces(JsonArray surfaces, string surfaceId) => surfaces.OfType<JsonObject>().Where(s => IdOf(s) == surfaceId);

    static bool SetValue(JsonArray surfaces, string surfaceId, string key, JsonNode value)
    {
        var surface = Surfaces(surfaces, surfaceId).FirstOrDefault();
        if (surface is null)
        {
            surface = new JsonObject { ["id"] = surfaceId };
            surfaces.Add(surface);
        }

        string[] parts = key.Split('.');
        var parent = surface;
        foreach (string part in parts[..^1])
        {
            if (parent[part] is not JsonObject child)
            {
                child = [];
                parent[part] = child; // replaces a non-object value: the user's new choice wins
            }

            parent = child;
        }

        parent[parts[^1]] = value.DeepClone();
        return true;
    }

    // From every entry with that id: the loader merges them all, so a value left in a second entry would still apply.
    static bool RemoveValue(JsonArray surfaces, string surfaceId, string key)
    {
        bool changed = false;
        string[] parts = key.Split('.');
        foreach (var surface in Surfaces(surfaces, surfaceId).ToList())
        {
            var parents = new List<JsonObject> { surface };
            foreach (string part in parts[..^1])
            {
                if (parents[^1][part] is not JsonObject child)
                    break;
                parents.Add(child);
            }

            if (parents.Count != parts.Length || !parents[^1].Remove(parts[^1]))
                continue; // nothing set there
            changed = true;
            for (int i = parents.Count - 1; i > 0 && parents[i].Count == 0; i--)
                parents[i - 1].Remove(parts[i - 1]);
            if (surface.Count == 1)
                surfaces.Remove(surface); // only the id is left
        }

        return changed;
    }

    static bool HasComments(string text)
    {
        try
        {
            JsonDocument.Parse(text, NoComments).Dispose();
            return false;
        }
        catch (JsonException)
        {
            return true; // it parsed with comments skipped, so comments are what failed here
        }
    }

    // Write a temporary file next to the target, flush it to disk, then swap it in: a crash or power loss never
    // leaves a half-written or empty overrides file.
    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temp, path, destinationBackupFileName: null);
            else
                File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
