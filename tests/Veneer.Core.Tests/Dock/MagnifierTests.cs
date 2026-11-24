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

        // The cursor maps through the cell plus its gap: 10 of 54 base px -> the same share of the magnified span.
        double share = 10.0 / (48 + 6);
        Assert.Equal(cursor, second.Start + share * (second.Length + 6), 6);
    }

    [Fact]
    public void The_row_moves_continuously_as_the_cursor_crosses_gaps()
    {
        // Sweep across the whole row in 0.5 px steps: no cell may jump by more than a few px between two steps.
        MagnifiedCell[]? previous = null;
        for (double x = 0; x <= 5 * 54; x += 0.5)
        {
            var cells = Magnifier.Layout(FiveIcons, 6, x, 1.8, 2);
            if (previous is not null)
            {
                for (int i = 0; i < cells.Length; i++)
                    Assert.True(Math.Abs(cells[i].Start - previous[i].Start) < 3, $"cell {i} jumped at x={x}");
            }

            previous = cells;
        }
    }

    [Fact]
    public void Cursor_outside_the_row_and_empty_rows_are_safe()
    {
        Assert.Empty(Magnifier.Layout([], 6, 10, 2, 3));
        Assert.All(Magnifier.Layout(FiveIcons, 6, -500, 2, 3), c => Assert.False(double.IsNaN(c.Start)));
        Assert.All(Magnifier.Layout(FiveIcons, 6, 5000, 2, 3), c => Assert.Equal(1, c.Scale));
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
    public void Move_backwards_and_across_separators()
    {
        Assert.Equal(["ms-settings:", "explorer.exe", "msedge.exe", DockModel.Separator], DockModel.Move(Pinned, 3, 0));
        Assert.Equal(["explorer.exe", DockModel.Separator, "msedge.exe", "ms-settings:"], DockModel.Move(Pinned, 2, 1));
    }

    [Fact]
    public void Duplicate_pinned_entries_give_one_running_icon()
    {
        var explorer = new RunningApp("exe:c:\\windows\\explorer.exe", null, @"C:\Windows\explorer.exe", [1]);
        var items = DockModel.Build(["explorer.exe", "explorer.exe"], [explorer]);

        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsRunning);
        Assert.False(items[1].IsRunning);
    }

    [Theory]
    [InlineData(@"C:explorer.exe", false)] // drive-relative: not a full path
    [InlineData(@"C:\Windows\explorer.exe", true)]
    public void Only_fully_qualified_paths_match_as_paths(string entry, bool expected)
    {
        var explorer = new RunningApp("k", null, @"C:\Windows\explorer.exe", [1]);
        Assert.Equal(expected, DockModel.Matches(entry, explorer));
    }

    // Drag: four pinned 48 px cells, 6 px apart (starts 0, 54, 108, 162), then two running apps.
    static readonly double[] Sizes = [48, 48, 48, 48, 48, 48];

    [Theory]
    [InlineData(0, 0, 0)]      // dropped where it was
    [InlineData(0, 60, 0)]     // first half of cell 1: stays first
    [InlineData(0, 90, 1)]     // past the middle of cell 1: lands after it
    [InlineData(0, 170, 2)]    // into the last pinned cell
    [InlineData(3, 5, 0)]      // last to first
    [InlineData(1, 10000, 3)]  // beyond the pinned cells: last pinned position
    public void Drop_index_follows_the_cursor(int from, double along, int expected) =>
        Assert.Equal(expected, DockModel.DropIndex(Sizes, 6, 4, along, from));

    [Fact]
    public void Drag_order_moves_one_cell()
    {
        Assert.Equal([1, 2, 0, 3], DockModel.DragOrder(4, 0, 2));
        Assert.Equal([3, 0, 1, 2], DockModel.DragOrder(4, 3, 0));
        Assert.Equal([0, 1, 2], DockModel.DragOrder(3, 7, 0));
        Assert.Equal(0, DockModel.DropIndex(Sizes, 6, 0, 50, 0));
    }

    [Fact]
    public void Entry_for_a_running_app_prefers_its_aumid()
    {
        Assert.Equal("A!B", DockModel.EntryFor(new RunningApp("k", "A!B", @"C:\a.exe", [])));
        Assert.Equal(@"C:\a.exe", DockModel.EntryFor(new RunningApp("k", null, @"C:\a.exe", [])));
    }
}
