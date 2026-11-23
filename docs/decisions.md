# Technical decisions

Record each decision with its date, context, the options considered and the outcome.

## 2026-11-23: Stack

C# + WPF on .NET 10 (LTS), Windows 11 only. .NET 8 reached end of support on 2026-11-10.

## Open: to be settled by the Phase 0 spikes

- Real blur behind borderless WPF windows: DWM system backdrop vs `SetWindowCompositionAttribute`.
- Several app bars at once (e.g. top and bottom) with `SHAppBarMessage`.
- Hiding the taskbar safely and restoring it in every exit path, including Explorer restarts.
- The right API for the accent color on current Windows 11.
