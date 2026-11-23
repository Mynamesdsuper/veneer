# Veneer

**A themeable desktop shell for Windows 11.** Veneer replaces the taskbar with its own bars and dock,
and lets *themes* decide everything about them: which components exist, which screen edge they sit on,
which modules they show and how they look.

> **Status: early development.** Nothing is usable yet. The repository currently holds the project
> skeleton. Follow the [roadmap](#roadmap) to see what is coming.

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

- [x] **Phase 1:** repository skeleton, CI
- [ ] **Phase 0:** technical spikes: blurred overlay windows, app bars on several edges, safe taskbar hiding
- [ ] **Phase 2:** theme engine and personalization layers
- [ ] **Phase 3:** layout engine and bars
- [ ] **Phase 4:** dock
- [ ] **Phase 5:** Windows settings from themes (wallpaper, accent, light/dark) with restore
- [ ] **Phase 6:** profiles, settings window, tray, theme install, start with Windows
- [ ] **Phase 7:** bundled themes
- [ ] **Phase 8:** polish, documentation, first release

## Requirements

- Windows 11 (22H2 or later). Windows 10 is not supported.
- To build: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Build

```powershell
git clone https://github.com/Mynamesdsuper/veneer.git
cd veneer
dotnet build
dotnet test
```

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
  decisions.md      Technical decisions and spike results.
```

## Themes

A theme is a folder (or `.zip`) with a `theme.json` and an `assets/` folder. See
[docs/theme-format.md](docs/theme-format.md). The format is still a draft and will change until the
first release.

Community themes must respect the licenses of the assets they use. Veneer only ships original assets
or assets with a compatible, declared license.

## Contributing

The project is not ready for contributions yet. Issues with ideas and feedback are welcome.

## License

[MIT](LICENSE).

Veneer is an independent project. It is not affiliated with, endorsed by or sponsored by Microsoft or
Apple. Windows is a trademark of Microsoft Corporation; macOS is a trademark of Apple Inc.
