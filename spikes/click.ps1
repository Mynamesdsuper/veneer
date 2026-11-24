# Dev helper: left click at screen coordinates (physical pixels), or press Escape with -Escape.
param([int]$X, [int]$Y, [switch]$Escape)
Add-Type @'
using System.Runtime.InteropServices;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, System.UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
}
'@
if ($Escape) { [Mouse]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [Mouse]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero); return }
[Mouse]::SetCursorPos($X, $Y) | Out-Null
[Mouse]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); [Mouse]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
