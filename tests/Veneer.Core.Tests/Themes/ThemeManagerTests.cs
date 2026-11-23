using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Themes;

public class ThemeManagerTests
{
    [Fact]
    public void Starts_with_the_built_in_theme()
    {
        using var m = new ThemeManager();
        Assert.Same(BuiltInTheme.Theme, m.Current);
        Assert.Null(m.Reload());
    }

    [Fact]
    public void Failed_load_keeps_the_previous_theme()
    {
        using var good = new TempTheme();
        using var bad = new TempTheme("{ broken");
        using var m = new ThemeManager();

        Assert.True(m.Apply(good.Root).Success);
        var r = m.Apply(bad.Root);

        Assert.False(r.Success);
        Assert.Equal("com.example.sample", m.Current.Id);
        Assert.Same(r, m.LastResult);
    }

    [Fact]
    public async Task Edited_theme_reloads_by_itself()
    {
        using var t = new TempTheme();
        using var m = new ThemeManager(TimeSpan.FromMilliseconds(50));
        var reloaded = new TaskCompletionSource<ThemeLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        m.Apply(t.Root);
        m.Loaded += r => { if (r.Theme?.Name == "Edited") reloaded.TrySetResult(r); };

        t.Write("theme.json", TempTheme.Sample.Replace("\"name\": \"Sample\"", "\"name\": \"Edited\""));

        var result = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Success);
        Assert.Equal("Edited", m.Current.Name);
    }

    [Fact]
    public async Task Edited_overrides_reload_by_itself()
    {
        using var t = new TempTheme();
        string overrides = t.Write("../" + Path.GetFileName(t.Root) + "-user/overrides.json", "{}");
        using var m = new ThemeManager(TimeSpan.FromMilliseconds(50));
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        m.Apply(t.Root, overridesPath: overrides);
        m.Loaded += r => { if (r.Theme?.Surfaces[1] is DockSurface { IconSize: 64 }) reloaded.TrySetResult(); };

        File.WriteAllText(overrides, """{ "surfaces": [ { "id": "dock", "iconSize": 64 } ] }""");

        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Directory.Delete(Path.GetDirectoryName(overrides)!, recursive: true);
    }
}
