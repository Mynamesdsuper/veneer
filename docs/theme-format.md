# Theme format

> Schema version **1**. The format can still change before the first release; breaking changes will bump
> `schemaVersion`.

A theme is a folder, or a `.zip` with the same structure:

```
my-theme/
  theme.json
  LICENSE        # or CREDITS: required for bundled themes, recommended for all
  assets/        # images, fonts, wallpapers
```

Themes are **data only**. A theme cannot run code, and every path must stay inside the theme folder.

## Example

```jsonc
{
  "schemaVersion": 1,
  "id": "com.example.my-theme",
  "name": "My Theme",
  "author": "someone",
  "version": "1.0.0",
  "license": "CC-BY-4.0",

  "windows": {
    "wallpaper": "assets/wallpaper.jpg",
    "appearance": "light",
    "accentColor": "#FF0A84FF"
  },

  "fonts": { "ui": "assets/Inter-Regular.ttf", "uiBold": "assets/Inter-Bold.ttf" },
  "tokens": { "glass": "#99F5F5F5", "text": "#FF1D1D1F" },

  "surfaces": [
    {
      "id": "menubar",
      "type": "bar",
      "edge": "top",
      "thickness": 24,
      "reserveSpace": true,
      "style": { "background": "$glass", "foreground": "$text", "blur": true,
                 "font": "ui", "fontSize": 13, "padding": 12, "spacing": 14 },
      "start":  [ { "module": "logo-menu", "icon": "assets/logo.png" }, { "module": "active-app", "style": { "fontWeight": "bold" } } ],
      "center": [],
      "end":    [ { "module": "volume" }, { "module": "clock", "format": "ddd d MMM HH:mm" } ]
    },
    {
      "id": "dock",
      "type": "dock",
      "edge": "bottom",
      "iconSize": 48,
      "magnification": { "enabled": true, "maxScale": 1.6, "radius": 3 },
      "cornerRadius": 18,
      "padding": 6,
      "style": { "background": "#66FFFFFF", "border": "#33FFFFFF", "blur": true },
      "runningIndicator": { "shape": "dot", "color": "$text", "size": 4 },
      "autoHide": false,
      "defaultPinned": [ "explorer.exe", "msedge.exe", "ms-settings:" ]
    }
  ],

  "iconOverrides": { "exe:code.exe": "assets/icons/vscode.png" },
  "iconFolder": "assets/icons",

  "variants": {
    "dark": {
      "tokens": { "glass": "#99202022", "text": "#FFFFFFFF" },
      "windows": { "wallpaper": "assets/wallpaper-dark.jpg" }
    }
  }
}
```

Comments (`//`, `/* */`) and trailing commas are allowed. `theme.json` can be at most 1 MB.

## Reference

Every field is optional unless marked **required**. Defaults are listed in parentheses.

### Metadata

| Field | Type | Notes |
|-------|------|-------|
| `schemaVersion` | number | **Required.** Must be `1`. |
| `id` | string | **Required.** Lowercase letters, digits, `.` and `-`; starts and ends with a letter or digit. It is also the install folder name. |
| `name` | string | **Required.** |
| `author`, `version`, `license` | string | |

### `windows`

The Windows settings a theme may change. The user decides, for each one, whether it comes from the theme (the
default) or stays as Windows has it; light/dark can also be forced to light or dark.

| Field | Type | Notes |
|-------|------|-------|
| `wallpaper` | path | `.png`, `.jpg`, `.jpeg`. Set on every monitor, filling the screen. |
| `appearance` | `light` \| `dark` \| `system` | `system` leaves Windows' mode alone. |
| `accentColor` | color | The alpha byte is ignored. Title bars follow it when "show accent color" is on. |

**Light and dark variants.** Veneer loads the variant named `dark` in dark mode and `light` in light mode (a
theme without a `light` variant uses its base in light mode). The mode is the one Veneer sets, or Windows' own
mode when Veneer follows the system, and the variant switches when that mode changes.

The changes stay when Veneer exits. Before the first change Veneer saves the user's own values, and "Restore
original Windows settings" puts them back.

### `fonts`, `tokens`

- `fonts`: a name → font file (`.ttf`, `.otf`) map.
- `tokens`: a name → value (string, number or boolean) map. Anywhere else in the theme, the string
  `"$name"` is replaced by the token. Tokens cannot reference other tokens. Write `"$$text"` for a literal
  `"$text"`. An unknown token is an error.

### `surfaces`

Each surface has:

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | **Required**, unique. Your overrides and variants find the surface by this id. |
| `type` | `bar` \| `dock` | **Required.** Unknown types are skipped with a warning, so newer themes still load. |
| `edge` | `top` \| `bottom` \| `left` \| `right` | **Required.** A dock cannot use `top`. |
| `monitor` | string | Only `primary` for now. |
| `style.background`, `style.foreground`, `style.border` | color | On a bar, the border is a 1 px line on the side facing the desktop (all around a floating bar). |
| `style.blur` | boolean (`false`) | Real blur behind the surface. Without blur, a translucent background shows the desktop unblurred; a fully transparent bar (`#00000000`) still takes clicks. When Windows "Transparency effects" is off or energy saver is on, blur is turned off and the background is drawn without its transparency (`#66FFFFFF` becomes `#FFFFFFFF`); it comes back when they change back. |
| `style.font` | string | A key of `fonts` (a font file in the theme) or an installed font family. Default: the Windows 11 UI font. |
| `style.fontSize` | number 4–200 (`12`) | In device-independent pixels. |
| `style.fontWeight` | `thin` \| `extralight` \| `light` \| `regular` \| `medium` \| `semibold` \| `bold` \| `extrabold` \| `black` | |
| `style.padding` | number 0–200 (`0`) | Space at both ends of a bar. |
| `style.spacing` | number 0–200 (`0`) | Gap between modules. |
| `style.hover`, `style.pressed` | color | Background of a clickable module under the mouse, and while its menu is open (`pressed` defaults to `hover`). |
| `style.highlightRadius` | number 0–100 (`0`) | Corner radius of that background. |
| `style.highlightInset` | number 0–50 (`2`) | Space between that background and the bar's edges. |
| `style.borderHighlight` | color | Makes the border a vertical gradient: this color at the top, `border` at the bottom, like light on the edge of glass. Pop-up styles accept it too. |

**Bar** (`"type": "bar"`):

| Field | Type | Notes |
|-------|------|-------|
| `thickness` | integer 8–200 (`28`) | Height for top/bottom bars, width for left/right bars. |
| `reserveSpace` | boolean (`true`) | Maximized windows stay clear of the bar. |
| `margin` | number 0–200 (`0`) | Above 0 the bar floats: it keeps this distance from the screen edge and from both ends of its edge. The space it reserves is `thickness` + `margin`. A floating bar is outlined all around and keeps its window shadow. |
| `cornerRadius` | number 0–100 (`0`) | Up to 8 physical pixels (8 at 100% scaling) uses native Windows rounding, which rounds to 4 or 8 px only. Larger values draw the bar like the dock's plate, blur included (no shadow). |
| `defaultPinned` | array of strings | The pinned apps of the bar's `apps` modules. Same entries and user changes as a dock's (see below). |
| `start`, `center`, `end` | array of modules | Each module is `{ "module": "<name>", ...options }`. |

Modules: `logo-menu`, `active-app`, `clock`, `volume`, `network`, `battery`, `spacer`, `apps`. Unknown
modules are skipped with a warning. A module's `icon` option is a theme path (`.png`, `.ico`, `.jpg`, `.jpeg`).

Every module accepts a `style` with `foreground`, `font`, `fontSize`, `fontWeight` and `padding` (space on
both sides of the module). Missing values come from the bar's `style`. A `style.background` puts the module on a
tile of that color (`style.cornerRadius`, number 0–100, rounds it); the tile spans the bar minus
`highlightInset`, and a clickable module's hover background fills it.

| Module | Options |
|--------|---------|
| `logo-menu` | `icon`: theme image. A click opens the system menu: About this PC, Settings, Sleep, Restart, Shut Down (these three ask for confirmation), Lock Screen. |
| `active-app` | `desktopName` (`"Desktop"`): shown when the desktop or the taskbar is in front. `names`: exe → display name, e.g. `{ "explorer.exe": "Files" }`. A click opens Close Window, Minimize, Quit. The app's own menus are never read. |
| `clock` | `format`: a .NET date/time format (`"HH:mm"`). Uses the Windows regional language. |
| `volume` | `showPercent` (`false`). Click mutes, the mouse wheel changes the level. `icons` states: `muted`, `off`, `low`, `medium`, `high`. |
| `network` | Click opens the network settings. `icons` states: `wifi`, `ethernet`, `offline`. |
| `battery` | `showPercent` (`true`). Hidden on PCs without a battery. `icons` states: `empty`, `low`, `medium`, `high`, `full`, `charging`. |
| `spacer` | `size`: number 0–1000 (`8`), empty space along the bar. |
| `apps` | Pinned apps (the bar's `defaultPinned`) and running apps, like a taskbar. `iconSize`: integer 12–64 (`24`). `itemPadding`: number 0–50 (`6`), space on both sides of each icon. `runningIndicator`: `shape`, `color`, `size` as on a dock (default: a 3 px dot in the text color), on the screen-edge side. `separateRunning` (`false`). A click shows the app's last used window or starts it; a right click opens Pin to Bar / Unpin from Bar / Close. Not reorderable by dragging yet. |

Status modules show a glyph from the Windows icon font in the text color. A theme can replace it with its own
image per state: `"icons": { "muted": "assets/volume-muted.png", "high": "assets/volume-high.png" }`. Unknown
state names are ignored with a warning; missing states keep the glyph.

**Dock** (`"type": "dock"`):

| Field | Type | Notes |
|-------|------|-------|
| `iconSize` | integer 16–256 (`48`) | |
| `magnification.enabled` | boolean (`true` when the object is present) | Icons grow under the mouse and spill out of the dock. |
| `magnification.maxScale` | number 1–3 (`1.5`) | Scale of the icon right under the mouse. |
| `magnification.radius` | integer 0–10 (`3`) | How many icons on each side grow (cosine falloff). |
| `cornerRadius` | number 0–100 (`0`) | Up to 8 uses native Windows rounding. Larger values draw the plate with smooth rounded corners of that radius, blur included (no shadow). |
| `padding` | number 0–100 (`0`) | Space around the icons. The running indicator sits in the padding on the screen-edge side. |
| `margin` | number 0–200 (`0`) | Distance from the screen edge. |
| `reserveSpace` | boolean (`true`) | Maximized windows stay clear of the dock. |
| `style.spacing` | number 0–200 (`0`) | Gap between icons. |
| `runningIndicator.shape` | `dot` \| `line` (`dot`) | |
| `runningIndicator.color` | color | |
| `runningIndicator.size` | number 1–20 (`4`) | |
| `runningIndicator.mode` | `app` \| `window` (`app`) | `window`: one mark per open window, up to three, a mark's size apart (lines share the length). |
| `autoHide` | boolean (`false`) | Hidden until the mouse touches the screen edge; hides again 0.6 s after it leaves. An auto-hidden dock never reserves space. |
| `separateRunning` | boolean (`true`) | A separator between the pinned apps and the other running apps. |
| `defaultPinned` | array of strings | See below. |
| `label` | pop-up style | The app name shown above the icon under the mouse. Without it, Windows tooltips. |
| `bounceOnLaunch` | boolean (`false`) | The icon hops a few times when its app is started from the dock. |
| `downloads` | boolean (`false`) | A Downloads item at the end of the dock, after a separator. A click lists the 10 newest files and folders of the Windows Downloads folder (unfinished downloads, hidden and system files left out) and "Open Folder". The user can turn it on or off in Settings. |
| `trash` | boolean (`false`) | A Recycle Bin item at the very end of the dock. Its icon shows empty or full (the shell tells Veneer, no polling). A click opens the bin; the menu has "Empty Recycle Bin…", which goes through Windows' own confirmation; files dropped on it go to the bin through Windows, never deleted for good. The user can turn it on or off in Settings. |

Pinned entries can be:

- an exe file name (`"explorer.exe"`, found like the Run dialog does) or a full path. A full path to an app
  installed by Squirrel (`...\Discord\Discord.exe`, the stub next to `Update.exe`) also matches the copy that
  runs from its versioned folder (`...\Discord\app-1.0.9269\Discord.exe`), so the pin survives updates;
- an AppUserModelID for Store and packaged apps (`"Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"`). Pin
  Store apps this way: a Store app's exe name does not match its running windows (`calc.exe` starts the
  Calculator app, which runs as `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`);
- `ms-settings:`, for Settings;
- `"---"`, a separator line.

The user can change the pinned apps from the dock (right click: Keep in Dock, Remove from Dock; drag to
reorder), or import the apps pinned to the Windows taskbar (offered on first run, and from Settings ›
Customize; the import replaces the list). The new list goes to the user's overrides as
`surfaces[id=<dock id>].defaultPinned`, so it survives theme updates, and other themes with a dock of the same `id`
use it too. A bar with an `apps` module works the same way, with its own `id`: right click Pin to Bar / Unpin from
Bar, and the taskbar import fills it too.

Running apps that are not pinned follow the pinned ones, in the order they were opened. Windows of the same
app are grouped by AppUserModelID, or by exe when the app has none.

### `menu`

The look of Veneer's menus (logo menu, active app, dock). Without it, menus keep the Windows 11 look. It is a
**pop-up style**, like the dock's `label`:

| Field | Type | Notes |
|-------|------|-------|
| `background`, `foreground`, `border` | color | |
| `borderHighlight` | color | Top color of a gradient border (see `style.borderHighlight`). |
| `blur` | boolean (`false`) | Blur behind the menu. Blurred menus have no drop shadow. Turned off like the surfaces' blur (see `style.blur`). |
| `cornerRadius` | number 0–50 (`0`) | |
| `padding` | number 0–100 (`4`) | |
| `font`, `fontSize`, `fontWeight` | | As in `style`. |
| `highlight`, `highlightForeground` | color | The menu item under the mouse. |
| `separator` | color | Separator lines. |
| `shadow` | boolean (`true`) | A soft drop shadow (not with `blur`). |

### Icons

- `iconOverrides`: keys are `exe:<name.exe>` or `aumid:<AppUserModelID>`, values are image paths. The dock's own items use `dock:downloads`, `dock:trash` and `dock:trash-full` (a bin with files in it; without it, `dock:trash` is used for both states). Without them, the Windows icons are used.
- `iconFolder`: a folder of images named after the exe, for example `discord.png` for `discord.exe`.
- Order: `iconOverrides` (`aumid:` first, then `exe:`), then `iconFolder`, then the icon Windows shows.

### `variants`

A variant is a partial theme applied on top of the base, for example `dark`. It uses the same shape as the
theme and the same merge rules as user overrides (see below). If the requested variant does not exist, the
base theme is used.

## Values

- **Colors:** `#RRGGBB` or `#AARRGGBB` (alpha first).
- **Paths:** relative to the theme folder, with `/` or `\`. Absolute paths, drive letters, `..`, links
  (symlinks, junctions) and other file types are errors. A file that does not exist is only a warning: the
  value is ignored.

## Layers and user overrides

A theme is applied in three layers: **theme → variant → user overrides**. The user's overrides file has the
same shape as `theme.json` but holds only the changed values:

```json
{ "surfaces": [ { "id": "dock", "iconSize": 56 } ] }
```

Merge rules:

- Objects merge key by key.
- `surfaces` merge by `id`. A user override for a surface the theme no longer has is kept in the file, is
  not applied, and is reported as orphaned.
- Every other array (module lists, `defaultPinned`) is replaced as a whole.
- `null` removes the value, so the default applies.

Switching or updating a theme never changes the overrides file.

## Errors

Veneer never crashes on a bad theme. Each problem is reported with its path, for example
`surfaces[id=dock].iconSize: 900 is out of range (16–256)`.

- **Errors** reject the theme, and the previous theme stays active: invalid JSON, a wrong type, a value out of
  range, an unknown token, an unsafe path, an unsupported `schemaVersion`.
- **Warnings** keep the theme: unknown surface types or modules, missing files, orphaned overrides. Unknown
  fields are ignored.
- A broken overrides file is ignored with a warning, and the theme loads without it.
- Overrides that make the theme invalid (a value out of range, a token the theme does not have) are not applied:
  the theme loads without them, and their problems are listed as warnings. The file is never changed.
- When Veneer writes the overrides file (for example after pinning an app), comments cannot be kept. A file that
  has comments is first copied to `overrides.json.bak`.

## Installing from a `.zip`

The zip holds the theme folder's contents, either at its root or inside a single folder. Limits:

- 100 MB unpacked in total, 30 MB per file, 2,000 files.
- Allowed files: `.json`, `.png`, `.ico`, `.jpg`, `.jpeg`, `.ttf`, `.otf`, `.txt`, `.md`, `LICENSE`, `CREDITS`.
- Paths outside the folder and links are rejected.
- macOS metadata (`__MACOSX/`, `.DS_Store`) is skipped.

The theme is validated before it is installed. An existing theme with the same `id` is replaced only when
the new one is valid.
