using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class ThemeChangesTests
{
    [Fact]
    public void Two_loads_of_the_same_theme_are_the_same()
    {
        // Different temp folders would differ in paths, so compare loads from one folder.
        using var t = new TempTheme();
        var a = ThemeLoader.Load(t.Root).Theme!;
        var b = ThemeLoader.Load(t.Root).Theme!;

        Assert.True(ThemeChanges.OnlyPinnedChanged(a, b));
    }

    [Fact]
    public void A_pinned_list_change_alone_is_recognized()
    {
        using var t = new TempTheme();
        var before = ThemeLoader.Load(t.Root).Theme!;
        string overrides = t.Write("../" + Path.GetFileName(t.Root) + ".ov.json", """{ "surfaces": [ { "id": "dock", "defaultPinned": [ "code.exe" ] } ] }""");
        try
        {
            var after = ThemeLoader.Load(t.Root, overridesPath: overrides).Theme!;
            Assert.True(ThemeChanges.OnlyPinnedChanged(before, after));
        }
        finally
        {
            File.Delete(overrides);
        }
    }

    [Fact]
    public void A_bar_pinned_list_change_alone_is_recognized()
    {
        using var t = new TempTheme();
        var before = ThemeLoader.Load(t.Root).Theme!;
        string overrides = t.Write("../" + Path.GetFileName(t.Root) + ".ov.json", """{ "surfaces": [ { "id": "menubar", "defaultPinned": [ "code.exe" ] } ] }""");
        try
        {
            var after = ThemeLoader.Load(t.Root, overridesPath: overrides).Theme!;
            Assert.True(ThemeChanges.OnlyPinnedChanged(before, after));
        }
        finally
        {
            File.Delete(overrides);
        }
    }

    [Theory]
    [InlineData("""{ "surfaces": [ { "id": "menubar", "margin": 6 } ] }""")]
    [InlineData("""{ "surfaces": [ { "id": "dock", "iconSize": 56 } ] }""")]
    [InlineData("""{ "surfaces": [ { "id": "menubar", "thickness": 30 } ] }""")]
    [InlineData("""{ "tokens": { "text": "#FF000000" } }""")]
    [InlineData("""{ "surfaces": [ { "id": "dock", "defaultPinned": [ "a.exe" ], "padding": 2 } ] }""")]
    public void Any_other_change_needs_a_rebuild(string overrides)
    {
        using var t = new TempTheme();
        var before = ThemeLoader.Load(t.Root).Theme!;
        string path = t.Write("../" + Path.GetFileName(t.Root) + ".ov.json", overrides);
        try
        {
            var after = ThemeLoader.Load(t.Root, overridesPath: path).Theme!;
            Assert.False(ThemeChanges.OnlyPinnedChanged(before, after));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Module_option_changes_need_a_rebuild()
    {
        using var t = new TempTheme();
        var before = ThemeLoader.Load(t.Root).Theme!;
        t.Write("theme.json", TempTheme.Sample.Replace("\"format\": \"ddd d MMM HH:mm\"", "\"format\": \"HH:mm\""));
        var after = ThemeLoader.Load(t.Root).Theme!;

        Assert.False(ThemeChanges.OnlyPinnedChanged(before, after));
    }
}
