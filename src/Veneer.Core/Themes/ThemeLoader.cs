using System.Text.Json;
using System.Text.Json.Nodes;

namespace Veneer.Core.Themes;

/// <summary>
/// Loads a theme folder: parse → schema check → theme → variant → user overrides → tokens → validate.
/// Never throws for bad input: every problem becomes a <see cref="Diagnostic"/>.
/// </summary>
public static class ThemeLoader
{
    public const int SupportedSchemaVersion = 1;
    public const long MaxJsonBytes = 1024 * 1024;
    public const string ThemeFileName = "theme.json";

    static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
        AllowDuplicateProperties = false, // a duplicate key is a parse error, reported with its line
    };

    /// <param name="themeDirectory">Folder that holds <c>theme.json</c> and the theme's assets.</param>
    /// <param name="variant">Variant to apply (e.g. <c>dark</c>); ignored when the theme does not define it.</param>
    /// <param name="overridesPath">The user's overrides file. A missing file means no overrides.</param>
    public static ThemeLoadResult Load(string themeDirectory, string? variant = null, string? overridesPath = null)
    {
        var diagnostics = new List<Diagnostic>();
        string? json = ReadText(Path.Combine(themeDirectory, ThemeFileName), ThemeFileName, Severity.Error, required: true, diagnostics);
        if (json is null)
            return new ThemeLoadResult(null, diagnostics, []);
        string? overrides = overridesPath is null ? null : ReadText(overridesPath, "overrides", Severity.Warning, required: false, diagnostics);
        return LoadWithFallback(json, themeDirectory, variant, overrides, diagnostics);
    }

    /// <summary>Same as <see cref="Load(string, string?, string?)"/> with the JSON already in memory. Assets resolve against <paramref name="themeDirectory"/>.</summary>
    public static ThemeLoadResult LoadFromJson(string themeJson, string themeDirectory, string? variant = null, string? overridesJson = null) =>
        LoadWithFallback(themeJson, themeDirectory, variant, overridesJson, []);

    /// <summary>
    /// The overrides file is shared by every theme, so a value valid for one theme can be invalid for another (an
    /// unknown token, a field out of range). When the overrides break the load, the theme loads without them and
    /// their problems become warnings: the user still gets the theme, and the file is left untouched.
    /// </summary>
    static ThemeLoadResult LoadWithFallback(string json, string themeDirectory, string? variant, string? overridesJson, List<Diagnostic> diagnostics)
    {
        var first = new List<Diagnostic>(diagnostics);
        var result = Load(json, themeDirectory, variant, overridesJson, first);
        if (result.Success || overridesJson is null)
            return result;

        var retry = new List<Diagnostic>(diagnostics);
        var without = Load(json, themeDirectory, variant, null, retry);
        if (!without.Success)
            return without; // the theme itself is broken: report its own errors

        retry.Add(Diagnostic.Warning("overrides", "your customizations were not applied because of the problems below; the theme loaded without them"));
        retry.AddRange(first.Skip(diagnostics.Count).Where(d => d.Severity == Severity.Error).Select(d => d with { Severity = Severity.Warning }));
        return without with { Diagnostics = retry };
    }

    static ThemeLoadResult Load(string json, string themeDirectory, string? variant, string? overridesJson, List<Diagnostic> diagnostics)
    {
        var orphans = new List<string>();
        try
        {
            if (Parse(json, ThemeFileName, Severity.Error, diagnostics) is not { } root)
                return Fail();

            if (root["schemaVersion"] is not JsonValue sv || sv.GetValueKind() != JsonValueKind.Number)
            {
                diagnostics.Add(Diagnostic.Error("schemaVersion", "is required (a number)"));
                return Fail();
            }

            if (!sv.TryGetValue(out double version) || version != SupportedSchemaVersion)
            {
                diagnostics.Add(Diagnostic.Error("schemaVersion", $"version {sv.ToJsonString()} is not supported: this Veneer reads version {SupportedSchemaVersion}. Update Veneer or ask the theme author."));
                return Fail();
            }

            var variants = root["variants"];
            root.Remove("variants");
            var origins = new Dictionary<string, ThemeLayer>();
            ThemeLayers.Mark(root, "", ThemeLayer.Theme, origins);

            if (variants is not null and not JsonObject)
                diagnostics.Add(Diagnostic.Error("variants", "must be an object"));
            else if (variant is not null && variants is JsonObject all && all[variant] is { } selected)
            {
                if (selected is JsonObject v)
                    ThemeLayers.Merge(root, v, "", ThemeLayer.Variant, origins, orphans, diagnostics);
                else
                    diagnostics.Add(Diagnostic.Error($"variants.{variant}", "must be an object"));
            }

            if (overridesJson is not null && Parse(overridesJson, "overrides", Severity.Warning, diagnostics) is { } user)
            {
                user.Remove("variants");
                ThemeLayers.Merge(root, user, "", ThemeLayer.User, origins, orphans, diagnostics);
            }

            ThemeLayers.ResolveTokens(root, diagnostics);
            if (HasErrors())
                return Fail();

            var theme = new ThemeBinder(Path.GetFullPath(themeDirectory), diagnostics).Bind(root, origins);
            return new ThemeLoadResult(HasErrors() ? null : theme, diagnostics, orphans);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Safety net: a theme must never crash Veneer.
            diagnostics.Add(Diagnostic.Error(ThemeFileName, $"could not be read: {e.Message}"));
            return Fail();
        }

        bool HasErrors() => diagnostics.Any(d => d.Severity == Severity.Error);

        ThemeLoadResult Fail() => new(null, diagnostics, orphans);
    }

    static JsonObject? Parse(string json, string label, Severity severity, List<Diagnostic> diagnostics)
    {
        string suffix = severity == Severity.Warning ? " The file was ignored." : "";
        try
        {
            if (JsonNode.Parse(json, documentOptions: JsonOptions) is JsonObject o)
                return o;

            diagnostics.Add(new Diagnostic(severity, label, "must be a JSON object { ... }." + suffix));
        }
        catch (JsonException e)
        {
            diagnostics.Add(new Diagnostic(severity, label, $"invalid JSON at line {e.LineNumber + 1}, column {e.BytePositionInLine + 1}." + suffix));
        }
        catch (ArgumentException e)
        {
            diagnostics.Add(new Diagnostic(severity, label, $"invalid JSON: {e.Message}" + suffix));
        }

        return null;
    }

    static string? ReadText(string path, string label, Severity severity, bool required, List<Diagnostic> diagnostics)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                if (required)
                    diagnostics.Add(new Diagnostic(severity, label, $"not found ({path})"));
                return null;
            }

            if (AssetPaths.IsLink(path))
                diagnostics.Add(new Diagnostic(severity, label, "links (symlinks, junctions) are not allowed"));
            else if (info.Length > MaxJsonBytes)
                diagnostics.Add(new Diagnostic(severity, label, $"is larger than {MaxJsonBytes / 1024} KB"));
            else
                return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // ArgumentException / NotSupportedException: an empty or malformed path.
            diagnostics.Add(new Diagnostic(severity, label, $"could not be read: {e.Message}"));
        }

        return null;
    }
}
