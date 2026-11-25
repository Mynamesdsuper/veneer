# Technical decisions

Record each decision with its date, context, the options considered and the outcome.

## 2026-11-23: Stack

C# + WPF on .NET 10 (LTS), Windows 11 only. .NET 8 reached end of support on 2026-11-10.

## 2026-11-25: The Strip theme, the apps module and floating bars (Phase 8a)

- **Strip = one floating bar, no dock.** Menu on a tile at the start, pinned and running apps in the center, status
  and clock (on a tile) at the end. Original art from the same generator as Aqua, now `tools/theme-art`
  (`-Theme aqua|strip`): dusk and dawn hills. From the author's taskbar inspiration only the cheap, data-only parts
  were taken (a floating pill and per-module tiles); widgets, folder tiles and the glass material stay for later.
- **The `apps` module shares the dock's logic.** Pinned + running apps, activation, pin/unpin/close and the overrides
  write moved from `DockWindow` to `AppList`, used by the dock and the module. The module's pinned list is the bar's
  `defaultPinned` (a surface key, not a module option): module lists are arrays and an override would replace the
  whole `start`/`center`/`end` list, freezing the theme's modules. `ThemeChanges.OnlyPinnedChanged` covers bars, so
  pinning in a bar updates it in place. The taskbar import fills docks and apps bars; its texts no longer say "dock".
- **Floating bars.** `margin` > 0: the shell reserves `thickness + margin` and the window sits `margin` away from the
  edge and both ends (`SurfaceLayout.Float`), through `AppBar.Place`'s fit callback. `cornerRadius` ≤ 8 uses DWM
  rounding and keeps the DWM shadow; above 8 the bar is drawn like the dock: per-pixel transparent window plus a
  `GlassPlate`. A `GlassPlate` hides its own window's WPF content, so then the modules live in a second transparent
  window owned by the bar and laid over it (the dock does the same with its icons); it follows the bar on shell moves
  (`AppBar.Moved`) and hides with it for full-screen apps.
- **Tiles.** A module `style.background` (+ `style.cornerRadius`) wraps it in a tile that spans the bar minus
  `highlightInset`; a clickable module's hover fills the tile.
- **Not done here:** drag to reorder in the `apps` module, a distinct indicator for the app in front.
- Self-test steps: `bar-shot` (screen copy of the bars only, plus 4 px), `bar-unpin:<text>`.

## 2026-11-25: Settings polish and Veneer's own window frame (Phase 7d)

- **One fixed Veneer frame, not a themed one** (the author's choice). Veneer's windows (settings, the taskbar import
  offer, the profile name dialog) share `VeneerWindow`: a 40 px title bar with the app icon, the title and Windows
  11-style caption buttons (Segoe Fluent Icons; Close turns red). A frame styled by the theme would need a theme
  format section; it can come later.
- **WindowChrome, glass frame 0.** WPF's `WindowChrome` keeps the native frame's behavior (move, resize edges, Aero
  snap, Win+arrows, Alt+Space system menu, double-click to maximize). With the DWM glass extended into the window
  (`GlassFrameThickness -1`), DWM paints its own caption under Veneer's: the accent-colored strip and the old
  caption buttons show through, and the window background turns black. With glass 0 the window is opaque and draws
  the Fluent `ApplicationBackgroundBrush` itself; no Mica.
- **Rounded corners must be asked for.** With the client area covering the whole frame, Windows 11 drew square
  corners and no border; `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND` brings the rounded corners and the 1 px border
  back. The DWM shadow stays (pixels just outside the edge are darker than with the window closed).
- **Snap layouts.** Windows 11 shows the snap layouts flyout only where `WM_NCHITTEST` answers `HTMAXBUTTON`. The
  window answers it over its Maximize button; the clicks then arrive as non-client messages WPF never routes, so
  hover, press, click and double click of that one button are handled in the window procedure. Checked by the
  self-test step `frame-hittest` (sends `WM_NCHITTEST`: 9 on Maximize, 2 on the title bar, normal and maximized); the
  flyout itself needs a real mouse.
- **Maximized**: a maximized window is larger than the screen by its invisible resize edges, so the content gets
  that margin (`SystemParameters.WindowResizeBorderThickness`).
- **Settings layout.** Windows 11 navigation (icons, a subtle fill and a small accent bar instead of a solid accent
  block); every page on cards; pages fill up to 820 px and stay on the left. Where a value comes from is a small line
  under its name, and "Reset" shows only on the values you changed (a column of disabled buttons said nothing).
- **Screenshots without the screen.** `settings-render` draws the window with WPF instead of copying the screen, so no
  other window (the author's chats, games) can end up in a picture; `settings-mode:light|dark` switches that window
  only, so the README picture of both modes no longer changes the Windows mode. Renders have no shadow or rounded
  corners.

## 2026-11-25: Importing the taskbar's pinned apps (Phase 7c)

- **Source: the taskbar's own record.** Store apps pinned to the taskbar have no shortcut file; only
  `HKCU\...\Explorer\Taskband\FavoritesResolve` lists every pin, in taskbar order. It is a run of serialized shell
  links, each after its 4-byte size; Windows' own `ShellLink` object reads each one (`IPersistStream.Load`), so no
  undocumented format is decoded by hand. Desktop pins point at the shortcut files of `User Pinned\TaskBar` (or
  `ImplicitAppShortcuts`), which are read in turn. If the record cannot be read, the shortcut files are the fallback
  (desktop apps only, logged). Nothing on the system is written.
- **Which entry each pin becomes.** Running windows of desktop apps often do not carry the id their shortcut sets
  (Discord, Spotify, Explorer windows have none; Zen and Steam carry ids without `!`, which the pinned-list format
  reads as exe names). So: Store apps by AppUserModelID; desktop apps by the full path of their exe; Explorer (whose
  pin is a shell folder) as `explorer.exe`; other id-only desktop pins are skipped and logged.
- **Squirrel apps (Discord).** Their shortcut starts `Update.exe --processStart Discord.exe`, and the app runs from
  `app-<version>\`, a folder that changes on every update. The pin is the stub `...\Discord\Discord.exe`, which
  starts the newest version, and a full-path pin also matches the same exe one `app-*` folder below it. The name
  from the arguments may not leave the updater's folder. "Keep in Dock" on a running Squirrel app pins the stub too.
- **Replace, not merge** (the author's choice): the import replaces the dock's pinned list in the user's overrides
  (every dock of the theme), without duplicates; the theme's own list stays in the theme.
- **Offered once.** A small Fluent window (icons and names, Import / Not now) after the first theme load, when the
  profile's theme has a dock and the taskbar has something to import. The answer is stored as `pinImportOffered` in
  `settings.json`, next to the active profile; quitting Veneer with the window open is no answer. Not shown with
  `--theme` or during self-tests. Settings › Customize can run the import again.

## 2026-11-24: Settings window and translations (Phase 7b)

- **Built in code, in the Fluent style.** Like the bars and the dock, the window is built in C# (no XAML), and the
  app's `ThemeMode = System` gives it the Windows 11 look (Mica, Fluent controls) with no new dependency. Six pages:
  General, Profiles, Theme, Customize, Windows, Diagnostics. Opened from "Veneer Settings…" in the Veneer menu; the
  logo menu's own "Settings…" (Windows Settings) became "System Settings…" so the two are not confused.
- **One path for every change.** The window writes the same files as the menus (the profile's `overrides.json`
  and `windows.json`, the profile store); hot reload applies the change and `App.Changed` rebuilds the page. A
  slider saves when the drag ends (or 0.6 s after a key press), because every save rebuilds the bars. A value set
  back to what the theme says is not written, so it never turns into "yours" by accident.
- **Customize shows the main values only** (position, thickness, reserved space, icon size, magnification,
  auto-hide). An editor for every key belongs to the live theme editor, after the first release. Their ranges come
  from the theme loader's own checks.
- **Nested overrides.** `OverridesFile` now takes dotted keys (`magnification.maxScale`), so changing one value of an
  object keeps the theme's other values in it; a reset removes the objects it leaves empty, and the surface entry
  when only its id is left. Orphaned surfaces can be removed from the Customize page.
- **Translations.** Every UI text is in `Strings.resx` (English) and `Strings.it.resx` (Italian), in `Veneer.App`
  and, for the profile messages, in `Veneer.Core`. The language follows Windows. The MSBuild strongly-typed resource
  generation (`StronglyTypedLanguage`) did not run in the SDK build, so a small hand-written `Strings` class reads each
  key by its property name; a test checks that both languages have the same keys and placeholders and that every key
  has a property. **Theme diagnostics stay in English**: they are for theme authors and match `theme-format.md`.
- **Diagnostics report** in English (anyone can read the issue), with the user folder replaced by `%USERPROFILE%` and
  the user and PC names by placeholders (whole words only, at least 3 letters, the PC name first because it often
  contains the user name). It includes the last 40 log lines, which can name running apps; the user sees the whole
  text before copying it.
- **Tested in a sandbox** (`VENEER_HOME`, a copy of Aqua without its `windows` section, `--keep-taskbar`): every page,
  a value set and reset, installing a `.zip` over the active theme while it is watched (works: the watcher does not
  lock the folder), installing a new theme, and renaming the active profile (its folder moves; the session reloads
  from the new place). New self-test steps drive the window without simulated input.

## 2026-11-24: Profiles, Veneer menu, autostart (Phase 7a)

- **Profile layout.** One folder per profile under `profiles\`: `profile.json` (the theme, by folder name),
  `overrides.json`, and `windows.json` (the Phase 5 file, which already held the light/dark choice, so a profile's
  "variant" lives there). The active profile is in `settings.json` at the root. `default` always exists; an existing
  install needs no migration (no `profile.json` means the default theme).
- **Names are folder names**, checked before any path is built (no separators, `..`, reserved device names, trailing
  dots or spaces): profile names come from the UI and theme names from an editable file.
- **Themes by name**: the user's `themes\` first, then the ones bundled next to the executable, so an installed copy
  wins. A missing theme falls back to `aqua`, then to the built-in theme.
- **`--theme` on the command line turns profiles off** for that run (tests and theme development).
- **A switch gives back what the new profile leaves to Windows.** When a setting is left to Windows (its choice is
  "system", or the theme does not set it) and the snapshot holds its original, that setting alone is restored and
  dropped from the snapshot. Before, the previous theme's wallpaper stayed until "Restore original Windows
  settings". Tested for real: the wallpaper came back and the snapshot was deleted.
- **Veneer menu.** The author chose (option 1): the same menu in the bar (logo menu → "Veneer ›") and on a
  notification-area icon. Veneer hides the taskbar, so the icon alone would rarely be visible. Themed menus have no
  submenus (their item template is only the text), so "Veneer ›" opens a second, flat menu in the same place, with
  profiles and themes listed under section titles and a check mark in the text.
- **Notification-area icon** through `Shell_NotifyIcon` (no WinForms: in a WPF project its types clash with WPF's).
  It has a fixed GUID, so Windows remembers the user's show/hide choice. Windows binds that GUID to the
  executable's path, so when another copy owns it (a Debug build), the icon falls back to the window + number
  identity.
- **App icon**: the Aqua logo's "V" on a slate rounded square, drawn by `tools/app-icon/make-icon.ps1` (original, MIT).
- **Start with Windows**: a `Veneer` value in `HKCU\...\Run` with the quoted executable path. Before the first
  change the previous value (or its absence) goes to `state\autostart.json`; turning it off puts that back.
  Tested for real: on, then off, with the Run key unchanged afterwards.
- **The quit hang** (found in the Phase 6 fixes): a blurred (acrylic) menu still open while WPF shuts down its
  dispatcher keeps `Application.Run` from returning. Closing the menu during `Exit` is too late. Every way out now
  goes through `App.Quit`, which closes the open menu and queues the shutdown after WPF has finished closing it.
  An unblurred menu never hung.
- **UI strings** of the new menu are still English literals; they move to resource files with the settings window
  (7b), together with the existing menus. (Done in 7b.)

## 2026-11-24: Bar shadow and rounded dock glass (Phase 6 fixes)

Two defects the author saw in the Aqua screenshots, measured with pixel samples over a plain white (and a striped)
window with Veneer off and on.

- **The dark band under the top bar was DWM's window shadow**: about 60 px deep (`#6F6F6F` fading to white), as
  large as an active window's, because the bar is kept "active" for its blur. Fix: give the bar a window region.
  DWM draws no shadow around a window with a region. The region is larger than any window, so it clips nothing and
  never needs updating.
- **The bar was 34 px tall, not 28**: Windows' minimum height for a captioned window (`SM_CYMINTRACK`) is 34 px.
  The app-bar position pin now marks `WM_WINDOWPOSCHANGING` handled, so no later handler enlarges the window. WPF
  also lays the content out at the minimum it saw at creation, so the bar's content is pinned to the bar's
  thickness. Before, 6 px of bare backdrop sat under the bar's edge line, and the modules sat 3 px low.
- **The dock's square corners: no DWM backdrop can be rounded above 8 px.** Measured: the system backdrop, the
  composition-attribute blur (also with a `DwmEnableBlurBehindWindow` blur region) and even the bare extended frame
  without blur all fill the whole window rectangle, whatever the window region or the per-pixel alpha.
  - Options: native rounding (certain, but only ~8 px), no blur on the dock, or drawing the glass ourselves. The
    author chose the last.
  - Outcome: above 8 px the dock window is per-pixel transparent, and its plate is drawn by the composition engine
    (`GlassPlate`, through `ICompositorDesktopInterop.CreateDesktopWindowTarget`). It has a host-backdrop blur
    clipped to a rounded rectangle, with the theme's tint and gradient edge as composition shapes on top. The
    blur needs the window's undocumented accent state 5 (`ACCENT_ENABLE_HOSTBACKDROP`); without it the brush is
    black. The composition tree hides the window's WPF content, so the plate is drawn entirely in composition; an
    almost invisible WPF shape keeps the plate clickable.
  - If composition fails, the dock falls back to the old plain blur, with square blurred corners and a log line.
  - Without blur, a dock above 8 px is a per-pixel transparent WPF plate: the bare DWM frame used to show a
    dark square there too.
- **Found, not fixed here:** quitting while a bar menu is open hung the process after its cleanup (the taskbar was
  already back). It also happened on the previous commit. Fixed in Phase 7a.

## 2026-11-25: The default theme, Aqua (Phase 6b)

- **Style A** (author's choice): macOS-like, a glass menu bar on top and a magnifying dock at the bottom, but every
  piece of art is original. Wallpapers and logo are drawn by a script (`tools/aqua-art/make-art.ps1`) from
  gradients and shapes, so they are reproducible and MIT like the code. Status icons stay Windows' Segoe Fluent
  glyphs, drawn at run time.
- **Follows Windows' light/dark** (`appearance: system`) with a `dark` variant; the logo is a token, so the variant
  swaps it. The theme sets no accent color: a default theme should not repaint the user's Windows accent.
- **Shipped next to the executable** (`themes\aqua`, copied by the App project) and used when no `--theme` is given.
  `BuiltInTheme` stays as the fallback when the files are missing or broken.
- **A test loads every bundled theme**, base and variants, and requires zero diagnostics.
- **Packaged apps show Windows' name** in the bar: their exe description is often a host ("Windows Terminal Host").
- **Restyled after a reference image** the author provided (aesthetic only, widgets later): smoked slate glass,
  monochrome text and highlights (no blue), radii 12–22, and **gradient borders** (`borderHighlight`, a new theme
  field for surfaces and pop-ups) that imitate light on a glass edge. New wallpapers in the same spirit: silky
  slate folds (dark) and ice (light), still generated by the script. Soft drop shadows under the bar and dock
  were left out: a shadow outside a DWM window needs an extra window under it, for a small gain.
- **Large radii use a plain blur.** The DWM system backdrop ignores a window region, so a dock rounder than 8 px
  showed solid square corners (visible on a light wallpaper). Above that radius the surface now uses the
  composition-attribute blur without tint: outside the region it shows only the blurred wallpaper. (Replaced on
  2026-11-24: see "Bar shadow and rounded dock glass".)
- **Repository screenshots** are taken with `--demo` (Debug and release): the bar shows "Desktop" and the dock
  only the theme's pinned apps, so no personal app appears in a public image.

## 2026-11-25: Look and feel, engine part (Phase 6a)

- **New phase.** The author found the look too bare and asked for a polished default theme before profiles. Phase 6
  is "look and feel"; the former Phases 6–8 became 7–9.
- **Module highlight** is the module's own rounded background: padding moved inside it, the bar's `highlightInset`
  keeps it off the bar edges. "Pressed" lasts while the module's menu is open.
- **Themed menus** are built from XAML text with the theme's values (the shortest way to express WPF control
  templates). Item styles are set per item: `ItemContainerStyle` would also hit the separators, which WPF refuses
  (it crashed the shell in the first test). Any style error falls back to the Windows look.
- **Blur on menus** uses the `SetWindowCompositionAttribute` acrylic from Phase 0: WPF menus are per-pixel
  transparent windows, where the DWM backdrop does not work. Windows 11 rounding is requested on the pop-up.
- **The dock plate stretches** with the magnified icons by moving the plate window inside its reserved strip every
  frame (`AppBar.MoveWindow`, no new shell request). Scripted sweep: 72 fps, slowest frame 37 ms.
- **Dock label and bounce** live in the icons window, which gets extra room toward the screen for them.

## 2026-11-25: Windows settings from themes (Phase 5)

- **User choices** (author, 2026-11-24): every setting starts "from the theme". Wallpaper and accent: theme or
  system; light/dark: theme, light, dark or system. "System" means Veneer leaves that setting alone. Switching a
  setting to "system" does not undo what the theme already changed; "Restore original Windows settings" does.
  Stored in `profiles\default\windows.json`, read leniently (each bad value falls back alone, with a warning).
- **Snapshot per setting**, taken right before that setting is first changed and never overwritten while the file
  exists (`state\windows-settings.json`), so it always holds the user's own values. A damaged snapshot blocks
  further changes instead of being overwritten.
- **Wallpaper:** `IDesktopWallpaper`, declared by hand (CsWin32 only exposes it with raw pointers). Set for all
  monitors with position Fill; restored per monitor, or for all monitors at once when they shared one picture
  (that also updates the registry value). Explorer writes `Control Panel\Desktop\WallPaper` a few seconds after
  the change, so a check right after a restore still sees the old value. Slideshows and Spotlight are saved as
  their current picture (noted in the code).
- **Accent:** `SetUserColorPreference` (ordinal 122) only while `GetUserColorPreference` is still ordinal 120,
  otherwise "not supported". `ColorizationColor` and `ColorizationAfterglow` are written too, since the API
  leaves them alone. The restore calls the API with the saved preference, then writes the saved registry values
  back, which also puts the palette back byte for byte.
- **Off the UI thread.** `WM_SETTINGCHANGE "ImmersiveColorSet"` is broadcast with `SMTO_ABORTIFHUNG`, but slow
  apps still make it take seconds; on the UI thread the bars froze for 4–5 s at startup. Applying runs on the
  thread pool, one change at a time (lock), and never fatal.
- **Variant choice:** dark mode loads the `dark` variant, light mode `light` (or the base). The app reloads the theme
  with the right variant before building the surfaces, and listens for `ImmersiveColorSet` to switch when Windows
  changes mode. `--variant` on the command line wins.
- **Maximized windows follow the restored work area.** After a crash the author's maximized browser (Zen) kept
  the size it had under Veneer's bars (top at y=20, bottom past the returning taskbar): Windows does not always
  resize maximized windows when app bars disappear. Every taskbar restore now moves maximized windows whose
  bounds do not match their monitor's work area back onto it (keeping the frame overhang and the maximized state,
  `SetWindowPos` async). Restore-then-maximize was tried first; Zen ignored it.
- **Emergency exit in the watchdog:** Ctrl+Alt+Shift+F12, registered on the watchdog's thread. The watchdog sleeps
  in `MsgWaitForMultipleObjects` on the main process handle and its message queue (no polling), ends the main
  process on the hotkey, then restores the taskbar as after any crash.

## 2026-11-24: Code review of Phases 2–4

Three read-only reviewers (one per phase); every finding was checked against the code before fixing. The real
system tests were repeated afterwards (quit, second instance, crash, `taskkill /T /F`, Explorer restart, auto-hide).

**Taskbar safety**
- **The state file is written atomically** (temporary file, flush, swap) and read leniently: a damaged or empty
  snapshot no longer blocks the restore forever; auto-hide is then turned off (the Windows default).
  The logic is in `Veneer.Core.TaskbarState`, with tests.
- **Restore keeps the state file unless it worked.** Without Explorer's taskbar (logoff, Explorer restarting) the
  request would go nowhere, so the file stays for the next attempt. After the change, the shell is asked again
  and the file is deleted only if auto-hide really is what we asked for.
- **Restore always shows the taskbar windows**, even with no state file: an earlier run or a manual restore may
  have reset the settings while our show hook kept hiding the windows.
- **The watchdog is not a child of Veneer.** It is started by a short-lived launcher (`--launch-watchdog`), so
  "End process tree" and `taskkill /T` leave it alive. It waits up to 30 s for Explorer, and does nothing if a
  newer Veneer owns the state (single-instance mutex).
- **RunOnce safety net:** `HKCU\...\RunOnce\VeneerRestoreTaskbar` runs `Veneer.App --restore-taskbar-at-signin`
  at the next sign-in. It is set before the first change and removed after every successful restore.
  **Explorer also runs RunOnce entries when it restarts.** The first version restored the taskbar under a running
  Veneer, whose show hook then hid it again, and the exit left it hidden. Found by the restart test, fixed: the
  sign-in argument does nothing while a Veneer runs except put the consumed entry back. `--restore-taskbar`
  (manual) stays unconditional, as an emergency command.
- **One Veneer at a time** (`Local\Veneer.App` mutex). Two instances fought over the state file and reserved the
  bars twice.
- **Logoff:** after cleaning up on `SessionEnding`, Veneer exits. If another app cancels the logoff, a half-cleaned
  Veneer would have been worse than none.
- **Cleanup** runs every step on its own, with the taskbar restore in `finally`.

**Theme input**
- **Duplicate JSON keys are parse errors** (`AllowDuplicateProperties = false`, .NET 10). Before, they slipped
  through parsing and threw later, which rejected the whole theme because of a broken overrides file.
- **Overrides that break a theme are skipped:** the theme loads without them and their errors become warnings.
  The overrides file is shared by every theme, so a value valid for one can be invalid for another.
- **Merging is linear.** Origin cleanup scanned every recorded path for every scalar key (minutes for a crafted
  1 MB theme); now it scans only when an object or array is replaced, with ordinal comparisons, and surfaces are
  indexed by id.
- **Zip extraction never throws** (corrupt data, CRC errors, unsupported methods such as LZMA), and a zip larger
  than the total limit is rejected before its directory is read.
- **Numbers outside the double range** (`1e400`) are errors at their own path.
- **Overrides writes** go through a flushed temporary file and `File.Replace`, retry on a sharing violation, and
  copy a commented file to `.bak` first.

**Dock and bars**
- **The window tracker reacts only to app windows:** shown or uncloaked windows that pass the taskbar filter, and
  hide, destroy or cloak events for windows of the last listing. Before, every tooltip and menu of every app
  re-listed all windows, and windows closed in the background were missed. The most recently used window is
  looked up at click time.
- **Visibility is one rule** (`ShowsSurfaces`): the plate and the owned icons window hide together for full-screen
  apps and auto-hide (an owned window does not hide with its owner); the auto-hide edge strip exists only while
  the dock is tucked away; the dock tucks itself away again if the mouse leaves without reaching it.
- **Pin, unpin and reorder no longer rebuild every surface.** `ThemeChanges.OnlyDockPinnedChanged` recognizes a
  reload where only a dock's pinned list changed, and the dock updates in place (no strip released and reserved,
  no resize of maximized windows).
- **Closed docks stay closed:** timers stop, and a menu or drag that outlives a rebuild cannot write a stale list.
- **Magnifier:** the cursor maps through each cell plus its gap, so the row no longer jumps when crossing a gap.
- **Calls to other apps** use `ShowWindowAsync` and `PostMessage`; exe descriptions are cached; theme images
  load through one guarded loader (`ThemeVisuals.LoadImage`), so a damaged image falls back instead of crashing.
- **Moved to Core with tests:** taskbar snapshot rules, clock tick math, battery level, virtual adapter
  detection, drag drop index and order.

**Deferred:** loading dock icons in the background (Phase 8), checking files inside `iconFolder` for links at
every read, and a log shared safely between Veneer and its watchdog.

## 2026-11-24: Dock magnification and editing (Phase 4b)

- **Two windows.** The plate is the blurred DWM window (it reserves the strip). The icons live in a per-pixel
  transparent window (`AllowsTransparency`) that is larger than the plate, so magnified icons can grow out of
  it. Fully transparent pixels let clicks through to the windows behind. Over the plate the icons window has an
  alpha-1 fill, so the gaps between icons still count as "inside the dock".
- **The icons window is owned by the plate** (`Owner`). Windows keeps an owned window above its owner, even when
  the shell moves the plate to the top of the topmost band. Without it the icons ended up behind the plate.
- **Frame cap.** A per-pixel transparent WPF window gets `CompositionTarget.Rendering` callbacks far above the
  display rate (over 1000 per second measured). The hover animation caps its work at about 120 updates per
  second, and unsubscribes when it ends, so an idle dock costs nothing. Scripted sweep: 63 fps, slowest frame
  33 ms.
- **Pinned apps are user overrides** (`surfaces[id=<dock>].defaultPinned`), written with `OverridesFile`: it keeps
  the rest of the file, writes a temporary file and swaps it in, and never overwrites a file it cannot parse.
  The theme then hot-reloads.
- **A pinned running app is stored by AppUserModelID when it has one**, otherwise by full exe path.
- **Full screen:** bars and dock hide on `ABN_FULLSCREENAPP`. The shell only sends it to registered app bars, so
  at least one surface must reserve space.

## 2026-11-24: Dock (Phase 4a)

- **Grouping key:** AppUserModelID (window property first, then the packaged process), else the exe path. This
  matches how the taskbar groups Store apps, Terminal, Chromium apps and Steam.
- **Updates** come from WinEvent hooks (object create, destroy, show, hide, cloak, uncloak; foreground) and are
  debounced by 150 ms, then the windows are listed again. A burst of events costs one listing; idle costs nothing.
  The icons are rebuilt only when the set of apps or their running state changes.
- **The dock reserves a full-width strip** on its edge (`AppBar`), but its window is only as long as its icons,
  centered in the strip the shell grants.
- **The running indicator lives inside each icon cell**, in the padding strip on the screen-edge side. Drawing it
  outside the icon box with negative margins or a Canvas got it clipped by WPF layout.
- **Icon quality.** `IShellItemImageFactory` returns some icons premultiplied and some with straight alpha.
  Each icon is checked (a color channel above its alpha means straight alpha). Guessing wrong gave light
  fringes. Icons are requested at 256 px, cached per theme and app, and scaled down with
  `BitmapScalingMode.HighQuality`; the default bilinear filter gave jagged edges.
- **Store apps use their unplated icon**, like the taskbar: the manifest's `Square44x44Logo` in its largest
  `altform-unplated` target size (package found through `PackageManager`). The shell icon of some Store apps
  sits on a gray plate.
- **Activation** uses `SetForegroundWindow` right after the user's click on the dock, which Windows allows to
  the process that received the last input.

## 2026-11-24: Bar modules (Phase 3b)

- **Menus use the WPF Fluent theme** (`Application.ThemeMode = System`, still marked experimental, `WPF0001`
  suppressed). It gives the Windows 11 look and follows light/dark for free. Theme-styled menus come later
  (roadmap, Phase 7).
- **Confirmation** for Sleep, Restart and Shut Down is a plain Windows message box with "No" as the default.
  Restart and shut down run `shutdown.exe`, which enables the shutdown privilege by itself.
- **"Quit" never kills.** It posts `WM_CLOSE` to the app's visible top-level windows, like closing them by
  hand, so the app can ask to save.
- **Volume** uses Core Audio through CsWin32 COM interfaces with change callbacks, and re-binds when the default
  device changes. No audio library was needed.
- **Network state** comes from `NetworkChange` events. Virtual adapters (Hyper-V, VPN, VM software) are
  excluded by name, a heuristic noted in the code.
- **Status icons** are glyphs from Segoe Fluent Icons in the text color by default. A theme can give one image
  per state (`icons`), so a theme can match any look without code changes.
- **Debug self-test** (`--self-test "wait:2,menu,..."`): Claude Code's auto mode refuses commands that simulate
  keyboard and mouse input on the user's session, so the app drives itself instead. It also makes the checks
  repeatable.

## 2026-11-24: Bars in the app (Phase 3a)

- **Target framework `net10.0-windows10.0.22621.0`** for `Veneer.Interop` and `Veneer.App` (Windows 11 22H2), and
  `PlatformTarget` x64 for CsWin32.
- **Default font: "Segoe UI Variable Text, Segoe UI", 12 px.** `SystemFonts.MessageFontFamily` returned Tahoma on
  the test PC, an old non-client metrics value, so it is not used. Themes set their own font anyway.
- **A hidden top-level window** receives `TaskbarCreated` and the hotkeys. Message-only windows do not receive
  broadcasts.
- **Bars are rebuilt from scratch** on every theme load and display change. This is simple and fast enough
  (two bars in well under a second). Incremental updates only if this ever shows up as a problem.
- **Without blur**, a translucent bar background shows the desktop unblurred: DWM draws no backdrop
  (`DWMSBT_NONE`) and the extended frame stays transparent.
- **The clock wakes once per minute** (once per second only when the format shows seconds), aligned to the
  boundary. Idle CPU with two bars: 0.04%.

## 2026-11-23: Theme engine (Phase 2)

- **Layers merge on raw JSON, before binding.** Theme → variant → user overrides are merged as `JsonNode`
  trees, and only then turned into typed records. This way overrides have exactly the theme's shape,
  fields unknown to this version are not lost, and each value records the layer it came from (needed for
  "reset to theme value").
- **Orphans are tracked per surface.** A user override for a surface `id` the theme no longer has is kept and
  reported. Field-level overrides always apply: any field in the schema is valid for the user to set, even
  when the theme leaves it out.
- **`height` became `thickness`.** In the draft, `height` was wrong for left/right bars.
- **No exceptions for bad input.** Loading returns a result with diagnostics, and a catch-all turns any
  unexpected failure into an error diagnostic.
- **Fallback theme.** `BuiltInTheme` is plain JSON loaded through the same loader. It is used until a theme
  loads.
- **Hot reload.** A `FileSystemWatcher` with a 300 ms debounce raises `ThemeManager.Loaded` on a thread-pool
  thread; the WPF layer marshals it to the UI thread.

## 2026-11-23: Phase 0 spikes

The spikes live in `spikes/` (throwaway code, not in `Veneer.slnx`, not built by CI). Test machine: Windows 11
26H2 build 26300, one 1920×1080 monitor at 100% scaling, dark mode, transparency effects on, x64.

### Blurred overlay windows (`spikes/OverlayBlur`)

Goal: a borderless, topmost WPF window, hidden from Alt+Tab (`WS_EX_TOOLWINDOW`), that never takes focus
(`WS_EX_NOACTIVATE`), with real blur behind it and rounded corners.

Options tried:

| Option | Blur | Notes |
|--------|------|-------|
| DWM system backdrop (`DWMWA_SYSTEMBACKDROP_TYPE` = `DWMSBT_TRANSIENTWINDOW`), `WindowStyle=None` | No | DWM draws the solid "inactive" fallback. A `WS_EX_NOACTIVATE` window is never active. |
| Same, plus forcing `WM_NCACTIVATE(TRUE)` | No | Still solid while the window has no `WS_CAPTION`. |
| Same, `WindowStyle=SingleBorderWindow` (keeps `WS_CAPTION`) + forced `WM_NCACTIVATE(TRUE)` | **Yes** | Stays blurred when other windows take focus. |
| `SetWindowCompositionAttribute` (`ACCENT_ENABLE_ACRYLICBLURBEHIND` / `BLURBEHIND`), `AllowsTransparency=True` | Yes | Undocumented API; needs layered (`AllowsTransparency`) rendering; square blur unless clipped. |

**Decision:** use the documented DWM system backdrop. The recipe:

1. `AllowsTransparency=False`, `Background=Transparent`, `WindowStyle=SingleBorderWindow`, and a
   `WindowChrome` with `GlassFrameThickness=-1`, `CaptionHeight=0`, `ResizeBorderThickness=0`.
2. `HwndSource.CompositionTarget.BackgroundColor = Transparent` and
   `DwmExtendFrameIntoClientArea(-1)`.
3. `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (acrylic).
4. Remove `WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX`. Otherwise DWM draws a close button.
5. Set `DWMWA_CAPTION_COLOR` and `DWMWA_BORDER_COLOR` to `DWMWA_COLOR_NONE`. Otherwise DWM draws an
   accent-colored caption strip and a border.
6. Handle `WM_NCACTIVATE`: answer `wParam=0` with `DefWindowProc(..., TRUE)`, and send `WM_NCACTIVATE(TRUE)`
   once at start. This keeps the backdrop in its active (blurred) state.
7. Add `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `Topmost`, `ShowActivated=False`.

The theme tint (e.g. `#99F5F5F5`) is a WPF brush painted over the backdrop. The backdrop's own tint
follows the system light/dark mode and cannot be changed, so themes control color through that brush.

**Corners:** `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND` gives about 8 px, antialiased, with the DWM
shadow. `DWMWCP_DONOTROUND` gives square corners (bars). A larger radius (the dock draft uses 18) needs
`SetWindowRgn` with a rounded rectangle. That works with the backdrop, but the edge is aliased (1-bit) and
it clips the DWM shadow. Theme value `cornerRadius` ≤ 8 → DWM rounding; > 8 → window region, with a WPF
1 px border drawn on top to soften the edge.

**Cost:** idle CPU 0.00–0.21 % with both surfaces up; about 110 MB working set for the spike process.

**Kept as fallback:** `SetWindowCompositionAttribute` works too, if a future Windows build breaks the
`WM_NCACTIVATE` trick.

**Risk:** the `WM_NCACTIVATE` behavior is not a documented contract. Smooth movement (the dock animation)
was not measured in this spike; Phase 4 checks it.

### Several app bars at once (`spikes/AppBars`)

Two app bars registered with `SHAppBarMessage` (`ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS`, `ABM_REMOVE`),
top (28 px) and bottom (64 px), work together. With the taskbar visible, the bottom bar sits above
it and the work area becomes `0,28,1920,968`. With the taskbar hidden, the bottom bar sits on the
screen edge (`0,28,1920,1016`). After `ABM_REMOVE` the work area is back to the original.

Findings:

- **The system moves an app bar into the work area it just reserved.** The top bar was moved
  to `y=28` (its own height) right after `ABM_SETPOS`. Fix: keep the shell-approved rectangle and enforce it
  in `WM_WINDOWPOSCHANGING`.
- `ABN_POSCHANGED` arrives in short bursts after each registration. Re-running `QUERYPOS`/`SETPOS` is
  cheap and converges.
- Also forward `WM_ACTIVATE` → `ABM_ACTIVATE` and `WM_WINDOWPOSCHANGED` → `ABM_WINDOWPOSCHANGED`, as the docs
  ask.
- After a forced kill, Explorer drops the dead app bars by itself and the work area recovers.

### Hiding the taskbar safely (`spikes/AppBars --hide-taskbar`)

How it hides:

1. Write the original taskbar state (`ABM_GETSTATE`) to a state file *before* touching anything. The
   file's existence means "dirty".
2. Turn on auto-hide (`ABM_SETSTATE` with `ABS_AUTOHIDE`). This frees the work area.
3. `ShowWindow(SW_HIDE)` on `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`. This stops the
   auto-hidden taskbar from popping up.
4. Explorer shows the taskbar again on its own (7 times in the first second in the spike). A WinEvent
   hook (`EVENT_OBJECT_SHOW`, filtered to Explorer's process, out of context) hides it again. It is
   event-driven, with no polling.

How it comes back:

| Exit path | Who restores | Result |
|-----------|--------------|--------|
| Normal exit | `Application.Exit` | ✓ |
| Unhandled exception (dispatcher) | `DispatcherUnhandledException` / `AppDomain.UnhandledException` | ✓ |
| `Environment.FailFast` (no handler runs) | Watchdog process | ✓ |
| `taskkill /f` | Watchdog process | ✓ |
| `taskkill /f` with no watchdog | Explorer shows the taskbar again (still auto-hide); next start restores the rest from the dirty state file | ✓ (degraded until next start) |
| Explorer restart | `TaskbarCreated` → hide again + re-register app bars | ✓ |
| Logoff / shutdown | `Application.SessionEnding` | Wired, not tested |

**Watchdog:** the app starts a second copy of itself with `--watchdog <pid>`, before any WPF window
exists. It waits for that process to exit and restores the taskbar only if the state file is still
there. Cost: about 31 MB, no CPU. A smaller dedicated executable can replace it later if memory matters.

**Explorer restart:** a force-killed Explorer does not save the auto-hide bit we set, so the new
Explorer reserves its 48 px again. On `TaskbarCreated`, run the full hide again (auto-hide + `SW_HIDE` +
a new WinEvent hook on the new Explorer process), not only `SW_HIDE`.

**Cost:** idle CPU 0.00 % with both bars, the hidden taskbar and the hook.

**Not tested (single monitor):** `Shell_SecondaryTrayWnd`, and the multi-monitor work area.

### Accent color on Windows 11 (`spikes/Accent`)

There is no documented API to set the accent color. Two paths tried on build 26300:

| Path | Registry | Palette regenerated | Apps see it (`UISettings.Accent`) | Live on screen |
|------|----------|---------------------|------------------------------------|----------------|
| Write `DWM\AccentColor`, `ColorizationColor`, `Explorer\Accent\AccentColorMenu` + broadcast `WM_SETTINGCHANGE "ImmersiveColorSet"` | Changed | **No** | **No** | No |
| `uxtheme.dll` `SetUserColorPreference` (export **ordinal 122**, no name), `IMMERSIVE_COLOR_PREFERENCE { COLORREF start, accent }`, `forceCommit=TRUE` | Changed | **Yes** (`AccentPalette`, `StartColorMenu`) | **Yes** | Yes (taskbar badges and indicators) |

**Decision:** set the accent with `SetUserColorPreference`. First read the current value with the named
export `GetUserColorPreference` (ordinal 120), change only the accent field and keep the alpha byte
`0xFF`. Windows derives its own palette from the color, so `UISettings.Accent` is a Windows-adjusted shade
(`#0A84FF` in → `#0075EE` out).

**Snapshot and restore:** before the first change, save `DWM\{AccentColor, ColorizationColor,
ColorizationAfterglow, ColorPrevalence}`, `Explorer\Accent\{AccentPalette, AccentColorMenu, StartColorMenu}`,
`Control Panel\Desktop\AutoColorization` and the `GetUserColorPreference` result. To restore, write the
values back, call `SetUserColorPreference` with the saved preference, and broadcast `ImmersiveColorSet`.
In the spike the values came back byte for byte.

**Risks:** the export is ordinal-only. Check that `GetUserColorPreference` is still at ordinal 120 before
calling ordinal 122, and fall back to "accent not supported on this build" otherwise. `SetUserColorPreference`
does not update `ColorizationColor` (title bars with "show accent color" on); Phase 5 decides whether to
write it as well.

### CsWin32

CsWin32 works for this layer, with two notes:

- `APPBARDATA` has an architecture-specific layout, so `Veneer.Interop` needs `PlatformTarget` set (x64, and
  arm64 as a separate build) instead of AnyCPU.
- `SetWinEventHook` returns `UnhookWinEventSafeHandle`.

Undocumented APIs (`SetUserColorPreference` by ordinal, `SetWindowCompositionAttribute`) need a manual
`DllImport` inside `Veneer.Interop`.
