<h1 align="center">Veneer</h1>

<h4 align="center">A themeable desktop shell for Windows 11: themes decide which bars and docks you get, where they sit and how they look.</h4>

<p align="center">
  <a href="#project-status">Status</a> |
  <a href="docs/roadmap.md">Roadmap</a> |
  <a href="#why-veneer">Why Veneer</a> |
  <a href="#build">Build</a> |
  <a href="#themes">Themes</a> |
  <a href="docs/decisions.md">Decisions</a> |
  <a href="#license">License</a>
</p>

Veneer replaces the Windows taskbar with its own bars and dock, and lets *themes* decide everything about them:
which components exist, which screen edge they sit on, which modules they show and how they look.

## Project status

**Early. Not usable day to day yet.** The theme engine works and is tested. The app already draws blurred bars
from a theme with all their modules and a dock with magnification, hides the taskbar and always gives it back.
Themes also set the wallpaper, accent color and light/dark mode, with a full restore. A polished look and a real
default theme are next. The checklist of every feature is in [docs/roadmap.md](docs/roadmap.md).

| What | Status |
|---|---|
| Repository, CI (build + tests on `windows-latest`) | ✅ Done |
| Code review of the theme engine, bars and dock (2026-11-24) | ✅ Done: findings fixed and tested ([details](docs/decisions.md#2026-11-24-code-review-of-phases-24)) |
| Real blur behind bars | ✅ In the app: DWM acrylic on a non-activating window ([details](docs/decisions.md#blurred-overlay-windows-spikesoverlayblur)). Rounded corners above 8 px (dock) wait for Phase 4 |
| Bars on any edge, several at once, with reserved screen space | ✅ In the app, built from the theme and rebuilt on hot reload |
| Hide the taskbar, always bring it back | ✅ In the app: normal exit, unhandled exception, crash or kill (a watchdog outside Veneer's process tree, so "End process tree" is covered too), next start, Explorer restart, and a RunOnce entry for power loss. One Veneer at a time. Logoff is wired but untested |
| Theme engine: parse, validate, tokens, light/dark variants | ✅ Done, unit-tested |
| Your overrides on top of a theme, with value origins and orphans | ✅ Done, unit-tested |
| Safe theme files and `.zip` install (path, link, type and size checks) | ✅ Done, unit-tested |
| Hot reload, fallback to the previous theme on errors | ✅ Done, unit-tested |
| Bar modules: logo menu, active app, clock, volume, network, battery, spacer | ✅ Done. Status icons use Windows glyphs unless the theme gives its own images |
| Dock: pinned and running apps, live updates, system or theme icons, click to open or switch | ✅ Done |
| Dock: hover magnification, context menu, drag to reorder, separators, auto-hide, saved pinned apps | ✅ Done (drag still to be tried by hand) |
| Wallpaper, accent color, light/dark from the theme, with restore | ✅ Done: each one from the theme or the system, snapshot before the first change, byte-for-byte restore tested |
| A polished look: highlights, themed menus, dock labels, launch bounce, stretching dock | ✅ Done (Phase 6a) |
| A real default theme (macOS-like, original artwork, light and dark) | ⏳ Next (Phase 6b) |
| Profiles, settings window, tray icon, start with Windows | ⏳ Planned (Phase 7) |
| Two bundled themes with very different layouts | ⏳ Planned (Phase 8) |
| First release | ⏳ Planned (Phase 9) |
| Multi-monitor, display scaling above 100%, ARM64 | ❔ Not verified yet |

✅ done · ⚠️ partly done · 🧪 proven in a spike, not in the app yet · ⏳ planned · ❔ not verified

## Why Veneer

Most Windows customization tools either restyle one piece (a dock, a bar) or ship a single fixed look.
Veneer is built around a different idea:

- **Themes control layout, not just colors.** A theme can be a top menu bar plus a bottom dock, one
  slim bar on the left, or anything in between, all without touching code.
- **Your tweaks live on top of the theme.** Change the icon size, pin apps, recolor a bar: your
  changes are stored separately, so switching or updating a theme never wipes them.
- **Profiles.** Save a whole setup (theme, variant, your tweaks, which Windows settings to apply) and
  switch between setups in one click, for example *Work* and *Gaming*.
- **Themes are data, never code.** A theme is a `theme.json` plus images and fonts. Installing a theme
  from a stranger cannot run anything on your PC.
- **Always reversible.** The Windows taskbar comes back when Veneer exits, even after a crash. Every
  Windows setting a theme changes (wallpaper, accent color, light/dark mode) can be restored to what you had.

## Planned features

### First release (MVP)

| Area | What it does |
|------|--------------|
| **Bars** | On any screen edge, optionally reserving screen space. Modules: logo menu (system actions), active app name, clock, volume, network, battery, spacers. |
| **Dock** | Bottom, left or right edge. Pinned and running apps, running indicator, hover magnification, drag to reorder, context menu, separators. |
| **Theme engine** | Layout + styling, design tokens, light/dark variants, hot reload, install from `.zip`, strict validation. |
| **Windows integration** | Optional per theme: wallpaper, accent color, light/dark mode, with a snapshot to restore the originals. |
| **Personalization layer** | Your overrides on top of any theme, with "reset to theme value" per setting. |
| **Profiles** | Create, duplicate, switch from the tray, the settings window or a hotkey. |
| **Bundled themes** | Two original themes with very different layouts, to show what the engine can do. |

### Later

Desktop widgets (calendar, weather, battery, photos), a live theme editor, an in-app theme gallery,
automatic profile switching (time of day, power, running apps), accent color from wallpaper, a launcher,
full multi-monitor support.

## Roadmap

| Phase | Scope | Status |
|---|---|---|
| 1 | Repository skeleton, CI | ✅ |
| 0 | Technical spikes: blur, app bars, taskbar safety, accent API | ✅ |
| 2 | Theme engine and personalization layers | ✅ |
| 3 | Layout engine, bars and their modules | ✅ |
| 4 | Dock | ✅ |
| 5 | Windows settings from themes (wallpaper, accent, light/dark) with restore | ✅ |
| 6 | Look and feel: what the engine can draw, and a real default theme | ⚠️ 6a done, 6b next |
| 7 | Profiles, settings window, tray, theme install, start with Windows | ⏳ |
| 8 | Bundled themes | ⏳ |
| 9 | Polish, documentation, first release | ⏳ |

Every feature, phase by phase, with its checkbox: [docs/roadmap.md](docs/roadmap.md).

## Requirements

- Windows 11 (22H2 or later). Windows 10 is not supported.
- To build: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Build

```powershell
git clone https://github.com/Mynamesdsuper/veneer.git
cd veneer
dotnet build
dotnet test
dotnet run --project src/Veneer.App -- --theme <theme folder>
```

While Veneer runs, **Ctrl+Alt+Shift+Q** quits and gives the taskbar back. **Ctrl+Alt+Shift+T** restores the
taskbar without quitting. If Veneer ever hangs, **Ctrl+Alt+Shift+F12** ends it and restores the taskbar. By hand:
`Veneer.App --restore-taskbar`, and `Veneer.App --restore-windows-settings` for the wallpaper, accent color and
light/dark mode.

## Repository layout

```
src/
  Veneer.Core/      Theme model, parser and validator, personalization merge, profiles, layout logic.
                    No WPF or Win32 dependencies, fully unit-tested.
  Veneer.Interop/   Thin Win32 layer: window tracking, app bars, taskbar, wallpaper, accent, light/dark.
  Veneer.App/       WPF application: bars, dock, modules, settings window, tray icon.
tests/
  Veneer.Core.Tests/
themes/             Themes shipped with Veneer (original assets only).
tools/              Developer tools (e.g. icon import). Never used by the theme loader.
docs/
  theme-format.md   How to write a theme.
  roadmap.md        Full plan with a checklist of every feature.
  decisions.md      Technical decisions and spike results.
spikes/             Phase 0 throwaway code (not in the solution, not built by CI).
```

## Themes

A theme is a folder (or `.zip`) with a `theme.json` and an `assets/` folder. See
[docs/theme-format.md](docs/theme-format.md). The format can still change until the first release.

Community themes must respect the licenses of the assets they use. Veneer only ships original assets
or assets with a compatible, declared license.

## Contributing

The project is not ready for contributions yet. Issues with ideas and feedback are welcome.

## License

[MIT](LICENSE).

Veneer is an independent project. It is not affiliated with, endorsed by or sponsored by Microsoft or
Apple. Windows is a trademark of Microsoft Corporation; macOS is a trademark of Apple Inc.
