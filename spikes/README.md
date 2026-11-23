# Phase 0 spikes

Throwaway code that answered the questions that could change Veneer's architecture. It is not part of
`Veneer.slnx` and CI does not build it. Results and decisions: [docs/decisions.md](../docs/decisions.md).

| Spike | Question |
|-------|----------|
| `OverlayBlur/` | Real blur and rounded corners behind a borderless, non-activating WPF window. |
| `AppBars/` | Two app bars at once; hiding the taskbar and restoring it on every exit path (watchdog, Explorer restart). |
| `Accent/` | Which API actually changes the accent color on Windows 11. |

`shot.ps1` takes a screenshot; `check-taskbar.ps1` prints the taskbar state and the work area.
`AppBars --restore` restores the taskbar by hand from the spike's state file.
