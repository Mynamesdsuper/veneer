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
- [Phase 3: layout engine and bars](#phase-3-layout-engine-and-bars): ✅ done
- [Phase 4: dock](#phase-4-dock): ✅ done
- [Phase 5: Windows settings from themes](#phase-5-windows-settings-from-themes): ✅ done
- [Phase 6: look and feel](#phase-6-look-and-feel): ✅ done
- [Phase 7: profiles, settings, tray, install, autostart](#phase-7-profiles-settings-tray-install-autostart): ⏳ next
- [Phase 8: bundled themes](#phase-8-bundled-themes)
- [Phase 9: polish and first release](#phase-9-polish-and-first-release)
- [After the first release](#after-the-first-release)
- [Not verified yet](#not-verified-yet)
- [Ideas from FluidWin](#ideas-from-fluidwin)

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

Split in two steps: **3a** (foundations and the first visible bars) and **3b** (the remaining modules). Both are done.

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
- [x] `logo-menu`: theme icon and a system menu (About this PC, Settings, Sleep, Restart, Shut Down, Lock).
      Sleep, Restart and Shut Down ask for confirmation. (3b)
- [x] `active-app`: name of the foreground app (WinEvent hook), theme name overrides, and a fixed menu (Close
      Window, Minimize, Quit; Quit asks the app's windows to close, never kills). Real app menus are never read. (3b)
- [x] `volume`: level and mute through Core Audio callbacks (no polling), follows the default device; click
      mutes, wheel changes the level. (3b)
- [x] `network`: Wi-Fi / Ethernet / offline from .NET network events. (3b)
- [x] `battery`: level and charging from power events, hidden without a battery. Untested on real battery
      hardware (the test PC has none). (3b)
- [x] Per-state images from the theme (`icons`), with Windows icon-font glyphs as the fallback. (3b)
- [x] Menus with the Windows 11 look (WPF Fluent theme, follows light/dark). (3b)

**Safety, verified in the app** (3a)
- [x] Taskbar restored on normal exit (hotkey), unhandled exception (debug hotkey), `taskkill /f` (watchdog),
      next start (dirty state); hidden again after an Explorer restart.
- [x] Idle CPU 0.04% with two bars and a clock.

**Tools** (3a)
- [x] Hotkeys (Ctrl+Alt+Shift): **Q** quit, **R** reload theme, **T** restore taskbar; debug builds also **S**
      screenshot to `artifacts/screenshots/` and **X** simulated crash.
- [x] Debug `--self-test "<steps>"`: drives the app (open/close menus, change the foreground app, screenshot,
      quit) without simulated input. (3b)
- [x] Visual comparison with the reference screenshots: the remaining gaps are listed under Phase 8. (3b)

## Phase 4: dock

Split like Phase 3: **4a** (a working dock) and **4b** (magnification, menus, drag, persistence). Both are done.

**Placement and look**
- [x] Dock on the bottom, left or right edge, centered, with a margin from the edge; blurred plate, border,
      corner radius (window region above 8 px), padding, spacing. (4a)
- [x] Reserved space, so maximized windows stay clear; the window resizes as apps open and close. (4a)
- [x] Placed after the bars (a bottom bar and a bottom dock stack). (4a)
- [x] Separators: `"---"` in the pinned list, and one before the running apps (`separateRunning`). (4b)
- [x] Optional auto-hide: an edge strip brings it back, it hides 0.6 s after the mouse leaves; no reserved space. (4b)
- [x] Bars and dock step aside while a full-screen app is in front (`ABN_FULLSCREENAPP`; needs a surface that
      reserves space). Not tested with a real full-screen app yet. (4b)

**Apps and windows**
- [x] Pinned apps: exe names (resolved like the Run dialog), full paths, AppUserModelIDs, `ms-settings:`. (4a)
- [x] Running apps: the taskbar's filter (visible, unowned or `WS_EX_APPWINDOW`, not tool windows, not cloaked);
      grouped by AppUserModelID (window property, then the packaged process), else by exe. (4a)
- [x] Live updates from WinEvent hooks (create, destroy, show, hide, cloak, foreground), debounced; no polling.
      Idle CPU 0.00% with a bar and a dock. (4a)
- [x] Running indicator (dot or line) in the theme's color. (4a)
- [x] Click: launch, or bring the most recently used window to the front, restoring it if minimized. (4a)
- [x] Store apps launched through `shell:AppsFolder\<AUMID>`. (4a)

**Icons**
- [x] High-resolution icons from the shell (`IShellItemImageFactory`) for exe files and AppUserModelIDs, with a
      fallback to the exe when an id is not in the Apps folder (Steam). (4a)
- [x] Store apps use their unplated package icon (as the taskbar does). (4a)
- [x] Clean edges: straight or premultiplied alpha detected per icon, high-quality downscaling. (4a)
- [x] Theme `iconOverrides` and `iconFolder` take priority over system icons. (4a)

**Interaction** (4b)
- [x] Hover magnification: intensity and radius from the theme, cosine falloff, the point under the cursor stays
      put; the math is in `Veneer.Core.Dock.Magnifier` with tests. Icons are drawn in a transparent window owned by
      the plate, so they grow out of the dock and empty pixels let clicks through.
- [x] Context menu: app name, Keep in Dock / Remove from Dock, Close (Close All Windows).
- [x] Drag to reorder pinned apps. Logic tested in Core; the mouse gesture itself still needs a hand test.
- [x] Pinned apps and their order saved to the user's overrides (`defaultPinned`), written safely (temp file and
      swap, never over a file it cannot read); the theme hot-reloads from it.

**Performance** (4b)
- [x] Magnification measured with a scripted sweep: 63 fps over 2 s, one 33 ms frame. Rendering work capped at
      about 120 updates per second; no work at all while idle.
- [x] The plate stretches with the magnified icons, as on macOS. (Phase 6a)

## Phase 5: Windows settings from themes

**Settings**
- [x] Wallpaper (`IDesktopWallpaper`), on every monitor, position Fill.
- [x] Light/dark mode: `AppsUseLightTheme` / `SystemUsesLightTheme`, then broadcast `ImmersiveColorSet`.
- [x] Accent color with `SetUserColorPreference` (Windows rebuilds the palette apps see).
- [x] Ordinal check, with "not supported on this Windows build" as the fallback.
- [x] `ColorizationColor` (title bars with "show accent color") written too, and saved in the snapshot.
- [x] The theme's light or dark variant follows the mode Veneer sets, or Windows' own mode when following the
      system, and switches when the mode changes.

**Safety and control**
- [x] One choice per setting, all "from the theme" by default: wallpaper and accent from the theme or the system;
      light/dark from the theme, light, dark or the system. In `profiles\default\windows.json` until the settings
      window (Phase 7).
- [x] Snapshot of the originals before the first change of each setting, never overwritten while it exists.
- [x] "Restore original Windows settings": `Veneer.App --restore-windows-settings` (a button in Phase 7).
      Tested on the real PC: accent, palette and light/dark come back byte for byte; the wallpaper too.
- [x] Changes stay after Veneer exits; the wallpaper does not jump back.
- [x] Applied off the UI thread: the change notification waits for every app (seconds), the bars do not.

**Hardening** (ideas from [FluidWin](#ideas-from-fluidwin))
- [x] Emergency exit hotkey registered by the **watchdog** (Ctrl+Alt+Shift+F12): it ends a hung Veneer and
      restores the taskbar. Tested.
- [x] "End process tree" from Task Manager: check that it does not take the watchdog down too, and fix it if it
      does (the taskbar must come back).
- [x] Calls to other apps' windows (close, titles, activation) never block: `SendMessageTimeout` or async, so a hung
      app cannot freeze the bars or the dock.
      Done in the code review of 2026-11-24: the watchdog is started through a short-lived launcher, so it is not
      in Veneer's process tree (tested with `taskkill /T /F`); window close uses `PostMessage`, restore and minimize
      use `ShowWindowAsync`, titles are read without sending messages to other processes.
- [x] RunOnce safety net: a `HKCU\...\RunOnce` entry restores the taskbar at the next sign-in after a power loss
      or a crash of both processes; removed after every restore. (Code review, 2026-11-24.)

## Phase 6: look and feel

Added on 2026-11-25 at the author's request: the default look was too bare. **6a** teaches the engine to draw a
polished, macOS-like interface; **6b** ships a real default theme built on it.

**6a: what the engine can draw** (all from the theme)
- [x] Rounded highlight behind bar modules: on hover, and stronger while their menu is open (`style.hover`,
      `pressed`, `highlightRadius`, `highlightInset`).
- [x] Menus in the theme's style (`menu`): background, border, corner radius, font, item highlight, separators,
      optional blur (acrylic through `SetWindowCompositionAttribute`, since WPF menus are per-pixel transparent).
      Without `menu` the Windows 11 look stays. A style WPF cannot use never takes the shell down.
- [x] The dock's name label above the hovered icon (`label`), instead of a Windows tooltip.
- [x] Launch bounce of a dock icon (`bounceOnLaunch`).
- [x] The dock plate stretches with the magnified icons (72 fps in the scripted sweep).
- [ ] Animated dock auto-hide (slide) and fades for the label.

**6b: the default theme** (style chosen by the author: macOS-like, original artwork)
- [x] `themes/aqua` in the repository: glass menu bar on top, magnifying dock at the bottom, themed menus and dock
      label, launch bounce; light and `dark` variants following Windows' mode. Tested to load with no diagnostics.
- [x] Original artwork only: wallpapers (dawn and dusk) and logo generated by `tools/aqua-art/make-art.ps1`, MIT;
      status icons from Windows' Segoe Fluent Icons at run time.
- [x] Used when no theme is chosen (copied next to the executable); the built-in theme stays only as the emergency
      fallback.
- [x] Screenshots in light and dark; Windows settings restored byte for byte afterwards.
- [x] Packaged apps show their Windows name in the bar ("Terminal", not "Windows Terminal Host").
- [x] Restyled after the author's reference image (`references/referenceestetica.png`, aesthetic only): smoked
      slate glass, light-catching gradient edges (`borderHighlight`), larger radii, monochrome highlights, new
      silky slate / ice wallpapers. The widgets in that image stay in "After the first release".

**Fixes before Phase 7** (2026-11-24)
- [x] No shadow band under the top bar, and the bar exactly as thick as the theme says.
- [x] Truly rounded dock glass at any radius (composition), with or without blur; no square corners.
- [ ] Quitting while a bar menu is open hangs the process after its cleanup (the taskbar is already restored).

## Phase 7: profiles, settings, tray, install, autostart

**Profiles**
- [ ] A profile holds the theme, the variant, the overrides file and which Windows settings to apply.
- [ ] Create, rename, duplicate and delete profiles.
- [ ] Switch hot from the tray, the settings window or a hotkey.
- [ ] Stored in `%APPDATA%\Veneer\profiles\<name>\` (`profile.json` + `overrides.json`).

**Settings window**
- [ ] Shows each value's origin (theme or you), with "reset to theme value".
- [ ] Lists theme errors and warnings, and orphaned overrides.
- [ ] Install a theme from a `.zip` (UI on top of the Phase 2 installer).
- [ ] Diagnostics report: version, Windows build, monitors and DPI, active theme id, last theme-load diagnostics and
      the last log lines, ready to paste in an issue. No personal paths. (FluidWin)
- [ ] All UI strings in resource files from the start. English and Italian ship; more languages from the
      community. (FluidWin)

**First run**
- [ ] Offer to import the Windows taskbar's pinned apps into the dock (user overrides), Store apps included,
      deduplicated by AppUserModelID (Discord changes its exe folder on every update). (FluidWin)

**Tray icon**
- [ ] Open settings, switch profile, switch theme, restore taskbar, quit.

**Autostart**
- [ ] Start with Windows (`HKCU\...\Run`), off by default.

## Phase 8: bundled themes

**Bundled themes**
- [ ] `aqua-like`: done as the default theme `aqua` in Phase 6b.
- [ ] `strip`: a single bottom bar with apps, clock and status, no dock.
- [ ] A `LICENSE` / `CREDITS` file for each bundled theme.

**Fidelity and tools**
- [ ] Tool in `tools/`: convert chosen SVG icons to PNG, with an editable exe → icon map.
- [ ] Visual fidelity pass against private reference screenshots, with the remaining differences listed. Themes
      that use non-redistributable assets stay local and never enter the repository.
- [x] Theme styling for module hover and pressed states, and for menus: done in Phase 6a.

**Dock items the reference themes need** (FluidWin)
- [ ] One dot per window (up to 3, theme setting `runningIndicator.mode`) and the app's window list in the dock
      menu.
- [ ] Downloads stack: a dock item that shows the latest downloaded files on click.
- [ ] Recycle Bin: a dock item that shows empty/full and accepts dropped files.

## Phase 9: polish and first release

- [ ] Performance pass: idle CPU about 0%, no stutter.
- [ ] Complete [theme-format.md](theme-format.md).
- [ ] Self-contained release build.
- [ ] First release.

## After the first release

Not built yet. The theme format already leaves room for these.

**Desktop**
- [ ] Desktop widgets: calendar, weather, battery, photos.
- [ ] Launcher / search: apps, files and folders (Windows Search index), settings, quick math. (FluidWin)
- [ ] Full multi-monitor support; the dock can follow the mouse to another monitor. (FluidWin)

**New surface types** (FluidWin)
- [ ] Island (`"type": "island"`): a small pill that grows on events (volume, notifications, music, timer, file
      shelf, microphone/camera in use). The theme decides whether there is one and how it looks.
- [ ] Volume OSD drawn by the theme, replacing the Windows volume popup.
- [ ] Control center panel, opened from a bar or dock module: Wi-Fi, Bluetooth, dark mode, keep awake, volume mixer,
      clipboard history, calendar. Online services (weather, news) optional.

**Dock** (FluidWin)
- [ ] Live window previews on hover (`DwmRegisterThumbnail`), styled by the theme.
- [ ] Jump lists (new window, recent files) in the dock menu. Needs a spike first: there is no simple public API to
      read other apps' jump lists.
- [ ] Notification badges on dock icons (`UserNotificationListener`; Windows asks the user for permission).

**Updates**
- [ ] Opt-in update check against GitHub releases: verify the downloaded file's hash, never update silently.
      (FluidWin)

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

## Ideas from FluidWin

Source: https://github.com/clooudfn/FluidWin (read on 2026-11-24, version 1.3.1). A single-person C++ project
that sits on top of Windows 11: a dock instead of the taskbar, a "notch", a control center, window animations, its
own snapping, right-click menu, search, Alt+Tab overview and custom cursors. It restores Windows when it quits,
crashes or is killed.

**License: MIT.** Ideas are free to use. If Veneer ever ports non-trivial logic from it (not just the idea or a
documented Windows API call), its copyright notice goes into `THIRD-PARTY-NOTICES.md`.

**Main difference:** FluidWin is one fixed design with settings. Veneer is an engine where themes decide layout and
look, so every idea taken from it is shown by the theme and can be turned off by the user.

**Picked by the author on 2026-11-24** (all candidates), tagged "FluidWin" in the phases above:

| Where | Items |
|-------|-------|
| Phase 5 | Emergency exit in the watchdog, "End process tree" test, non-blocking calls to other apps' windows. |
| Phase 7 | Diagnostics report, UI strings in resources (English + Italian), import of the taskbar's pinned apps. |
| Phase 8 | One dot per window and window list, Downloads stack, Recycle Bin. |
| After the first release | Island, volume OSD, control center, launcher/search, dock following the mouse across monitors, live window previews, jump lists, notification badges, update check. |

**Design notes (not separate items):**
- Load icons only for visible dock items; keep idle memory low (FluidWin loads its app list lazily).
- Full-screen detection: `SHQueryUserNotificationState` (`QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN`,
  `QUNS_PRESENTATION_MODE`) as a second signal next to `ABN_FULLSCREENAPP`.
- A code review at the end of every phase (FluidWin found more than 150 problems in one review). Already a rule in
  `CLAUDE.md`.

**Not for Veneer** (they conflict with settled decisions):
- Window animations (genie, jelly, bounce), its own snapping, a replacement right-click menu in File Explorer: they
  restyle or take over other apps' windows, which is a non-goal, and they break most easily with Windows updates.
- Custom cursors: a theme may change only wallpaper, accent color and light/dark mode.
- Turning off Windows notification banners: it stays off until the user signs out, so it cannot be undone at once.
