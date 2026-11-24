using System.Text.Json;
using System.Text.Json.Nodes;

namespace Veneer.Core.Themes;

/// <summary>
/// Layer merging on the raw JSON tree (theme → variant → user overrides) and <c>$token</c> resolution.
/// Rules: objects merge key by key; <c>surfaces</c> merge by <c>id</c>; every other array and value is replaced.
/// An explicit <c>null</c> removes the value, so the default applies.
/// </summary>
internal static class ThemeLayers
{
    public static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    static string? SurfaceId(JsonNode? node) =>
        node is JsonObject o && o["id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    static string ItemPath(string path, JsonArray array, int index, JsonNode? item) =>
        path == "surfaces" && SurfaceId(item) is { } id ? $"surfaces[id={id}]" : $"{path}[{index}]";

    /// <summary>Records <paramref name="layer"/> as the origin of every leaf under <paramref name="path"/>.</summary>
    public static void Mark(JsonNode? node, string path, ThemeLayer layer, Dictionary<string, ThemeLayer> origins)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, child) in o)
                    Mark(child, Join(path, key), layer, origins);
                break;
            case JsonArray a when path == "surfaces":
                for (int i = 0; i < a.Count; i++)
                    Mark(a[i], ItemPath(path, a, i, a[i]), layer, origins);
                break;
            default:
                origins[path] = layer;
                break;
        }
    }

    public static void Merge(JsonObject target, JsonObject overlay, string path, ThemeLayer layer,
        Dictionary<string, ThemeLayer> origins, List<string> orphans, List<Diagnostic> diagnostics)
    {
        foreach (var (key, value) in overlay)
        {
            string p = Join(path, key);
            if (path.Length == 0 && key == "surfaces" && value is JsonArray items && target["surfaces"] is JsonArray existing)
            {
                MergeSurfaces(existing, items, layer, origins, orphans, diagnostics);
                continue;
            }

            if (value is JsonObject childOverlay && target[key] is JsonObject childTarget)
            {
                Merge(childTarget, childOverlay, p, layer, origins, orphans, diagnostics);
                continue;
            }

            // Only a replaced object or array has origins below it. Scanning for every scalar key made a crafted
            // theme quadratic (minutes for a 1 MB file).
            if (target[key] is JsonObject or JsonArray)
            {
                string dot = p + ".", bracket = p + "[";
                foreach (string stale in origins.Keys.Where(k => k == p || k.StartsWith(dot, StringComparison.Ordinal) || k.StartsWith(bracket, StringComparison.Ordinal)).ToList())
                    origins.Remove(stale);
            }
            else
            {
                origins.Remove(p);
            }

            target[key] = value?.DeepClone();
            if (value is not null)
                Mark(target[key], p, layer, origins);
        }
    }

    static void MergeSurfaces(JsonArray existing, JsonArray items, ThemeLayer layer,
        Dictionary<string, ThemeLayer> origins, List<string> orphans, List<Diagnostic> diagnostics)
    {
        var byId = new Dictionary<string, JsonObject>();
        foreach (var s in existing.OfType<JsonObject>())
        {
            if (SurfaceId(s) is { } existingId)
                byId.TryAdd(existingId, s);
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (SurfaceId(items[i]) is not { } id)
            {
                diagnostics.Add(Diagnostic.Warning($"surfaces[{i}]", $"{layer} entry without an \"id\" ignored: surfaces are matched by id"));
                continue;
            }

            string sp = $"surfaces[id={id}]";
            if (byId.TryGetValue(id, out var match))
            {
                Merge(match, (JsonObject)items[i]!, sp, layer, origins, orphans, diagnostics);
            }
            else if (layer == ThemeLayer.User)
            {
                orphans.Add(sp);
                diagnostics.Add(Diagnostic.Warning(sp, "saved customization not applied: the theme has no surface with this id (it is kept)"));
            }
            else
            {
                var copy = items[i]!.DeepClone();
                existing.Add(copy);
                byId[id] = (JsonObject)copy;
                Mark(copy, sp, layer, origins);
            }
        }
    }

    /// <summary>
    /// Replaces every string value <c>"$name"</c> with <c>tokens.name</c>. <c>"$$text"</c> is the literal <c>"$text"</c>.
    /// Unknown tokens are errors. Tokens cannot reference other tokens.
    /// </summary>
    public static void ResolveTokens(JsonObject root, List<Diagnostic> diagnostics)
    {
        JsonObject tokens = [];
        switch (root["tokens"])
        {
            case null:
                break;
            case JsonObject t:
                tokens = t;
                foreach (var (name, value) in t)
                {
                    if (value is not JsonValue)
                        diagnostics.Add(Diagnostic.Error($"tokens.{name}", "a token must be a string, number or boolean"));
                    else if (IsReference(value, out _))
                        diagnostics.Add(Diagnostic.Error($"tokens.{name}", "a token cannot reference another token"));
                }
                break;
            default:
                diagnostics.Add(Diagnostic.Error("tokens", "must be an object"));
                return;
        }

        Walk(root, "", tokens, diagnostics);
    }

    static void Walk(JsonNode? node, string path, JsonObject tokens, List<Diagnostic> diagnostics)
    {
        if (node is JsonObject o)
        {
            foreach (string key in o.Select(kv => kv.Key).ToList())
            {
                if (path.Length == 0 && key == "tokens")
                    continue;
                var child = o[key];
                var resolved = Resolve(child, Join(path, key), tokens, diagnostics);
                if (!ReferenceEquals(resolved, child))
                    o[key] = resolved; // re-assigning the same node would throw: it already has a parent
            }
        }
        else if (node is JsonArray a)
        {
            for (int i = 0; i < a.Count; i++)
            {
                var child = a[i];
                var resolved = Resolve(child, ItemPath(path, a, i, child), tokens, diagnostics);
                if (!ReferenceEquals(resolved, child))
                    a[i] = resolved;
            }
        }
    }

    static JsonNode? Resolve(JsonNode? node, string path, JsonObject tokens, List<Diagnostic> diagnostics)
    {
        if (node is not JsonValue v)
        {
            Walk(node, path, tokens, diagnostics);
            return node;
        }

        if (v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().StartsWith("$$", StringComparison.Ordinal))
            return JsonValue.Create(v.GetValue<string>()[1..]);
        if (!IsReference(v, out string name))
            return node;
        if (tokens[name] is JsonValue token)
            return token.DeepClone();

        diagnostics.Add(Diagnostic.Error(path, $"unknown token \"${name}\""));
        return node;
    }

    static bool IsReference(JsonNode? node, out string name)
    {
        name = "";
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.String)
            return false;
        string s = v.GetValue<string>();
        if (s.Length < 2 || s[0] != '$' || s[1] == '$')
            return false;
        name = s[1..];
        return true;
    }
}
