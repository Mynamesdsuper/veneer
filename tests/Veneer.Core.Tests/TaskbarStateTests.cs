using Veneer.Core.Modules;

namespace Veneer.Core.Tests;

public class TaskbarStateTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void Snapshot_round_trips(uint state) => Assert.Equal(state, TaskbarState.Parse(TaskbarState.Serialize(state)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"AppBarState\": ")] // cut off by a power loss
    [InlineData("[]")]
    [InlineData("{ \"Other\": 1 }")]
    [InlineData("{ \"AppBarState\": -1 }")]
    [InlineData("{ \"AppBarState\": \"1\" }")]
    public void Damaged_snapshots_read_as_null(string? text) => Assert.Null(TaskbarState.Parse(text));

    [Fact]
    public void Restore_target_is_the_snapshot_when_readable()
    {
        Assert.Equal(0u, TaskbarState.RestoreTarget(0, current: 3));
        Assert.Equal(1u, TaskbarState.RestoreTarget(1, current: 0));
    }

    [Fact]
    public void Without_a_snapshot_auto_hide_is_turned_off_and_other_bits_kept()
    {
        Assert.Equal(2u, TaskbarState.RestoreTarget(null, current: 3));
        Assert.Equal(0u, TaskbarState.RestoreTarget(null, current: 1));
    }

    [Theory]
    [InlineData(0u, 0u, true)]
    [InlineData(1u, 3u, true)]
    [InlineData(0u, 1u, false)]
    [InlineData(1u, 0u, false)]
    public void Restore_is_confirmed_by_the_auto_hide_bit(uint target, uint actual, bool expected) =>
        Assert.Equal(expected, TaskbarState.IsRestored(target, actual));

    [Fact]
    public void Atomic_write_replaces_the_file_and_leaves_no_temp()
    {
        string dir = Path.Combine(Path.GetTempPath(), "veneer-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "state", "taskbar.json");
        try
        {
            AtomicFile.Write(path, TaskbarState.Serialize(1));
            AtomicFile.Write(path, TaskbarState.Serialize(0));

            Assert.Equal(0u, TaskbarState.Parse(File.ReadAllText(path)));
            Assert.Equal(["taskbar.json"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ModuleHelpersTests
{
    [Theory]
    [InlineData("HH:mm", false)]
    [InlineData("ddd d MMM  HH:mm", false)]
    [InlineData("HH:mm:ss", true)]
    [InlineData("T", true)]
    [InlineData("t", false)]
    [InlineData("G", true)]
    [InlineData("g", false)]
    public void Knows_when_a_format_shows_seconds(string format, bool expected) => Assert.Equal(expected, ModuleStates.ShowsSeconds(format));

    [Fact]
    public void Next_tick_lands_just_after_the_boundary()
    {
        var now = new DateTime(2026, 11, 24, 10, 30, 15, 250);

        Assert.Equal(TimeSpan.FromMilliseconds(44_750 + 20), ModuleStates.UntilNextTick(now, seconds: false));
        Assert.Equal(TimeSpan.FromMilliseconds(750 + 20), ModuleStates.UntilNextTick(now, seconds: true));
        Assert.Equal(TimeSpan.FromMilliseconds(60_000 + 20), ModuleStates.UntilNextTick(new DateTime(2026, 1, 1, 0, 0, 0), seconds: false));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(55, 5)]
    [InlineData(99, 9)]
    [InlineData(100, 10)]
    [InlineData(255, 10)]
    public void Battery_level_in_tenths(int percent, int level) => Assert.Equal(level, ModuleStates.BatteryLevel(percent));

    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData("Ethernet 2", "VirtualBox Host-Only Ethernet Adapter", true)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", false)]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", false)]
    public void Recognizes_virtual_adapters(string name, string description, bool expected) =>
        Assert.Equal(expected, ModuleStates.IsVirtualAdapter(name, description));
}
