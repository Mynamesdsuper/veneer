using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Veneer.Core.Settings;
using Veneer.Core.Tests.Themes;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests;

/// <summary>The logic behind the settings window: values and their origin, reset, orphans, report, translations.</summary>
public class SettingsWindowLogicTests : IDisposable
{
    readonly TempTheme theme = new();

    string Overrides => theme.Path("profile/overrides.json");

    public void Dispose() => theme.Dispose();

    Theme Load()
    {
        var result = ThemeLoader.Load(theme.Root, overridesPath: Overrides);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.Theme!;
    }

    SurfaceSetting Setting(Theme t, string surface, string key) =>
        SurfaceSettings.For(t, t.Surfaces.Single(s => s.Id == surface)).Single(s => s.Key == key);

    [Fact]
    public void Values_come_from_the_theme_until_the_user_changes_them()
    {
        var before = Setting(Load(), "dock", "magnification.maxScale");
        Assert.Equal(1.6, (double)before.Value);
        Assert.Equal(ThemeLayer.Theme, before.Origin);
        Assert.False(before.IsYours);

        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", before.Key, before.ToJson(2.0), out _));
        var after = Load();
        var changed = Setting(after, "dock", "magnification.maxScale");
        Assert.Equal(2.0, (double)changed.Value);
        Assert.True(changed.IsYours);
        // Only that value: the theme's other magnification values still apply.
        Assert.Equal(ThemeLayer.Theme, after.OriginOf("surfaces[id=dock].magnification.radius"));
        Assert.Equal(3, ((DockSurface)after.Surfaces.Single(s => s.Id == "dock")).Magnification.Radius);
    }

    [Fact]
    public void Reset_removes_the_value_and_the_empty_objects_around_it()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "magnification.maxScale", 2.0, out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "magnification.maxScale", null, out _));

        Assert.Empty(JsonNode.Parse(File.ReadAllText(Overrides))!["surfaces"]!.AsArray()); // no { "id": "dock", "magnification": {} } left
        Assert.Equal(ThemeLayer.Theme, Setting(Load(), "dock", "magnification.maxScale").Origin);
    }

    [Fact]
    public void Reset_keeps_the_other_customizations_of_the_surface()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "iconSize", 64, out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "magnification.enabled", false, out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "magnification.enabled", null, out _));

        var t = Load();
        Assert.True(Setting(t, "dock", "iconSize").IsYours);
        Assert.Equal(64.0, Convert.ToDouble(Setting(t, "dock", "iconSize").Value));
    }

    [Fact]
    public void Resetting_a_value_that_was_never_set_writes_nothing()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "magnification.maxScale", null, out _));
        Assert.False(File.Exists(Overrides));

        const string commented = "{ // my notes\n \"surfaces\": [ { \"id\": \"dock\", \"iconSize\": 60 } ] }";
        theme.Write("profile/overrides.json", commented);
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "autoHide", null, out _));
        Assert.True(OverridesFile.TryRemoveSurface(Overrides, "gone", out _));
        Assert.Equal(commented, File.ReadAllText(Overrides)); // comments kept, no backup needed
        Assert.False(File.Exists(Overrides + ".bak"));
    }

    [Fact]
    public void Reset_clears_the_value_from_every_entry_with_that_id()
    {
        theme.Write("profile/overrides.json", """{ "surfaces": [ { "id": "dock", "iconSize": 64 }, { "id": "dock", "iconSize": 72, "padding": 2 } ] }""");

        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "iconSize", null, out _));

        Assert.Equal(ThemeLayer.Theme, Setting(Load(), "dock", "iconSize").Origin);
    }

    [Fact]
    public void The_scale_keeps_magnification_off_when_the_theme_has_none()
    {
        theme.Write("theme.json", TempTheme.Sample.Replace("\"magnification\": { \"enabled\": true, \"maxScale\": 1.6, \"radius\": 3 },", ""));
        var t = Load();
        Assert.False((bool)Setting(t, "dock", "magnification.enabled").Value);
        var scale = Setting(t, "dock", "magnification.maxScale");

        // What the settings window does: the companions first, then the value.
        foreach (var w in scale.WriteWith ?? [])
            Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, w.SurfaceId, w.Key, w.ToJson(w.Value), out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", scale.Key, scale.ToJson(2.0), out _));

        var dock = (DockSurface)Load().Surfaces.Single(s => s.Id == "dock");
        Assert.False(dock.Magnification.Enabled);
        Assert.Equal(2.0, dock.Magnification.MaxScale);
    }

    [Fact]
    public void An_orphaned_surface_can_be_removed()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "gone", "thickness", 30, out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, "dock", "iconSize", 64, out _));
        Assert.Equal(["surfaces[id=gone]"], ThemeLoader.Load(theme.Root, overridesPath: Overrides).OrphanOverrides);

        Assert.True(OverridesFile.TryRemoveSurface(Overrides, "gone", out _));

        var result = ThemeLoader.Load(theme.Root, overridesPath: Overrides);
        Assert.Empty(result.OrphanOverrides);
        Assert.True(Setting(result.Theme!, "dock", "iconSize").IsYours);
    }

    [Fact]
    public void Numbers_are_clamped_and_whole_numbers_stay_whole()
    {
        var iconSize = Setting(Load(), "dock", "iconSize");
        Assert.Equal("256", iconSize.ToJson(900.0).ToJsonString());
        Assert.Equal("49", iconSize.ToJson(48.6).ToJsonString());
        var scale = Setting(Load(), "dock", "magnification.maxScale");
        Assert.Equal("1.7", scale.ToJson(1.66).ToJsonString());
        Assert.Throws<ArgumentException>(() => Setting(Load(), "dock", "edge").ToJson("top")); // a dock cannot sit on top
    }

    [Fact]
    public void Every_written_value_loads_back()
    {
        var t = Load();
        foreach (var s in t.Surfaces.SelectMany(s => SurfaceSettings.For(t, s)))
        {
            object value = s.Kind switch { SettingKind.Toggle => !(bool)s.Value, SettingKind.Number => s.Max, _ => s.Choices![^1] };
            Assert.True(OverridesFile.TrySetSurfaceValue(Overrides, s.SurfaceId, s.Key, s.ToJson(value), out _));
        }

        var reloaded = Load();
        Assert.All(reloaded.Surfaces.SelectMany(s => SurfaceSettings.For(reloaded, s)), s => Assert.True(s.IsYours, s.Key));
    }

    [Fact]
    public void The_report_hides_the_user_folder_and_names()
    {
        var info = new ReportInfo("0.1.0", "Windows 11 26300", ["2560x1440 at 144 dpi (primary)"], "Work", "com.example.sample",
            @"C:\Users\Mario\AppData\Roaming\Veneer\themes\sample",
            [Diagnostic.Warning("surfaces[id=gone]", "not applied")],
            [@"start: --theme c:\users\mario\themes\x", "user Mario on MARIO-PC", "Veneer.App started", "C:/Users/Mario/x.png"]);

        string report = DiagnosticsReport.Build(info, @"C:\Users\Mario", "Mario", "MARIO-PC");

        Assert.DoesNotContain("mario", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\AppData\Roaming\Veneer\themes\sample", report);
        Assert.Contains("user <user> on <pc>", report);
        Assert.Contains("Warning: surfaces[id=gone]: not applied", report);
        Assert.Contains("Veneer.App started", report);
    }

    [Fact]
    public void Short_user_names_do_not_eat_ordinary_words()
    {
        Assert.Equal("Veneer.App and app", DiagnosticsReport.Scrub("Veneer.App and app", @"C:\Users\ap", "ap", "pc"));
        Assert.Equal("Veneer.App <user>", DiagnosticsReport.Scrub("Veneer.App app", @"C:\Users\app", "app", "pc"));
        Assert.Equal(@"%USERPROFILE%\AppData\Local\Temp\x.tmp", DiagnosticsReport.Scrub(@"C:\Users\MARIOR~1\AppData\Local\Temp\x.tmp", @"C:\Users\Mario Rossi", "Mario Rossi", "pc"));
    }

    [Fact]
    public void Profile_messages_follow_the_display_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
            Assert.Equal("Il nome è vuoto.", Veneer.Core.Profiles.FolderName.Problem(""));
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Assert.Equal("The name is empty.", Veneer.Core.Profiles.FolderName.Problem(""));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    static string RepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Veneer.slnx")))
                return Path.Combine(dir.FullName, relative);
        }

        throw new InvalidOperationException("repository root not found");
    }

    static Dictionary<string, string> Resx(string relative) =>
        XDocument.Load(RepoFile(relative)).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    [Theory]
    [InlineData(@"src\Veneer.Core\Strings")]
    [InlineData(@"src\Veneer.App\Strings")]
    public void Every_string_is_translated_and_used(string resources)
    {
        var english = Resx(resources + ".resx");
        var italian = Resx(resources + ".it.resx");
        Assert.Equal(english.Keys.Order(), italian.Keys.Order());

        // Placeholders ({0}, {1}) must match, or string.Format throws in one language only.
        static string Holes(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order());
        Assert.All(english, e => Assert.Equal(Holes(e.Value), Holes(italian[e.Key])));

        // Each property of Strings.cs reads the key with its own name: every one must exist, and none may be unused.
        string code = File.ReadAllText(RepoFile(resources + ".cs"));
        var used = Regex.Matches(code, @"public static string (\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(english.Keys.Order(), used.Order());
    }
}
