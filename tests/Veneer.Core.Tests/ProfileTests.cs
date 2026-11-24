using Veneer.Core.Profiles;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests;

public class ProfileTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));

    ProfileStore Store => new(dir);

    public void Dispose()
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Gaming 2")]
    [InlineData("été")]
    public void Accepts_plain_folder_names(string name) => Assert.Null(FolderName.Problem(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData(@"..\x")]
    [InlineData("a/b")]
    [InlineData(@"C:\x")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("name.")]
    [InlineData(" name")]
    [InlineData("a*b")]
    public void Rejects_paths_and_names_windows_refuses(string name) => Assert.NotNull(FolderName.Problem(name));

    [Fact]
    public void Rejects_too_long_names() => Assert.NotNull(FolderName.Problem(new string('a', FolderName.MaxLength + 1)));

    [Fact]
    public void Default_exists_and_is_active_on_a_new_install()
    {
        Assert.Equal(["default"], Store.Names());
        Assert.Equal("default", Store.Active);
    }

    [Fact]
    public void Profile_file_round_trips_and_bad_values_fall_back()
    {
        var warnings = new List<string>();
        Assert.Equal("mytheme", Profile.Parse(new Profile("mytheme").Serialize(), warnings).Theme);
        Assert.Empty(warnings);

        Assert.Equal(Profile.DefaultTheme, Profile.Parse("""{ "theme": "..\\..\\evil" }""", warnings).Theme);
        Assert.Equal(Profile.DefaultTheme, Profile.Parse("""{ "theme": 3 }""", warnings).Theme);
        Assert.Equal(Profile.DefaultTheme, Profile.Parse("not json", warnings).Theme);
        Assert.Equal(Profile.DefaultTheme, Profile.Parse(null, warnings).Theme);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void Create_switch_and_next()
    {
        Assert.Null(Store.Create("Work"));
        Assert.NotNull(Store.Create("work")); // names are case-insensitive, like folders
        Assert.Equal(["default", "Work"], Store.Names());

        Store.SetActive("work");
        Assert.Equal("Work", Store.Active); // the spelling on disk
        Assert.Equal("default", Store.Next("Work"));
        Assert.Equal("Work", Store.Next("default"));
    }

    [Fact]
    public void Active_falls_back_to_default_when_its_folder_is_gone_or_the_file_is_bad()
    {
        Store.Create("Work");
        Store.SetActive("Work");
        Directory.Delete(Store.Folder("Work"), recursive: true);
        Assert.Equal("default", Store.Active);

        File.WriteAllText(Path.Combine(dir, ProfileStore.SettingsFileName), "{ broken");
        Assert.Equal("default", Store.Active);
    }

    [Fact]
    public void Duplicate_copies_theme_overrides_and_windows_preferences()
    {
        Store.Save("default", new Profile("other"));
        File.WriteAllText(Store.OverridesPath("default"), "{}");
        File.WriteAllText(Store.PreferencesPath("default"), """{ "wallpaper": "system" }""");

        Assert.Null(Store.Duplicate("default", "Copy"));

        Assert.Equal("other", Store.Load("Copy", []).Theme);
        Assert.True(File.Exists(Store.OverridesPath("Copy")));
        Assert.True(File.Exists(Store.PreferencesPath("Copy")));
        Assert.NotNull(Store.Duplicate("default", "Copy"));
        Assert.NotNull(Store.Duplicate("missing", "Copy 2"));
    }

    [Fact]
    public void Rename_keeps_the_active_profile_active_and_allows_case_changes()
    {
        Store.Create("Work");
        Store.SetActive("Work");

        Assert.Null(Store.Rename("Work", "Office"));
        Assert.Equal("Office", Store.Active);

        Assert.Null(Store.Rename("Office", "OFFICE"));
        Assert.Equal("OFFICE", Store.Active);
        Assert.NotNull(Store.Rename("OFFICE", "default"));
        Assert.NotNull(Store.Rename("OFFICE", @"..\x"));
    }

    [Fact]
    public void Default_can_be_renamed_before_its_folder_exists()
    {
        Assert.Null(Store.Rename("default", "Home"));
        Assert.Equal("Home", Store.Active);
        Assert.Contains("Home", Store.Names());
    }

    [Fact]
    public void Delete_refuses_the_active_profile()
    {
        Store.Create("Work");
        Assert.NotNull(Store.Delete("default")); // active
        Assert.Null(Store.Delete("Work"));
        Assert.False(Directory.Exists(Store.Folder("Work")));
        Assert.NotNull(Store.Delete("Work"));
    }

    [Fact]
    public void Theme_folders_prefer_the_users_copy_and_ignore_bad_names()
    {
        string user = Path.Combine(dir, "themes"), bundled = Path.Combine(dir, "bundled");
        WriteTheme(user, "aqua", "My Aqua");
        WriteTheme(bundled, "aqua", "Aqua");
        WriteTheme(bundled, "strip", null);
        Directory.CreateDirectory(Path.Combine(bundled, "empty"));

        Assert.Equal(Path.Combine(user, "aqua"), ThemeFolders.Find("aqua", [user, bundled]));
        Assert.Equal(Path.Combine(bundled, "strip"), ThemeFolders.Find("strip", [user, bundled]));
        Assert.Null(ThemeFolders.Find("empty", [user, bundled]));
        Assert.Null(ThemeFolders.Find(@"..\bundled\aqua", [user]));

        var list = ThemeFolders.List([user, bundled]);
        Assert.Equal(["aqua", "strip"], list.Select(t => t.Name));
        Assert.Equal(["My Aqua", "strip"], list.Select(t => t.DisplayName));
    }

    static void WriteTheme(string root, string name, string? displayName)
    {
        Directory.CreateDirectory(Path.Combine(root, name));
        File.WriteAllText(Path.Combine(root, name, ThemeLoader.ThemeFileName), displayName is null ? "{}" : $$"""{ "name": "{{displayName}}" }""");
    }
}
