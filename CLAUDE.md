# Veneer: rules for Claude Code

Talk to the author in **Italian**, in plain words (they are not necessarily a Win32 expert).
Write code, comments, commit messages, docs and issues in **English**.

The full design brief is `PROMPT.md` (Italian). It is local-only and gitignored, so it may be missing
on other machines. The rules below are the persistent part and always apply.

## Settled decisions (do not reopen)

- C# + WPF on **.NET 10**, Windows 11 (22H2+) only. MIT license.
- Themes decide **look and layout**: surfaces (`bar`, `dock`, later widgets) on screen edges, bars made of
  named modules. Themes are **data only** (`theme.json` + png/ico/jpg/ttf/otf). A theme never runs code.
- Layers, applied in order: theme → variant (light/dark) → user overrides. Surfaces match by `id`.
  Switching or updating a theme never deletes user overrides. Profiles are in the MVP.
- A theme may change only: wallpaper, accent color, light/dark mode. Each can be turned off by the user.
- Bar app zone: active app name + fixed menu. No reading of other apps' real menus.

## Hard rules

- The Windows taskbar must always come back: on exit, on unhandled exception, on next start if the state
  file is dirty, and from the tray menu. Handle Explorer restarts (`TaskbarCreated`).
- Before any change to the system (taskbar, registry, wallpaper, accent, autostart), snapshot the original
  and make sure the restore path exists and is tested.
- Theme loader: bad JSON or values → readable error and fallback, never a crash. Unknown surface/module
  types → skipped with a warning. Relative paths only, confined to the theme folder (no `..`, absolute
  paths or symlinks). Zip-slip protection and size limits. Allowed extensions only.
- Never commit Apple or other non-redistributable assets. `references/`, user themes and icon sources stay
  local. Never copy code or assets from WacOS (source-available license that forbids reuse).
- Never disable Windows security features or ask the author to.
- Event-driven (WinEvent hooks), no tight polling. Idle CPU ~0%. Per-monitor DPI v2.

## Code layout

- `Veneer.Core`: no WPF/Win32. All testable logic goes here, with xUnit tests in `tests/Veneer.Core.Tests`.
- `Veneer.Interop`: thin Win32 layer behind interfaces. Prefer `Microsoft.Windows.CsWin32` for P/Invoke.
  No P/Invoke anywhere else.
- `Veneer.App`: WPF surfaces, modules, settings, tray.
- Values that make a theme look like something belong in the **theme**, not in code. If code needs a
  constant to match a reference, extend the theme format instead.
- No new dependency when the framework is enough.

## Workflow

- Work phase by phase (see README roadmap). Before each phase, present a short plan and wait for the
  author's OK. Each phase ends with `dotnet build` and `dotnet test` green and one commit.
- Ask one question at a time, only when the answer changes what gets built. Otherwise decide, state the
  choice, and continue.
- Visual checks: compare surfaces against `references/` using the Debug screenshot hotkey (output in
  `artifacts/screenshots/`) and report remaining differences.

## Commands

```powershell
dotnet build
dotnet test
dotnet run --project src/Veneer.App
```
