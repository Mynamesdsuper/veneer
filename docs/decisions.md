# Technical decisions

Record each decision with its date, context, the options considered and the outcome.

## 2026-11-23: Stack

C# + WPF on .NET 10 (LTS), Windows 11 only. .NET 8 reached end of support on 2026-11-10.

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
- **Shell icons arrive as premultiplied BGRA** (`IShellItemImageFactory`), so they are wrapped as `Pbgra32`.
  Icons are requested at 256 px and cached per theme and app.
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
