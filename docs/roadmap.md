# Roadmap

The full plan, phase by phase, with every feature as a checklist. The status table in the
[README](../README.md#project-status) summarizes it. Technical results and the reasons behind each choice are
in [decisions.md](decisions.md).

Legend:

- `[x]` done and tested.
- `[~]` proven in a Phase 0 spike, not yet in the app.
- Each Phase 3 item is tagged (3a) or (3b).
- `[ ]` planned.

Every phase ends with `dotnet build` and `dotnet test` green and one commit.

## Contents

- [Phase 1: repository skeleton](#phase-1-repository-skeleton): ✅ done
- [Phase 0: technical spikes](#phase-0-technical-spikes): ✅ done
- [Phase 2: theme engine and personalization layers](#phase-2-theme-engine-and-personalization-layers): ✅ done
- [Phase 3: layout engine and bars](#phase-3-layout-engine-and-bars): 🚧 3a done, 3b next
- [Phase 4: dock](#phase-4-dock)
- [Phase 5: Windows settings from themes](#phase-5-windows-settings-from-themes)
- [Phase 6: profiles, settings, tray, install, autostart](#phase-6-profiles-settings-tray-install-autostart)
- [Phase 7: bundled themes](#phase-7-bundled-themes)
- [Phase 8: polish and first release](#phase-8-polish-and-first-release)
- [After the first release](#after-the-first-release)
- [Not verified yet](#not-verified-yet)

---

## Phase 1: repository skeleton

- [x] Solution with `Veneer.Core`, `Veneer.Interop` and `Veneer.App`, plus `Veneer.Core.Tests` (xUnit).
- [x] .NET 10, pinned SDK (`global.json`), `.editorconfig`, `.gitignore` (references, artifacts and user themes
  stay local).
- [x] MIT license, README, `CLAUDE.md` with the project rules.
- [x] GitHub Actions on `windows-latest`: build and test.

## Phase 0: technical spikes

Throwaway code in [`spikes/`](../spikes). The results are in [decisions.md](decisions.md#2026-11-23-phase-0-spikes).

**Blurred overlay window**
- [x] Borderless, topmost, hidden from Alt+Tab, never steals focus.
- [x] Real blur with the documented DWM acrylic backdrop.
- [x] Blur stays when other windows take focus.
- [x] Idle CPU at 0%.
- [x] Rounded corners: native up to 8 px, window region for larger radii.
- [x] Fallback found: `SetWindowCompositionAttribute`.

**App bars**
- [x] Two app bars at once (top and bottom), registered with `SHAppBarMessage`.
- [x] The work area shrinks with the bars and comes back after exit.
- [x] Bar position pinned against the system moving it.

**Taskbar**
- [x] Hidden: auto-hide, `SW_HIDE`, and an event hook that hides it again when Explorer shows it.
- [x] Restored on normal exit.
- [x] Restored after an unhandled exception.
- [x] Restored after `FailFast` and `taskkill /f`, by the watchdog process.
- [x] Restored at next start when the state file is dirty.
- [x] Explorer restart: hide again and re-register the app bars.

**Accent color**
- [x] Working API found: `SetUserColorPreference`.
- [x] Snapshot and restore, byte for byte.

## Phase 2: theme engine and personalization layers

Code: [`src/Veneer.Core/Themes`](../src/Veneer.Core/Themes). Format: [theme-format.md](theme-format.md).

**Theme model and parser**
- [x] Typed theme model: metadata, Windows settings, fonts, tokens, surfaces (bar, dock), modules, icons.
- [x] JSON with comments and trailing commas; size limit on `theme.json`.
- [x] `schemaVersion` check with a clear message.

**Layers**
- [x] Theme → variant (e.g. `dark`) → user overrides, merged in that order.
- [x] Surfaces matched by `id`; other arrays replaced as a whole; `null` resets to the default.
- [x] Origin of every value (theme, variant or user), for "reset to theme value".
- [x] Orphaned overrides kept, not applied, and reported.
- [x] `$token` references, `$$` for a literal `$`, errors for unknown tokens.

**Validation**
- [x] Types, ranges, enums, colors, unique surface ids, dock edges.
- [x] Unknown surface types and modules skipped with a warning (forward compatibility).
- [x] Readable error paths, e.g. `surfaces[id=dock].iconSize`.
- [x] Never throws on bad input.

**Safe files**
- [x] Asset paths: relative only, no `..`, drives, streams, symlinks or junctions; allowed extensions per field.
- [x] `.zip` install: zip-slip, links, file types, 100 MB / 30 MB / 2,000-file limits.
- [x] Validated before install; replaces the old version only on success.

**Runtime**
- [x] `ThemeManager`: keeps the previous theme when a load fails.
- [x] Built-in fallback theme.
- [x] Hot reload of the theme folder and the overrides file (debounced).

## Phase 3: layout engine and bars

Split in two steps: **3a** (foundations and the first visible bars) is done; **3b** (the remaining modules) is next.

**Interop: spike code moved into [`Veneer.Interop`](../src/Veneer.Interop)** (3a)
- [x] `AppBar`: register, position, pin the rect, `ABN_POSCHANGED`, remove on exit, re-register after an Explorer restart.
- [x] `Taskbar` (`ITaskbar`): snapshot, hide, re-hide hook, restore. Dirty state file in `%APPDATA%\Veneer\state\`.
- [x] `Watchdog` process (`--watchdog <pid>`), started before any window.
- [x] `TaskbarCreated`: hide again and re-register the bars.
- [x] `SurfaceWindow`: blurred surface window (DWM acrylic recipe) and corner rounding (DWM up to 8 px, window
      region above).
- [x] `ForegroundWatcher`: foreground window via a WinEvent hook (used by `active-app` in 3b).
- [x] Primary monitor bounds and DPI, global hotkeys, screen capture.
- [x] Per-monitor DPI awareness v2 (`app.manifest`).
- [x] CsWin32 for P/Invoke, x64 platform target. ARM64 build: later.

**Layout (`Veneer.Core`, tested)** (3a)
- [x] Surface rectangles per edge, stacked in theme order; side bars fit between top and bottom bars.
- [x] Primary monitor only, without assuming a single monitor (monitor offsets tested).
- [x] DIP → physical pixel conversion per monitor DPI.
- [x] Module options checked by name (`clock.format`, `spacer.size`); one place per module.

**Bars (`Veneer.App`)** (3a)
- [x] Bar windows built from the theme, rebuilt on hot reload and on display changes.
- [x] Bars on any edge, with optional reserved space.
- [x] Start / center / end module zones (center stays centered on the whole bar).
- [x] Theme text style (font from the theme or the system, size, weight), padding, spacing, per-module style.
- [x] Command line until profiles exist: `--theme`, `--variant`, `--overrides`, `--keep-taskbar`, `--restore-taskbar`.

**Modules**
- [x] `clock`: format from the theme; wakes once per minute (per second if the format shows seconds). (3a)
- [x] `spacer`. (3a)
- [ ] `logo-menu`: theme icon and a system menu (About this PC, Settings, Sleep, Restart, Shut down, Lock).
      Destructive actions ask for confirmation. (3b)
- [ ] `active-app`: name of the foreground app and a fixed menu (Close window, Minimize, Quit). Real app menus
      are never read. (3b)
- [ ] `volume`: level and mute, with Core Audio. (3b)
- [ ] `network`: connection state. (3b)
- [ ] `battery`: shown only on devices with a battery. (3b)

**Safety, verified in the app** (3a)
- [x] Taskbar restored on normal exit (hotkey), unhandled exception (debug hotkey), `taskkill /f` (watchdog),
      next start (dirty state); hidden again after an Explorer restart.
- [x] Idle CPU 0.04% with two bars and a clock.

**Tools** (3a)
- [x] Hotkeys (Ctrl+Alt+Shift): **Q** quit, **R** reload theme, **T** restore taskbar; debug builds also **S**
      screenshot to `artifacts/screenshots/` and **X** simulated crash.
- [ ] Visual comparison with the reference screenshots. (3b)

## Phase 4: dock

**Placement and look**
- [ ] Dock on the bottom, left or right edge, styled by the theme.
- [ ] Separators.
- [ ] Optional auto-hide.

**Apps and windows**
- [ ] Pinned apps.
- [ ] Running apps: visible, unowned, not tool windows, not cloaked; grouped by process / AppUserModelID.
- [ ] Running indicator (dot or line).
- [ ] Click: launch, bring to front, or restore.
- [ ] MSIX apps launched through `shell:AppsFolder\<AUMID>`.

**Icons**
- [ ] High-resolution icons for exe and lnk files (`IShellItemImageFactory`).
- [ ] Icons for MSIX apps via AUMID.
- [ ] Theme `iconOverrides` and `iconFolder` take priority over system icons.

**Interaction**
- [ ] Hover magnification: intensity and radius from the theme; the math lives in `Veneer.Core` with tests.
- [ ] Context menu: Keep in Dock, Remove, Close.
- [ ] Drag to reorder.
- [ ] Pinned apps and their order saved as user overrides, so they survive theme switches.

**Performance**
- [ ] Smooth at 60 fps, measured.

## Phase 5: Windows settings from themes

**Settings**
- [ ] Wallpaper (`IDesktopWallpaper`).
- [ ] Light/dark mode: `AppsUseLightTheme` / `SystemUsesLightTheme`, then broadcast `ImmersiveColorSet`.
- [~] Accent color with `SetUserColorPreference`.
- [ ] Ordinal check, with "not supported on this Windows build" as the fallback.
- [ ] Decide whether to also write `ColorizationColor` (title bars).
- [ ] `appearance: system`: pick the light or dark variant from the Windows mode.

**Safety and control**
- [ ] One switch per setting: the user can turn each one off.
- [ ] Snapshot of the originals before the first change.
- [ ] "Restore original Windows settings".
- [ ] Changes stay after Veneer exits; the wallpaper does not jump back.

## Phase 6: profiles, settings, tray, install, autostart

**Profiles**
- [ ] A profile holds the theme, the variant, the overrides file and which Windows settings to apply.
- [ ] Create, rename, duplicate and delete profiles.
- [ ] Switch hot from the tray, the settings window or a hotkey.
- [ ] Stored in `%APPDATA%\Veneer\profiles\<name>\` (`profile.json` + `overrides.json`).

**Settings window**
- [ ] Shows each value's origin (theme or you), with "reset to theme value".
- [ ] Lists theme errors and warnings, and orphaned overrides.
- [ ] Install a theme from a `.zip` (UI on top of the Phase 2 installer).

**Tray icon**
- [ ] Open settings, switch profile, switch theme, restore taskbar, quit.

**Autostart**
- [ ] Start with Windows (`HKCU\...\Run`), off by default.

## Phase 7: bundled themes

**Bundled themes**
- [ ] `aqua-like`: top bar and bottom dock, original artwork.
- [ ] `strip`: a single bottom bar with apps, clock and status, no dock.
- [ ] A `LICENSE` / `CREDITS` file for each bundled theme.

**Fidelity and tools**
- [ ] Tool in `tools/`: convert chosen SVG icons to PNG, with an editable exe → icon map.
- [ ] Visual fidelity pass against private reference screenshots, with the remaining differences listed. Themes
      that use non-redistributable assets stay local and never enter the repository.

## Phase 8: polish and first release

- [ ] Performance pass: idle CPU about 0%, no stutter.
- [ ] Complete [theme-format.md](theme-format.md).
- [ ] Self-contained release build.
- [ ] First release.

## After the first release

Not built yet. The theme format already leaves room for these.

**Desktop**
- [ ] Desktop widgets: calendar, weather, battery, photos.
- [ ] Launcher / search.
- [ ] Full multi-monitor support.

**Themes**
- [ ] Live theme editor with preview.
- [ ] Theme gallery from an index on GitHub, installable from the app.
- [ ] More official themes.

**Automation**
- [ ] Automatic profile switching by time of day, power state or running apps.
- [ ] Accent color taken from the wallpaper.

## Not verified yet

These need hardware or conditions the test PC lacks. They are checked as soon as possible.

- [ ] More than one monitor (`Shell_SecondaryTrayWnd`, per-monitor work areas).
- [ ] Display scaling other than 100%.
- [ ] Logoff and shutdown while the taskbar is hidden.
- [ ] ARM64 build.
