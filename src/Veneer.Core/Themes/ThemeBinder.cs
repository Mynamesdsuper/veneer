using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Veneer.Core.Modules;

namespace Veneer.Core.Themes;

/// <summary>Turns the merged, token-resolved JSON tree into a <see cref="Theme"/>, validating every value it reads.</summary>
internal sealed partial class ThemeBinder(string root, List<Diagnostic> diagnostics)
{
    public static readonly IReadOnlySet<string> KnownModules =
        new HashSet<string> { "logo-menu", "active-app", "clock", "volume", "network", "battery", "spacer" };

    static readonly HashSet<string> ReservedNames =
        ["con", "prn", "aux", "nul", .. Enumerable.Range(1, 9).SelectMany(i => new[] { $"com{i}", $"lpt{i}" })];

    // The id doubles as the install folder name, so keep it to safe characters.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{0,98}[a-z0-9])?$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id) && !ReservedNames.Contains(id.Split('.')[0]);

    bool HasErrors => diagnostics.Any(d => d.Severity == Severity.Error);

    public Theme? Bind(JsonObject o, IReadOnlyDictionary<string, ThemeLayer> origins)
    {
        string? id = Str(o, "id", "", required: true);
        if (id is not null && !IdPattern().IsMatch(id))
            Error("id", "use lowercase letters, digits, '.' and '-' only (for example com.example.my-theme)");
        else if (id is not null && !IsValidId(id))
            Error("id", $"\"{id.Split('.')[0]}\" is a reserved Windows name and cannot start a theme id");
        string? name = Str(o, "name", "", required: true);
        string? author = Str(o, "author", "");
        string? version = Str(o, "version", "");
        string? license = Str(o, "license", "");

        var windowsObj = Obj(o, "windows", "");
        var windows = new WindowsSettings(
            windowsObj is null ? null : Asset(windowsObj, "wallpaper", "windows", AssetPaths.WallpaperExtensions),
            windowsObj is null ? null : Enum<Appearance>(windowsObj, "appearance", "windows"),
            windowsObj is null ? null : Color(windowsObj, "accentColor", "windows"));

        var fonts = AssetMap(Obj(o, "fonts", ""), "fonts", AssetPaths.FontExtensions, key => true, "");
        var icons = AssetMap(Obj(o, "iconOverrides", ""), "iconOverrides", AssetPaths.ImageExtensions,
            key => key.StartsWith("exe:", StringComparison.Ordinal) || key.StartsWith("aumid:", StringComparison.Ordinal),
            "use \"exe:name.exe\" or \"aumid:AppUserModelId\"");
        string? iconFolder = Folder(o, "iconFolder", "");
        var surfaces = Surfaces(o["surfaces"]);

        if (HasErrors)
            return null;
        return new Theme(id!, name!, author, version, license, root, windows, fonts, surfaces, icons, iconFolder, origins);
    }

    List<Surface> Surfaces(JsonNode? node)
    {
        var list = new List<Surface>();
        if (node is null)
            return list;
        if (node is not JsonArray array)
        {
            Error("surfaces", "must be an array");
            return list;
        }

        var seen = new HashSet<string>();
        for (int i = 0; i < array.Count; i++)
        {
            string fallbackPath = $"surfaces[{i}]";
            if (array[i] is not JsonObject s)
            {
                Error(fallbackPath, "must be an object");
                continue;
            }

            string? id = Str(s, "id", fallbackPath, required: true);
            if (id is null)
                continue;
            string p = $"surfaces[id={id}]";
            if (!seen.Add(id))
            {
                Error(p, "two surfaces have the same id");
                continue;
            }

            string? type = Str(s, "type", p, required: true);
            if (type is not ("bar" or "dock"))
            {
                if (type is not null)
                    Warning(ThemeLayers.Join(p, "type"), $"unknown surface type \"{type}\" skipped (it may need a newer Veneer)");
                continue;
            }

            Edge? edge = Enum<Edge>(s, "edge", p, required: true);
            if (type == "dock" && edge == Edge.Top)
                Error(ThemeLayers.Join(p, "edge"), "a dock can sit on the bottom, left or right edge, not the top");
            string monitor = Str(s, "monitor", p) ?? "primary";
            if (monitor != "primary")
            {
                Warning(ThemeLayers.Join(p, "monitor"), $"\"{monitor}\" is not supported yet, using \"primary\"");
                monitor = "primary";
            }

            var styleObj = Obj(s, "style", p);
            string sp = ThemeLayers.Join(p, "style");
            var style = styleObj is null
                ? new SurfaceStyle(null, null, null, false, new TextStyle(null, null, null), 0, 0)
                : new SurfaceStyle(
                    Color(styleObj, "background", sp),
                    Color(styleObj, "foreground", sp),
                    Color(styleObj, "border", sp),
                    Bool(styleObj, "blur", sp) ?? false,
                    Text(styleObj, sp),
                    Num(styleObj, "padding", sp, 0, 200) ?? 0,
                    Num(styleObj, "spacing", sp, 0, 200) ?? 0);

            if (edge is not { } e)
                continue;
            list.Add(type == "bar" ? Bar(s, p, id, e, monitor, style) : Dock(s, p, id, e, monitor, style));
        }

        return list;
    }

    BarSurface Bar(JsonObject s, string p, string id, Edge edge, string monitor, SurfaceStyle style) => new(
        id, edge, monitor, style,
        Int(s, "thickness", p, 8, 200) ?? 28,
        Bool(s, "reserveSpace", p) ?? true,
        Modules(s, "start", p),
        Modules(s, "center", p),
        Modules(s, "end", p));

    DockSurface Dock(JsonObject s, string p, string id, Edge edge, string monitor, SurfaceStyle style)
    {
        var mag = Obj(s, "magnification", p);
        string mp = ThemeLayers.Join(p, "magnification");
        var ind = Obj(s, "runningIndicator", p);
        string ip = ThemeLayers.Join(p, "runningIndicator");
        return new DockSurface(
            id, edge, monitor, style,
            Int(s, "iconSize", p, 16, 256) ?? 48,
            mag is null
                ? new Magnification(false, 1.5, 3)
                : new Magnification(Bool(mag, "enabled", mp) ?? true, Num(mag, "maxScale", mp, 1, 3) ?? 1.5, Int(mag, "radius", mp, 0, 10) ?? 3),
            Num(s, "cornerRadius", p, 0, 100) ?? 0,
            Num(s, "padding", p, 0, 100) ?? 0,
            ind is null
                ? new RunningIndicator(IndicatorShape.Dot, null, 4)
                : new RunningIndicator(Enum<IndicatorShape>(ind, "shape", ip) ?? IndicatorShape.Dot, Color(ind, "color", ip), Num(ind, "size", ip, 1, 20) ?? 4),
            Bool(s, "autoHide", p) ?? false,
            StringList(s, "defaultPinned", p),
            Num(s, "margin", p, 0, 200) ?? 0,
            Bool(s, "reserveSpace", p) ?? true,
            Bool(s, "separateRunning", p) ?? true);
    }

    List<ModuleSpec> Modules(JsonObject s, string key, string p)
    {
        var list = new List<ModuleSpec>();
        string lp = ThemeLayers.Join(p, key);
        switch (s[key])
        {
            case null:
                return list;
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    string mp = $"{lp}[{i}]";
                    if (array[i] is not JsonObject m)
                    {
                        Error(mp, "must be an object like { \"module\": \"clock\" }");
                        continue;
                    }

                    string? module = Str(m, "module", mp, required: true);
                    if (module is null)
                        continue;
                    if (!KnownModules.Contains(module))
                    {
                        Warning(ThemeLayers.Join(mp, "module"), $"unknown module \"{module}\" skipped (it may need a newer Veneer)");
                        continue;
                    }

                    string? icon = m["icon"] is null ? null : Asset(m, "icon", mp, AssetPaths.ImageExtensions);
                    var styleObj = Obj(m, "style", mp);
                    string sp = ThemeLayers.Join(mp, "style");
                    var style = styleObj is null
                        ? new ModuleStyle(null, new TextStyle(null, null, null), null)
                        : new ModuleStyle(Color(styleObj, "foreground", sp), Text(styleObj, sp), Num(styleObj, "padding", sp, 0, 200));
                    ModuleOptions(module, m, mp);
                    var stateIcons = ModuleStates.IconStates.TryGetValue(module, out var states)
                        ? AssetMap(Obj(m, "icons", mp), ThemeLayers.Join(mp, "icons"), AssetPaths.ImageExtensions, key => states.Contains(key), $"allowed: {string.Join(", ", states)}")
                        : [];
                    list.Add(new ModuleSpec(module, mp, icon, stateIcons, style, (JsonObject)m.DeepClone()));
                }

                return list;
            default:
                Error(lp, "must be an array of modules");
                return list;
        }
    }

    static readonly Dictionary<string, int> FontWeights = new()
    {
        ["thin"] = 100, ["extralight"] = 200, ["light"] = 300, ["regular"] = 400, ["medium"] = 500,
        ["semibold"] = 600, ["bold"] = 700, ["extrabold"] = 800, ["black"] = 900,
    };

    TextStyle Text(JsonObject style, string p)
    {
        int? weight = null;
        if (Str(style, "fontWeight", p) is { } w)
        {
            if (FontWeights.TryGetValue(w, out int n))
                weight = n;
            else
                Error(ThemeLayers.Join(p, "fontWeight"), $"\"{w}\" is not valid (allowed: {string.Join(", ", FontWeights.Keys)})");
        }

        return new TextStyle(Str(style, "font", p), Num(style, "fontSize", p, 4, 200), weight);
    }

    /// <summary>Checks the options each module understands. A new module adds its case here and its view in the app.</summary>
    void ModuleOptions(string module, JsonObject m, string p)
    {
        switch (module)
        {
            case "clock":
                if (Str(m, "format", p) is { } format)
                {
                    try
                    {
                        _ = DateTime.Now.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        Error(ThemeLayers.Join(p, "format"), $"\"{format}\" is not a valid date/time format");
                    }
                }

                break;
            case "spacer":
                Num(m, "size", p, 0, 1000);
                break;
            case "active-app":
                Str(m, "desktopName", p);
                if (Obj(m, "names", p) is { } names && names.Any(kv => kv.Value is not JsonValue v || v.GetValueKind() != JsonValueKind.String))
                    Error(ThemeLayers.Join(p, "names"), "must map exe names to display names, e.g. { \"explorer.exe\": \"Files\" }");
                break;
            case "volume":
            case "battery":
                Bool(m, "showPercent", p);
                break;
        }
    }

    Dictionary<string, string> AssetMap(JsonObject? map, string p, IReadOnlySet<string> extensions, Func<string, bool> validKey, string keyHint)
    {
        var result = new Dictionary<string, string>();
        if (map is null)
            return result;
        foreach (var (key, _) in map)
        {
            if (!validKey(key))
            {
                Warning(ThemeLayers.Join(p, key), $"key ignored ({keyHint})");
                continue;
            }

            if (Asset(map, key, p, extensions) is { } full)
                result[key] = full;
        }

        return result;
    }

    // ----- typed readers: each one reports its own errors and returns null when the value is missing or invalid -----

    JsonObject? Obj(JsonObject o, string key, string p)
    {
        var node = o[key];
        if (node is null || node is JsonObject)
            return node as JsonObject;
        Error(ThemeLayers.Join(p, key), "must be an object");
        return null;
    }

    JsonValue? Value(JsonObject o, string key, string p, JsonValueKind kind, string expected, bool required)
    {
        string path = ThemeLayers.Join(p, key);
        var node = o[key];
        if (node is null)
        {
            if (required)
                Error(path, "is required");
            return null;
        }

        if (node is JsonValue v && (v.GetValueKind() == kind || (kind == JsonValueKind.True && v.GetValueKind() == JsonValueKind.False)))
            return v;
        Error(path, $"must be {expected}");
        return null;
    }

    string? Str(JsonObject o, string key, string p, bool required = false) =>
        Value(o, key, p, JsonValueKind.String, "a string", required)?.GetValue<string>();

    bool? Bool(JsonObject o, string key, string p) =>
        Value(o, key, p, JsonValueKind.True, "true or false", false)?.GetValue<bool>();

    double? Num(JsonObject o, string key, string p, double min, double max)
    {
        if (Value(o, key, p, JsonValueKind.Number, "a number", false) is not { } v)
            return null;
        if (!v.TryGetValue(out double n) || !double.IsFinite(n))
        {
            Error(ThemeLayers.Join(p, key), $"{v.ToJsonString()} is not a usable number");
            return null;
        }

        if (n >= min && n <= max)
            return n;
        Error(ThemeLayers.Join(p, key), $"{n} is out of range ({min}–{max})");
        return null;
    }

    int? Int(JsonObject o, string key, string p, int min, int max)
    {
        if (Num(o, key, p, min, max) is not { } n)
            return null;
        if (n == Math.Floor(n))
            return (int)n;
        Error(ThemeLayers.Join(p, key), $"{n} must be a whole number");
        return null;
    }

    ThemeColor? Color(JsonObject o, string key, string p)
    {
        if (Str(o, key, p) is not { } text)
            return null;
        if (ThemeColor.TryParse(text, out var color))
            return color;
        Error(ThemeLayers.Join(p, key), $"\"{text}\" is not a color: use #RRGGBB or #AARRGGBB");
        return null;
    }

    T? Enum<T>(JsonObject o, string key, string p, bool required = false) where T : struct, Enum
    {
        if (Str(o, key, p, required) is not { } text)
            return null;
        foreach (T value in System.Enum.GetValues<T>())
        {
            if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        string allowed = string.Join(", ", System.Enum.GetNames<T>().Select(n => n.ToLowerInvariant()));
        Error(ThemeLayers.Join(p, key), $"\"{text}\" is not valid (allowed: {allowed})");
        return null;
    }

    List<string> StringList(JsonObject o, string key, string p)
    {
        var list = new List<string>();
        string path = ThemeLayers.Join(p, key);
        switch (o[key])
        {
            case null:
                break;
            case JsonArray a when a.All(n => n is JsonValue v && v.GetValueKind() == JsonValueKind.String):
                list.AddRange(a.Select(n => n!.GetValue<string>()));
                break;
            default:
                Error(path, "must be an array of strings");
                break;
        }

        return list;
    }

    /// <summary>A theme file. Unsafe paths are errors; a missing file is only a warning and the value is dropped.</summary>
    string? Asset(JsonObject o, string key, string p, IReadOnlySet<string> extensions)
    {
        if (Str(o, key, p) is not { } relative)
            return null;
        string path = ThemeLayers.Join(p, key);
        if (!AssetPaths.TryResolve(root, relative, extensions, out string full, out string error))
        {
            Error(path, $"\"{relative}\": {error}");
            return null;
        }

        if (File.Exists(full))
            return full;
        Warning(path, $"\"{relative}\" not found in the theme folder, ignored");
        return null;
    }

    string? Folder(JsonObject o, string key, string p)
    {
        if (Str(o, key, p) is not { } relative)
            return null;
        string path = ThemeLayers.Join(p, key);
        if (!AssetPaths.TryResolve(root, relative, null, out string full, out string error))
        {
            Error(path, $"\"{relative}\": {error}");
            return null;
        }

        if (Directory.Exists(full))
            return full;
        Warning(path, $"folder \"{relative}\" not found in the theme, ignored");
        return null;
    }

    void Error(string path, string message) => diagnostics.Add(Diagnostic.Error(path, message));

    void Warning(string path, string message) => diagnostics.Add(Diagnostic.Warning(path, message));
}
