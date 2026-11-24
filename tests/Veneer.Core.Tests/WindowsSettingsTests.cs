using Veneer.Core.Settings;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests;

public class WindowsSettingsTests
{
    [Fact]
    public void Missing_preferences_mean_everything_from_the_theme()
    {
        var warnings = new List<string>();
        Assert.Equal(new WindowsPreferences(), WindowsPreferences.Parse(null, warnings));
        Assert.Equal(new WindowsPreferences(SettingSource.Theme, SettingSource.Theme, AppearanceChoice.Theme), WindowsPreferences.Parse("{}", warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Preferences_round_trip_and_read_any_case()
    {
        var prefs = new WindowsPreferences(SettingSource.System, SettingSource.Theme, AppearanceChoice.Dark);
        var warnings = new List<string>();

        Assert.Equal(prefs, WindowsPreferences.Parse(prefs.Serialize(), warnings));
        Assert.Equal(AppearanceChoice.System, WindowsPreferences.Parse("""{ "appearance": "SYSTEM" }""", warnings).Appearance);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("""{ "wallpaper": "blue", "accent": "system" }""")]
    [InlineData("""{ "wallpaper": 1, "accent": "system" }""")]
    [InlineData("""{ "wallpaper": "1", "accent": "system" }""")]
    public void A_bad_value_falls_back_alone(string json)
    {
        var warnings = new List<string>();
        var prefs = WindowsPreferences.Parse(json, warnings);

        Assert.Equal(SettingSource.Theme, prefs.Wallpaper);
        Assert.Equal(SettingSource.System, prefs.Accent); // the valid value is kept
        Assert.Contains("wallpaper", Assert.Single(warnings));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[]")]
    public void A_broken_file_gives_the_defaults(string json)
    {
        var warnings = new List<string>();
        Assert.Equal(new WindowsPreferences(), WindowsPreferences.Parse(json, warnings));
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData(null, AppearanceChoice.Theme, null)]
    [InlineData(Appearance.Dark, AppearanceChoice.Theme, true)]
    [InlineData(Appearance.Light, AppearanceChoice.Theme, false)]
    [InlineData(Appearance.System, AppearanceChoice.Theme, null)]
    [InlineData(Appearance.Dark, AppearanceChoice.Light, false)]
    [InlineData(Appearance.Light, AppearanceChoice.Dark, true)]
    [InlineData(Appearance.Dark, AppearanceChoice.System, null)]
    public void Desired_dark_mode(Appearance? theme, AppearanceChoice choice, bool? expected) =>
        Assert.Equal(expected, WindowsSettingsPlan.DesiredDark(theme, choice));

    [Theory]
    [InlineData(true, false, "dark")]
    [InlineData(false, true, "light")]
    [InlineData(null, true, "dark")]
    [InlineData(null, false, "light")]
    public void Variant_follows_the_mode_veneer_sets_or_windows(bool? desired, bool windowsDark, string expected) =>
        Assert.Equal(expected, WindowsSettingsPlan.Variant(desired, windowsDark));

    [Fact]
    public void Plan_takes_only_what_comes_from_the_theme()
    {
        using var t = new Themes.TempTheme();
        var theme = ThemeLoader.Load(t.Root).Theme!;

        var all = WindowsSettingsPlan.For(theme, new WindowsPreferences());
        Assert.EndsWith("wallpaper.jpg", all.Wallpaper);
        Assert.Equal(new ThemeColor(0xFF, 0x0A, 0x84, 0xFF), all.Accent);
        Assert.False(all.Dark); // the sample theme says "light"

        var none = WindowsSettingsPlan.For(theme, new WindowsPreferences(SettingSource.System, SettingSource.System, AppearanceChoice.System));
        Assert.Equal(new WindowsPlan(null, null, null), none);
    }

    [Fact]
    public void Snapshot_round_trips_with_missing_values()
    {
        var snapshot = new WindowsSnapshot(
            new WallpaperSnapshot([new MonitorWallpaper(@"\\?\DISPLAY#1", @"C:\Users\x\Pictures\a.jpg")], 4),
            new AccentSnapshot(0xFFA73A8E, 0xFFC14DA9,
            [
                new SavedValue(@"Software\Microsoft\Windows\DWM", "AccentColor", "DWord", -4108887, null, null),
                new SavedValue(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", "Binary", null, "AQID", null),
                new SavedValue(@"Control Panel\Desktop", "AutoColorization", null, null, null, null),
            ]),
            null);

        var back = WindowsSnapshot.Parse(snapshot.Serialize())!;

        Assert.Equal(snapshot.Wallpaper!.Monitors, back.Wallpaper!.Monitors);
        Assert.Equal(snapshot.Accent!.Values, back.Accent!.Values);
        Assert.False(back.Accent.Values[2].Existed);
        Assert.Null(back.Appearance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ \"Wallpaper\": ")]
    [InlineData("{ \"Wallpaper\": 3 }")]
    public void A_damaged_snapshot_reads_as_null(string? text) => Assert.Null(WindowsSnapshot.Parse(text));
}
