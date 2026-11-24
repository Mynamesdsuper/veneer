using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class ThemeLoaderTests
{
    static ThemeLoadResult LoadSample(string? variant = null, string? overrides = null, string json = TempTheme.Sample)
    {
        using var t = new TempTheme(json, TempTheme.SampleAssets);
        string? overridesPath = overrides is null ? null : t.Write("../" + Path.GetFileName(t.Root) + ".overrides.json", overrides);
        try
        {
            return ThemeLoader.Load(t.Root, variant, overridesPath);
        }
        finally
        {
            if (overridesPath is not null)
                File.Delete(overridesPath);
        }
    }

    static string Replace(string from, string to)
    {
        Assert.Contains(from, TempTheme.Sample);
        return TempTheme.Sample.Replace(from, to);
    }

    [Fact]
    public void Sample_loads_without_diagnostics()
    {
        var r = LoadSample();

        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Empty(r.Diagnostics);
        var theme = r.Theme!;
        Assert.Equal("com.example.sample", theme.Id);
        Assert.Equal(Appearance.Light, theme.Windows.Appearance);
        Assert.Equal(new ThemeColor(0xFF, 0x0A, 0x84, 0xFF), theme.Windows.AccentColor);
        Assert.EndsWith(Path.Combine("assets", "wallpaper.jpg"), theme.Windows.Wallpaper);
        Assert.True(Path.IsPathRooted(theme.Fonts["ui"]));
        Assert.Contains("exe:code.exe", theme.IconOverrides.Keys);

        var bar = Assert.IsType<BarSurface>(theme.Surfaces[0]);
        Assert.Equal(Edge.Top, bar.Edge);
        Assert.Equal(24, bar.Thickness);
        Assert.Equal(["logo-menu", "active-app"], bar.Start.Select(m => m.Module));
        Assert.NotNull(bar.Start[0].Icon);
        Assert.True(bar.Start[1].Options["bold"]!.GetValue<bool>());
        Assert.Equal("ddd d MMM HH:mm", bar.End[1].Options["format"]!.GetValue<string>());

        var dock = Assert.IsType<DockSurface>(theme.Surfaces[1]);
        Assert.Equal(48, dock.IconSize);
        Assert.Equal(new Magnification(true, 1.6, 3), dock.Magnification);
        Assert.Equal(["explorer.exe", "ms-settings:"], dock.DefaultPinned);
    }

    [Fact]
    public void Tokens_are_resolved()
    {
        var theme = LoadSample().Theme!;
        var bar = (BarSurface)theme.Surfaces[0];
        var dock = (DockSurface)theme.Surfaces[1];

        Assert.Equal(new ThemeColor(0x99, 0xF5, 0xF5, 0xF5), bar.Style.Background);
        Assert.Equal(new ThemeColor(0xFF, 0x1D, 0x1D, 0x1F), dock.RunningIndicator.Color);
    }

    [Fact]
    public void Variant_overrides_tokens_and_settings()
    {
        var theme = LoadSample("dark").Theme!;
        var bar = (BarSurface)theme.Surfaces[0];

        Assert.Equal(new ThemeColor(0x99, 0x20, 0x20, 0x22), bar.Style.Background);
        Assert.EndsWith("wallpaper-dark.jpg", theme.Windows.Wallpaper);
        Assert.Equal(ThemeLayer.Variant, theme.OriginOf("windows.wallpaper"));
        Assert.Equal(ThemeLayer.Theme, theme.OriginOf("windows.accentColor"));
    }

    [Fact]
    public void Unknown_variant_is_ignored()
    {
        var r = LoadSample("sepia");

        Assert.True(r.Success);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void User_overrides_apply_last_and_match_surfaces_by_id()
    {
        const string overrides = """{ "surfaces": [ { "id": "dock", "iconSize": 56 } ], "tokens": { "text": "#FF000000" } }""";
        var r = LoadSample("dark", overrides);

        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var theme = r.Theme!;
        var dock = (DockSurface)theme.Surfaces[1];
        Assert.Equal(56, dock.IconSize);
        Assert.Equal(18, dock.CornerRadius); // untouched fields stay from the theme
        Assert.Equal(new ThemeColor(0xFF, 0, 0, 0), dock.RunningIndicator.Color); // user token wins over the dark variant
        Assert.Equal(ThemeLayer.User, theme.OriginOf("surfaces[id=dock].iconSize"));
        Assert.Equal(ThemeLayer.Theme, theme.OriginOf("surfaces[id=dock].cornerRadius"));
        Assert.Equal(ThemeLayer.User, theme.OriginOf("tokens.text"));
    }

    [Fact]
    public void User_null_resets_to_default()
    {
        var r = LoadSample(overrides: """{ "surfaces": [ { "id": "dock", "cornerRadius": null } ] }""");

        Assert.Equal(0, ((DockSurface)r.Theme!.Surfaces[1]).CornerRadius);
        Assert.Null(r.Theme.OriginOf("surfaces[id=dock].cornerRadius"));
    }

    [Fact]
    public void Arrays_other_than_surfaces_are_replaced()
    {
        var r = LoadSample(overrides: """{ "surfaces": [ { "id": "menubar", "end": [ { "module": "clock" } ] } ] }""");

        Assert.Equal(["clock"], ((BarSurface)r.Theme!.Surfaces[0]).End.Select(m => m.Module));
    }

    [Fact]
    public void Override_for_a_removed_surface_is_an_orphan_and_not_applied()
    {
        var r = LoadSample(overrides: """{ "surfaces": [ { "id": "sidebar", "thickness": 40 } ] }""");

        Assert.True(r.Success);
        Assert.Equal(["surfaces[id=sidebar]"], r.OrphanOverrides);
        Assert.Contains(r.Warnings, d => d.Path == "surfaces[id=sidebar]");
        Assert.Equal(2, r.Theme!.Surfaces.Count);
    }

    [Fact]
    public void Broken_overrides_file_is_ignored_with_a_warning()
    {
        var r = LoadSample(overrides: "{ not json");

        Assert.True(r.Success);
        var w = Assert.Single(r.Warnings);
        Assert.Equal("overrides", w.Path);
        Assert.Contains("line 1", w.Message);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, ")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{ \"a\": 1, \"a\": 2 }")]
    public void Malformed_json_is_an_error_not_a_crash(string json)
    {
        using var t = new TempTheme(json);
        var r = ThemeLoader.Load(t.Root);

        Assert.False(r.Success);
        Assert.Contains(r.Errors, d => d.Path == "theme.json");
    }

    [Fact]
    public void Missing_theme_file_is_an_error()
    {
        using var t = new TempTheme(json: null);
        var r = ThemeLoader.Load(t.Root);

        Assert.False(r.Success);
        Assert.Contains("not found", Assert.Single(r.Errors).Message);
    }

    [Fact]
    public void Too_large_theme_file_is_rejected()
    {
        using var t = new TempTheme(TempTheme.Sample + new string(' ', (int)ThemeLoader.MaxJsonBytes));
        Assert.Contains("larger", Assert.Single(ThemeLoader.Load(t.Root).Errors).Message);
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 2,", "not supported")]
    [InlineData("\"schemaVersion\": 1,", "", "is required")]
    public void Schema_version_is_checked(string from, string to, string message)
    {
        var r = LoadSample(json: Replace(from, to));

        Assert.False(r.Success);
        var e = Assert.Single(r.Errors);
        Assert.Equal("schemaVersion", e.Path);
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("\"iconSize\": 48", "\"iconSize\": 900", "surfaces[id=dock].iconSize", "out of range")]
    [InlineData("\"iconSize\": 48", "\"iconSize\": 48.5", "surfaces[id=dock].iconSize", "whole number")]
    [InlineData("\"iconSize\": 48", "\"iconSize\": \"big\"", "surfaces[id=dock].iconSize", "must be a number")]
    [InlineData("\"maxScale\": 1.6", "\"maxScale\": 9", "surfaces[id=dock].magnification.maxScale", "out of range")]
    [InlineData("\"background\": \"#66FFFFFF\"", "\"background\": \"white\"", "surfaces[id=dock].style.background", "not a color")]
    [InlineData("\"type\": \"dock\", \"edge\": \"bottom\"", "\"type\": \"dock\", \"edge\": \"top\"", "surfaces[id=dock].edge", "not the top")]
    [InlineData("\"edge\": \"top\", \"monitor\"", "\"edge\": \"up\", \"monitor\"", "surfaces[id=menubar].edge", "allowed: top, bottom, left, right")]
    [InlineData("\"appearance\": \"light\"", "\"appearance\": \"sepia\"", "windows.appearance", "not valid")]
    [InlineData("\"id\": \"com.example.sample\"", "\"id\": \"My Theme!\"", "id", "lowercase")]
    [InlineData("\"id\": \"com.example.sample\"", "\"id\": \"con\"", "id", "reserved")]
    [InlineData("\"name\": \"Sample\",", "", "name", "is required")]
    [InlineData("\"id\": \"dock\", \"type\"", "\"id\": \"menubar\", \"type\"", "surfaces[id=menubar]", "same id")]
    [InlineData("\"background\": \"$glass\"", "\"background\": \"$frosted\"", "surfaces[id=menubar].style.background", "unknown token \"$frosted\"")]
    [InlineData("\"defaultPinned\": [ \"explorer.exe\"", "\"defaultPinned\": [ 1", "surfaces[id=dock].defaultPinned", "array of strings")]
    public void Invalid_values_give_readable_errors(string from, string to, string path, string message)
    {
        var r = LoadSample(json: Replace(from, to));

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == path && e.Message.Contains(message));
    }

    [Fact]
    public void Token_cannot_reference_a_token()
    {
        var r = LoadSample(json: Replace("\"text\": \"#FF1D1D1F\"", "\"text\": \"$glass\""));
        Assert.Contains(r.Errors, e => e.Path == "tokens.text");
    }

    [Fact]
    public void Double_dollar_is_a_literal()
    {
        var r = LoadSample(json: Replace("\"format\": \"ddd d MMM HH:mm\"", "\"format\": \"$$HH\""));
        Assert.Equal("$HH", ((BarSurface)r.Theme!.Surfaces[0]).End[1].Options["format"]!.GetValue<string>());
    }

    [Fact]
    public void Unknown_surface_types_and_modules_are_skipped_with_a_warning()
    {
        string json = Replace("\"surfaces\": [", "\"surfaces\": [ { \"id\": \"w\", \"type\": \"widget\", \"edge\": \"left\" },")
            .Replace("{ \"module\": \"volume\" }", "{ \"module\": \"weather\" }");
        var r = LoadSample(json: json);

        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        Assert.Equal(2, r.Theme!.Surfaces.Count);
        Assert.Equal(["clock"], ((BarSurface)r.Theme.Surfaces[0]).End.Select(m => m.Module));
        Assert.Contains(r.Warnings, w => w.Path == "surfaces[id=w].type");
        Assert.Contains(r.Warnings, w => w.Path == "surfaces[id=menubar].end[0].module");
    }

    [Fact]
    public void Unknown_fields_are_ignored_for_forward_compatibility()
    {
        var r = LoadSample(json: Replace("\"license\": \"MIT\",", "\"license\": \"MIT\", \"widgets\": [ { \"x\": 1 } ],"));
        Assert.True(r.Success);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [InlineData("../outside.jpg", "'..'")]
    [InlineData("assets/../../outside.jpg", "'..'")]
    [InlineData("C:/Windows/web/wallpaper.jpg", "absolute")]
    [InlineData("/wallpaper.jpg", "absolute")]
    [InlineData("\\\\server\\share\\x.jpg", "absolute")]
    [InlineData("assets/wallpaper.jpg:evil", "absolute")]
    [InlineData("assets/run.exe", "not allowed")]
    [InlineData("assets/wallpaper.jpg.", "not valid")]
    public void Unsafe_asset_paths_are_errors(string path, string message)
    {
        var r = LoadSample(json: Replace("\"wallpaper\": \"assets/wallpaper.jpg\"", $"\"wallpaper\": \"{path.Replace("\\", "\\\\")}\""));

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == "windows.wallpaper" && e.Message.Contains(message));
    }

    [Fact]
    public void Missing_asset_is_only_a_warning()
    {
        var r = LoadSample(json: Replace("assets/logo.png", "assets/missing.png"));

        Assert.True(r.Success);
        Assert.Contains(r.Warnings, w => w.Path == "surfaces[id=menubar].start[0].icon");
        Assert.Null(((BarSurface)r.Theme!.Surfaces[0]).Start[0].Icon);
    }

    [Fact]
    public void Text_style_padding_spacing_and_module_style_are_read()
    {
        string json = Replace("\"style\": { \"background\": \"$glass\", \"blur\": true, \"foreground\": \"$text\" }",
                "\"style\": { \"background\": \"$glass\", \"blur\": true, \"foreground\": \"$text\", \"font\": \"ui\", \"fontSize\": 13, \"fontWeight\": \"medium\", \"padding\": 12, \"spacing\": 8 }")
            .Replace("{ \"module\": \"active-app\", \"bold\": true }", "{ \"module\": \"active-app\", \"style\": { \"fontWeight\": \"bold\", \"padding\": 4, \"foreground\": \"#FF000000\" } }");
        var r = LoadSample(json: json);

        Assert.True(r.Success, string.Join("\n", r.Diagnostics));
        var bar = (BarSurface)r.Theme!.Surfaces[0];
        Assert.Equal(new TextStyle("ui", 13, 500), bar.Style.Text);
        Assert.Equal(12, bar.Style.Padding);
        Assert.Equal(8, bar.Style.Spacing);
        var app = bar.Start[1].Style;
        Assert.Equal(700, app.Text.FontWeight);
        Assert.Null(app.Text.FontSize); // inherits from the bar
        Assert.Equal(4, app.Padding);
        Assert.Equal(new ThemeColor(0xFF, 0, 0, 0), app.Foreground);
    }

    [Theory]
    [InlineData("\"format\": \"ddd d MMM HH:mm\"", "\"format\": \"'unclosed\"", "surfaces[id=menubar].end[1].format", "not a valid date/time format")]
    [InlineData("{ \"module\": \"volume\" }", "{ \"module\": \"spacer\", \"size\": -5 }", "surfaces[id=menubar].end[0].size", "out of range")]
    [InlineData("{ \"module\": \"volume\" }", "{ \"module\": \"volume\", \"style\": { \"fontWeight\": \"heavy-ish\" } }", "surfaces[id=menubar].end[0].style.fontWeight", "not valid")]
    [InlineData("\"style\": { \"background\": \"$glass\"", "\"style\": { \"fontSize\": 0, \"background\": \"$glass\"", "surfaces[id=menubar].style.fontSize", "out of range")]
    public void Invalid_text_and_module_options_give_readable_errors(string from, string to, string path, string message)
    {
        var r = LoadSample(json: Replace(from, to));

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == path && e.Message.Contains(message));
    }

    [Fact]
    public void Module_state_icons_and_names_are_read()
    {
        string json = Replace("{ \"module\": \"volume\" }", "{ \"module\": \"volume\", \"showPercent\": true, \"icons\": { \"muted\": \"assets/logo.png\", \"loud\": \"assets/logo.png\" } }")
            .Replace("{ \"module\": \"active-app\", \"bold\": true }", "{ \"module\": \"active-app\", \"names\": { \"explorer.exe\": \"Files\" } }");
        var r = LoadSample(json: json);

        Assert.True(r.Success, string.Join(Environment.NewLine, r.Diagnostics));
        var volume = ((BarSurface)r.Theme!.Surfaces[0]).End[0];
        Assert.Equal(["muted"], volume.StateIcons.Keys);
        Assert.Contains(r.Warnings, w => w.Path == "surfaces[id=menubar].end[0].icons.loud");
    }

    [Fact]
    public void Active_app_names_must_be_strings()
    {
        var r = LoadSample(json: Replace("{ \"module\": \"active-app\", \"bold\": true }", "{ \"module\": \"active-app\", \"names\": { \"a.exe\": 1 } }"));
        Assert.Contains(r.Errors, e => e.Path == "surfaces[id=menubar].start[1].names");
    }

    [Fact]
    public void Duplicate_keys_in_the_overrides_only_drop_the_overrides()
    {
        var r = LoadSample(overrides: """{ "surfaces": [], "surfaces": [] }""");

        Assert.True(r.Success, string.Join(Environment.NewLine, r.Diagnostics));
        Assert.Equal("overrides", Assert.Single(r.Warnings).Path);
    }

    [Theory]
    [InlineData("""{ "surfaces": [ { "id": "dock", "iconSize": 900 } ] }""", "surfaces[id=dock].iconSize")]
    [InlineData("""{ "tokens": { "text": "$accent" } }""", "tokens.text")]
    public void Overrides_that_break_the_theme_are_skipped_with_warnings(string overrides, string path)
    {
        var r = LoadSample(overrides: overrides);

        Assert.True(r.Success);
        Assert.Equal(48, ((DockSurface)r.Theme!.Surfaces[1]).IconSize);
        Assert.Empty(r.Errors);
        Assert.Contains(r.Warnings, w => w.Path == "overrides" && w.Message.Contains("not applied"));
        Assert.Contains(r.Warnings, w => w.Path == path);
    }

    [Fact]
    public void A_broken_theme_reports_its_own_errors_even_with_overrides()
    {
        var r = LoadSample(json: Replace("\"iconSize\": 48", "\"iconSize\": 900"), overrides: "{}");

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == "surfaces[id=dock].iconSize");
    }

    [Theory]
    [InlineData("\"iconSize\": 48", "\"iconSize\": 1e400", "surfaces[id=dock].iconSize")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1e400,", "schemaVersion")]
    public void Numbers_beyond_double_range_are_errors_at_their_path(string from, string to, string path)
    {
        var r = LoadSample(json: Replace(from, to));

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == path);
    }

    [Fact]
    public void Reserved_device_names_get_their_own_message()
    {
        var r = LoadSample(json: Replace("\"id\": \"com.example.sample\"", "\"id\": \"nul.theme\""));
        Assert.Contains(r.Errors, e => e.Path == "id" && e.Message.Contains("reserved"));
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("com.example.my-theme", true)]
    [InlineData("com1.x", false)]
    [InlineData("lpt9", false)]
    [InlineData("a-", false)]
    [InlineData(".a", false)]
    [InlineData("A", false)]
    public void Theme_id_rules(string id, bool valid) => Assert.Equal(valid, ThemeBinder.IsValidId(id));

    [Fact]
    public void Theme_id_length_limit_is_100()
    {
        Assert.True(ThemeBinder.IsValidId(new string('a', 100)));
        Assert.False(ThemeBinder.IsValidId(new string('a', 101)));
    }

    [Fact]
    public void Icon_folder_is_resolved_and_checked()
    {
        Assert.EndsWith(Path.Combine("assets", "icons"), LoadSample().Theme!.IconFolder);

        var missing = LoadSample(json: Replace("\"iconFolder\": \"assets/icons\"", "\"iconFolder\": \"assets/none\""));
        Assert.True(missing.Success);
        Assert.Contains(missing.Warnings, w => w.Path == "iconFolder");

        var escape = LoadSample(json: Replace("\"iconFolder\": \"assets/icons\"", "\"iconFolder\": \"../..\""));
        Assert.Contains(escape.Errors, e => e.Path == "iconFolder");
    }

    [Fact]
    public void Invalid_icon_override_keys_are_skipped_with_a_warning()
    {
        var r = LoadSample(json: Replace("\"exe:code.exe\"", "\"code.exe\""));

        Assert.True(r.Success);
        Assert.Empty(r.Theme!.IconOverrides);
        Assert.Contains(r.Warnings, w => w.Path == "iconOverrides.code.exe");
    }

    [Theory]
    [InlineData("\"variants\": {", "\"variants\": [], \"x\": {", "variants")]
    [InlineData("\"dark\": { \"tokens\"", "\"dark\": 3, \"z\": { \"tokens\"", "variants.dark")]
    [InlineData("\"glass\": \"#99F5F5F5\"", "\"glass\": { \"a\": 1 }", "tokens.glass")]
    [InlineData("\"tokens\": { \"glass\"", "\"tokens\": [], \"t\": { \"glass\"", "tokens")]
    public void Variants_and_tokens_of_the_wrong_kind_are_errors(string from, string to, string path)
    {
        // The dark variant replaces tokens.glass, so token cases load without it.
        var r = LoadSample(path.StartsWith("variants") ? "dark" : null, json: Replace(from, to));

        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Path == path);
    }

    [Fact]
    public void A_variant_can_add_a_surface()
    {
        string json = Replace("\"dark\": { \"tokens\"", "\"dark\": { \"surfaces\": [ { \"id\": \"side\", \"type\": \"bar\", \"edge\": \"left\" } ], \"tokens\"");
        var r = LoadSample("dark", json: json);

        Assert.True(r.Success, string.Join(Environment.NewLine, r.Diagnostics));
        Assert.Equal(["menubar", "dock", "side"], r.Theme!.Surfaces.Select(s => s.Id));
        Assert.Equal(ThemeLayer.Variant, r.Theme.OriginOf("surfaces[id=side].edge"));
    }

    [Fact]
    public void A_crafted_variant_with_many_keys_loads_quickly()
    {
        // Thousands of token leaves plus a variant overriding thousands of scalars: once quadratic (minutes).
        var tokens = string.Join(",", Enumerable.Range(0, 20000).Select(i => $"\"t{i}\": \"#FF000000\""));
        string json = Replace("\"tokens\": { \"glass\": \"#99F5F5F5\",", $"\"tokens\": {{ {tokens}, \"glass\": \"#99F5F5F5\",")
            .Replace("\"dark\": { \"tokens\": {", $"\"dark\": {{ \"tokens\": {{ {tokens},");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var r = LoadSample("dark", json: json);

        Assert.True(r.Success, string.Join(Environment.NewLine, r.Diagnostics.Take(3)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public void Built_in_theme_is_valid()
    {
        Assert.Equal("veneer.fallback", BuiltInTheme.Theme.Id);
        Assert.Single(BuiltInTheme.Theme.Surfaces);
    }
}
