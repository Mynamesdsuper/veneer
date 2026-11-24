using Veneer.Core.Modules;

namespace Veneer.Core.Tests.Modules;

public class ModuleStatesTests
{
    [Theory]
    [InlineData(0.5f, true, "muted")]
    [InlineData(0f, false, "off")]
    [InlineData(0.2f, false, "low")]
    [InlineData(0.5f, false, "medium")]
    [InlineData(1f, false, "high")]
    public void Volume_states(float level, bool muted, string expected) => Assert.Equal(expected, ModuleStates.Volume(level, muted));

    [Theory]
    [InlineData(3, false, "empty")]
    [InlineData(20, false, "low")]
    [InlineData(50, false, "medium")]
    [InlineData(85, false, "high")]
    [InlineData(100, false, "full")]
    [InlineData(40, true, "charging")]
    public void Battery_states(int percent, bool charging, string expected) => Assert.Equal(expected, ModuleStates.Battery(percent, charging));

    static ModuleStates.Adapter A(bool up = true, bool wireless = false, bool loopback = false, bool gateway = true) => new(up, wireless, loopback, gateway);

    [Fact]
    public void Network_prefers_wired_and_needs_a_gateway()
    {
        Assert.Equal("ethernet", ModuleStates.Network([A(wireless: true), A()]));
        Assert.Equal("wifi", ModuleStates.Network([A(wireless: true), A(gateway: false)]));
        Assert.Equal("offline", ModuleStates.Network([A(up: false), A(loopback: true), A(wireless: true, gateway: false)]));
        Assert.Equal("offline", ModuleStates.Network([]));
    }

    static readonly Dictionary<string, string> NoNames = [];

    [Theory]
    [InlineData("Progman", null, null, null, "Desktop")]
    [InlineData("Shell_TrayWnd", @"C:\Windows\explorer.exe", "Windows Explorer", "", "Desktop")]
    [InlineData("Notepad", @"C:\Apps\Notepad.exe", "Notepad.exe", "x.txt - Notepad", "Notepad")]
    [InlineData("Chrome_WidgetWin_1", @"C:\Apps\msedge.exe", " Microsoft Edge ", "Tab", "Microsoft Edge")]
    [InlineData("X", @"C:\Apps\tool.exe", null, "Title", "tool")]
    [InlineData("ApplicationFrameWindow", @"C:\Windows\System32\ApplicationFrameHost.exe", "Application Frame Host", "Calculator", "Calculator")]
    [InlineData("X", null, null, "Only title", "Only title")]
    public void Active_app_name(string? cls, string? exe, string? description, string? title, string expected) =>
        Assert.Equal(expected, ModuleStates.ActiveAppName(cls, exe, description, title, "Desktop", NoNames));

    [Fact]
    public void Theme_names_win_by_exe_file_name()
    {
        var names = new Dictionary<string, string> { ["explorer.exe"] = "Files" };
        Assert.Equal("Files", ModuleStates.ActiveAppName("CabinetWClass", @"C:\Windows\EXPLORER.EXE", "Windows Explorer", "Documents", "Desktop", names));
    }
}
