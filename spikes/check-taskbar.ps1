# Spike helper: print taskbar visibility, auto-hide bit, work area and whether the state file is dirty.
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class TB {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
  [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint cb; public uint edge; public RECT rc; public IntPtr lParam; }
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string w);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint m, ref APPBARDATA d);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint p, ref RECT r, uint f);
  public static string Report() {
    var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(d);
    var st = (uint)SHAppBarMessage(4, ref d);
    var r = new RECT(); SystemParametersInfo(0x30, 0, ref r, 0);
    var tray = FindWindow("Shell_TrayWnd", null);
    return string.Format("trayExists={0} trayVisible={1} autoHide={2} workArea={3},{4},{5},{6}", tray != IntPtr.Zero, IsWindowVisible(tray), (st & 1) != 0, r.l, r.t, r.r, r.b);
  }
}
'@
"{0} stateDirty={1} explorer={2}" -f [TB]::Report(), (Test-Path "$env:APPDATA\Veneer-spike\taskbar-state.json"), ((Get-Process explorer -EA SilentlyContinue).Id -join ',')
