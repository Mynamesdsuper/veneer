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

    /// <summary>Sets <c>surfaces[id=<paramref name="surfaceId"/>].<paramref name="key"/></c>; null removes the key.</summary>
    public static bool TrySetSurfaceValue(string path, string surfaceId, string key, JsonNode? value, out string? error)
    {
        // The theme loader may be reading the file right now (hot reload): retry a sharing violation a few times.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return TrySetOnce(path, surfaceId, key, value, out error);
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

    static bool TrySetOnce(string path, string surfaceId, string key, JsonNode? value, out string? error)
    {
        error = null;
        JsonObject root = [];
        if (File.Exists(path))
        {
            string text = File.ReadAllText(path);
            if (JsonNode.Parse(text, documentOptions: ReadOptions) is not JsonObject parsed)
            {
                error = "the overrides file is not a JSON object; it was left untouched";
                return false;
            }

            root = parsed;
            if (HasComments(text))
                File.Copy(path, path + ".bak", overwrite: true);
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

        var surface = surfaces.OfType<JsonObject>().FirstOrDefault(s => s["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == surfaceId);
        if (surface is null)
        {
            surface = new JsonObject { ["id"] = surfaceId };
            surfaces.Add(surface);
        }

        if (value is null)
            surface.Remove(key);
        else
            surface[key] = value.DeepClone();

        Write(path, root.ToJsonString(WriteOptions));
        return true;
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
