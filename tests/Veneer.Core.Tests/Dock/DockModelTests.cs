using Veneer.Core.Dock;
using Veneer.Core.Layout;
using Veneer.Core.Themes;

namespace Veneer.Core.Tests.Dock;

public class DockModelTests
{
    static RunningApp Exe(string path) => new(DockModel.KeyFor(null, path), null, path, [1]);

    static RunningApp Packaged(string aumid, string path) => new(DockModel.KeyFor(aumid, path), aumid, path, [2]);

    static readonly RunningApp Explorer = Exe(@"C:\Windows\explorer.exe");
    static readonly RunningApp Terminal = Packaged("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", @"C:\Program Files\WindowsApps\Terminal\WindowsTerminal.exe");
    static readonly RunningApp Settings = Packaged("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", @"C:\Windows\SystemApps\SystemSettings.exe");
    static readonly RunningApp Discord = Exe(@"C:\Users\x\AppData\Local\Discord\app-1.0\Discord.exe");

    [Theory]
    [InlineData("explorer.exe", true)]
    [InlineData("EXPLORER.EXE", true)]
    [InlineData(@"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\Other\explorer.exe", false)]
    [InlineData("notepad.exe", false)]
    public void Exe_entries_match_by_file_name_or_full_path(string entry, bool expected) =>
        Assert.Equal(expected, DockModel.Matches(entry, Explorer));

    [Fact]
    public void Aumid_and_uri_entries_match_packaged_apps()
    {
        Assert.True(DockModel.Matches("microsoft.windowsterminal_8wekyb3d8bbwe!app", Terminal));
        Assert.False(DockModel.Matches("WindowsTerminal.exe", Terminal with { ExePath = null }));
        Assert.True(DockModel.Matches("ms-settings:", Settings));
        Assert.False(DockModel.Matches("ms-settings:", Terminal));
    }

    [Fact]
    public void Pinned_come_first_in_order_then_other_running_apps()
    {
        var items = DockModel.Build(["msedge.exe", "explorer.exe", "ms-settings:"], [Discord, Explorer, Settings, Terminal]);

        Assert.Equal(["pin:msedge.exe", Explorer.Key, Settings.Key, Discord.Key, Terminal.Key], items.Select(i => i.Key));
        Assert.False(items[0].IsRunning);
        Assert.Same(Explorer, items[1].Running);
        Assert.Equal("explorer.exe", items[1].Pinned);
        Assert.Null(items[3].Pinned);
    }

    [Fact]
    public void A_running_app_joins_only_one_pinned_entry()
    {
        var items = DockModel.Build(["explorer.exe", @"C:\Windows\explorer.exe"], [Explorer]);

        Assert.True(items[0].IsRunning);
        Assert.False(items[1].IsRunning);
        Assert.Equal(2, items.Count);
    }

    [Theory]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", true)]
    [InlineData("explorer.exe", false)]
    [InlineData(@"C:\a!b\x.exe", false)]
    [InlineData("ms-settings:", false)]
    public void Recognizes_aumids(string entry, bool expected) => Assert.Equal(expected, DockModel.IsAumid(entry));

    static readonly PixelRect Area = new(0, 24, 1920, 1080);

    [Fact]
    public void Dock_is_centered_on_its_edge_with_a_margin()
    {
        Assert.Equal(new PixelRect(660, 1014, 1260, 1074), DockModel.Place(Area, Edge.Bottom, 600, 60, 6));
        Assert.Equal(new PixelRect(6, 252, 66, 852), DockModel.Place(Area, Edge.Left, 600, 60, 6));
        Assert.Equal(new PixelRect(1854, 252, 1914, 852), DockModel.Place(Area, Edge.Right, 600, 60, 6));
    }

    [Fact]
    public void Dock_never_grows_past_the_area()
    {
        var r = DockModel.Place(Area, Edge.Bottom, 5000, 60, 0);
        Assert.Equal(0, r.Left);
        Assert.Equal(1920, r.Right);
    }

    [Fact]
    public void Remaining_area_excludes_the_bars()
    {
        var r = SurfaceLayout.Remaining(new PixelRect(0, 0, 1920, 1080), [new("top", Edge.Top, 24), new("side", Edge.Left, 40)]);
        Assert.Equal(new PixelRect(40, 24, 1920, 1080), r);
    }
}
