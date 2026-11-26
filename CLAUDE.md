# Veneer: rules for Claude Code

**At every start and every resume, run the start-of-session check below before anything else.**

Talk to the author in **Italian**, in plain words (they are not necessarily a Win32 expert).
Write code, comments, commit messages, docs and issues in **English**.

The full design brief is `PROMPT.md` (Italian). It is local-only and gitignored, so it may be missing
on other machines. The rules below are the persistent part and always apply.

## Start-of-session check (always, before any edit)

Run these and read the output:

```powershell
git status --short
git log --oneline -5
git log origin/main..main --oneline     # commits not pushed yet
gh run list --limit 1                   # last CI run
Get-Process Veneer.App -ErrorAction SilentlyContinue   # a running Veneer locks the build output
Test-Path "$env:APPDATA\Veneer\state"   # then list it: a leftover dirty taskbar state means a crash last time
```

Then read:

1. `docs/roadmap.md`: which phase is next, and the items carried over.
2. Your memory files for this project (status, carried-over items, author preferences).
3. The newest entry in `docs/decisions.md`.

Then report to the author **in Italian, in 5 lines at most**: current phase and next step, uncommitted changes,
unpushed commits, CI state, anything left dirty (taskbar state, running Veneer). Wait for the author before
starting work.

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
- Never disable Windows security features or ask the author to.
- Event-driven (WinEvent hooks), no tight polling. Idle CPU ~0%. Per-monitor DPI v2.
- The UI thread never waits for the disk, the registry or another process.
- Costly effects (glass, animations) have a cheap fallback, used when Windows transparency effects are off, on
  battery saver and while a full-screen app or game is in front.
- Never load code into other processes. Never read other apps' pixels without the user turning it on.

## Assets and other projects

- **Never in the repo:** Apple or other non-redistributable assets, the local "macOS Golden Gate" theme,
  `references/`, icon sources, user themes.
- **MacTahoe icons** (GPL-3.0): local only, for the author's theme. Convert them with a tool in `tools/` that never
  writes into the repo.
- **WacOS** (source-available, reuse forbidden): look only. Copy no code, assets, shaders, algorithms or constants,
  and do not paraphrase them. Take only product ideas and the names of public Windows APIs, then build from
  Microsoft's docs and our own measurements. The analysis and the picked ideas are in `docs/roadmap.md` (tag "WacOS").
- **FluidWin** (MIT): ideas are fine. Porting real logic needs its copyright notice in `THIRD-PARTY-NOTICES.md`.
  The analysis and the picked ideas are in `docs/roadmap.md` (tag "FluidWin").

## Code layout

- `Veneer.Core`: no WPF/Win32. All testable logic goes here, with xUnit tests in `tests/Veneer.Core.Tests`.
- `Veneer.Interop`: thin Win32 layer behind interfaces. Prefer `Microsoft.Windows.CsWin32` for P/Invoke.
  No P/Invoke anywhere else.
- `Veneer.App`: WPF surfaces, modules, settings, tray.
- Values that make a theme look like something belong in the **theme**, not in code. If code needs a
  constant to match a reference, extend the theme format instead.
- No new dependency when the framework is enough.

## Workflow

- Work phase by phase (see `docs/roadmap.md`). Before a phase: a short plan in Italian, then wait for the
  author's OK.
- During a phase: ask one question at a time, only when the answer changes what gets built. Otherwise decide,
  state the choice, and continue.
- Visual checks: compare surfaces against `references/` using the Debug screenshot hotkey (output in
  `artifacts/screenshots/`) and report remaining differences.
- Fidelity: measure a look value in a reference (size, radius, blur, color) before putting it in a theme, and note
  where it was measured in `docs/decisions.md`. No guessed numbers.
- README: keep it in step with what works. Every screenshot shows real output; every claim about safety, network or
  performance is checked or measured.
- End of a phase, in this order:
  1. `dotnet build` and `dotnet test` green.
  2. A code review of the phase's diff, and fixes for the real problems found.
  3. Tick `docs/roadmap.md`, update the README status table, add the decisions to `docs/decisions.md`.
  4. Update your memory (status, carried-over items).
  5. One commit.
  6. **Ask the author before every push.** Never push without a yes in this session.
- End of session: update your memory with the status (phase, last commit, what is not pushed, what is half
  done), and tell the author in one or two lines where things stand.

## Do not step on other sessions

- The author also works with Claude in the **desktop app**. It may have changed files since your last session.
  If `git status` shows changes you did not make, **do not revert them**: tell the author and ask.
- Memory files can be edited by both. Re-read a memory file right before editing it.
- If the git history differs from what your memory says, the git history is right. Fix the memory.

## Known traps

- **Two different `%APPDATA%` folders.** The Claude desktop app is a packaged (MSIX) app. Files that a desktop-app
  session writes under `%APPDATA%` are silently redirected to
  `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\`, which neither this terminal nor Veneer sees. If something
  "in `%APPDATA%\Veneer`" is missing, look there before saying it does not exist.
- **Folder access.** Folders outside the project (like `%APPDATA%\Veneer`) need `/add-dir` before you can read
  them. "I cannot read it" is not the same as "it does not exist".
- **Build locked.** If `dotnet build` fails because `Veneer.App.exe` is in use, quit Veneer with
  Ctrl+Alt+Shift+Q (the taskbar comes back). Never kill it without making sure the taskbar is restored.
- **Real-system tests** (taskbar, Explorer restart, accent, wallpaper) change the author's PC. Before each one,
  make sure the restore path works. After it, check that the taskbar, the work area and the settings are back.

## Commands

```powershell
dotnet build
dotnet test
dotnet run --project src/Veneer.App
```
