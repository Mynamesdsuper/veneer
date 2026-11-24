using System.Text.Json.Nodes;
using Veneer.Core.Dock;

namespace Veneer.Core.Tests.Dock;

public class MagnifierTests
{
    static readonly (double, bool)[] FiveIcons = [(48, true), (48, true), (48, true), (48, true), (48, true)];

    [Fact]
    public void Without_cursor_the_row_is_the_base_layout()
    {
        var cells = Magnifier.Layout(FiveIcons, 6, null, 1.6, 3);

        Assert.Equal([0, 54, 108, 162, 216], cells.Select(c => c.Start));
        Assert.All(cells, c => Assert.Equal(1, c.Scale));
    }

    [Fact]
    public void Cell_under_the_cursor_gets_the_max_scale_and_neighbors_less()
    {
        var cells = Magnifier.Layout(FiveIcons, 6, 108 + 24, 1.6, 3); // center of the middle icon

        Assert.Equal(1.6, cells[2].Scale, 6);
        Assert.True(cells[1].Scale < cells[2].Scale && cells[1].Scale > 1);
        Assert.Equal(cells[1].Scale, cells[3].Scale, 6); // symmetric
        Assert.True(cells[0].Scale < cells[1].Scale);
    }

    [Fact]
    public void Point_under_the_cursor_stays_put()
    {
        double cursor = 54 + 10; // 10 px into the second icon
        var cells = Magnifier.Layout(FiveIcons, 6, cursor, 2, 2);
        var second = cells[1];

        double fraction = 10.0 / 48;
        Assert.Equal(cursor, second.Start + fraction * second.Length, 6);
    }

    [Fact]
    public void Beyond_the_radius_nothing_grows()
    {
        var cells = Magnifier.Layout(FiveIcons, 6, 0, 1.6, 1); // reach = 1 cell (54 px) from the first icon's edge

        Assert.Equal(1, cells[2].Scale);
        Assert.Equal(1, cells[4].Scale);
    }

    [Fact]
    public void Separators_never_grow()
    {
        var cells = Magnifier.Layout([(48, true), (13, false), (48, true)], 6, 48 + 6 + 6, 2, 3);
        Assert.Equal(1, cells[1].Scale);
        Assert.Equal(13, cells[1].Length);
    }

    [Theory]
    [InlineData(1.0, 3)]
    [InlineData(1.6, 0)]
    public void Disabled_magnification_changes_nothing(double maxScale, int radius)
    {
        var cells = Magnifier.Layout(FiveIcons, 6, 100, maxScale, radius);
        Assert.All(cells, c => Assert.Equal(1, c.Scale));
    }
}

public class PinnedListTests
{
    static readonly string[] Pinned = ["explorer.exe", "msedge.exe", DockModel.Separator, "ms-settings:"];

    [Fact]
    public void Pin_adds_once_at_the_index()
    {
        Assert.Equal(["explorer.exe", "msedge.exe", DockModel.Separator, "ms-settings:", "code.exe"], DockModel.Pin(Pinned, "code.exe"));
        Assert.Equal(["code.exe", "explorer.exe", "msedge.exe", DockModel.Separator, "ms-settings:"], DockModel.Pin(Pinned, "code.exe", 0));
        Assert.Equal(["msedge.exe", "explorer.exe", DockModel.Separator, "ms-settings:"], DockModel.Pin(Pinned, "EXPLORER.EXE", 1)
            .Select(p => p.ToLowerInvariant()));
    }

    [Fact]
    public void Unpin_and_move()
    {
        Assert.Equal(["explorer.exe", DockModel.Separator, "ms-settings:"], DockModel.Unpin(Pinned, "MSEDGE.exe"));
        Assert.Equal(["msedge.exe", DockModel.Separator, "explorer.exe", "ms-settings:"], DockModel.Move(Pinned, 0, 2));
        Assert.Equal(Pinned, DockModel.Move(Pinned, 9, 0));
    }

    [Fact]
    public void Separators_come_from_the_list_and_before_other_running_apps()
    {
        var running = new RunningApp("exe:c:\\x\\discord.exe", null, @"C:\x\Discord.exe", [1]);
        var items = DockModel.Build(Pinned, [running], separateRunning: true);

        Assert.Equal([false, false, true, false, true, false], items.Select(i => i.IsSeparator));
        Assert.False(DockModel.Build(Pinned, [], separateRunning: true)[^1].IsSeparator);
        Assert.DoesNotContain(DockModel.Build(Pinned, [running], separateRunning: false), i => i.Key == "sep:running");
    }

    [Fact]
    public void Entry_for_a_running_app_prefers_its_aumid()
    {
        Assert.Equal("A!B", DockModel.EntryFor(new RunningApp("k", "A!B", @"C:\a.exe", [])));
        Assert.Equal(@"C:\a.exe", DockModel.EntryFor(new RunningApp("k", null, @"C:\a.exe", [])));
    }
}

public class OverridesFileTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));

    string FilePath => Path.Combine(dir, "profile", "overrides.json");

    public void Dispose()
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Creates_the_file_and_the_surface()
    {
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray("a.exe"), out _));

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("dock", root["surfaces"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("a.exe", root["surfaces"]![0]!["defaultPinned"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Keeps_everything_else_and_updates_in_place()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "tokens": { "x": "#FF000000" }, "surfaces": [ { "id": "dock", "iconSize": 56 }, { "id": "bar" } ] }""");

        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray("b.exe"), out _));
        Assert.True(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "iconSize", null, out _));

        var root = JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("#FF000000", root["tokens"]!["x"]!.GetValue<string>());
        var dock = root["surfaces"]![0]!.AsObject();
        Assert.False(dock.ContainsKey("iconSize"));
        Assert.Equal("b.exe", dock["defaultPinned"]![0]!.GetValue<string>());
        Assert.Equal(2, root["surfaces"]!.AsArray().Count);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("{ \"surfaces\": 3 }")]
    public void Never_overwrites_a_file_it_cannot_read(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        Assert.False(OverridesFile.TrySetSurfaceValue(FilePath, "dock", "defaultPinned", new JsonArray(), out string? error));
        Assert.NotNull(error);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }
}
