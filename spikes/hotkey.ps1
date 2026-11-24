# Dev helper: press Ctrl+Alt+Shift+<Key> (Veneer hotkeys: Q quit, R reload, T restore taskbar, S screenshot).
# -Escape presses Esc instead.
param([char]$Key, [switch]$Escape)
Add-Type @'
using System.Runtime.InteropServices;
public static class Kb { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra); }
'@
if ($Escape) { [Kb]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); [Kb]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero); return }
$mods = 0x11, 0x12, 0x10  # Ctrl, Alt, Shift
foreach ($m in $mods) { [Kb]::keybd_event($m, 0, 0, [UIntPtr]::Zero) }
[Kb]::keybd_event([byte][char]::ToUpper($Key), 0, 0, [UIntPtr]::Zero)
[Kb]::keybd_event([byte][char]::ToUpper($Key), 0, 2, [UIntPtr]::Zero)
foreach ($m in $mods) { [Kb]::keybd_event($m, 0, 2, [UIntPtr]::Zero) }
