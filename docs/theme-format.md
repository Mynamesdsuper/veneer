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

The Windows settings a theme may change. The user can turn each one off.

| Field | Type | Notes |
|-------|------|-------|
| `wallpaper` | path | `.png`, `.jpg`, `.jpeg` |
| `appearance` | `light` \| `dark` \| `system` | |
| `accentColor` | color | |

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
| `style.background`, `style.foreground`, `style.border` | color | On a bar, the border is a 1 px line on the side facing the desktop. |
| `style.blur` | boolean (`false`) | Real blur behind the surface. Without blur, a translucent background shows the desktop unblurred. |
| `style.font` | string | A key of `fonts` (a font file in the theme) or an installed font family. Default: the Windows 11 UI font. |
| `style.fontSize` | number 4–200 (`12`) | In device-independent pixels. |
| `style.fontWeight` | `thin` \| `extralight` \| `light` \| `regular` \| `medium` \| `semibold` \| `bold` \| `extrabold` \| `black` | |
| `style.padding` | number 0–200 (`0`) | Space at both ends of a bar. |
| `style.spacing` | number 0–200 (`0`) | Gap between modules. |

**Bar** (`"type": "bar"`):

| Field | Type | Notes |
|-------|------|-------|
| `thickness` | integer 8–200 (`28`) | Height for top/bottom bars, width for left/right bars. |
| `reserveSpace` | boolean (`true`) | Maximized windows stay clear of the bar. |
| `start`, `center`, `end` | array of modules | Each module is `{ "module": "<name>", ...options }`. |

Modules: `logo-menu`, `active-app`, `clock`, `volume`, `network`, `battery`, `spacer`. Unknown modules
are skipped with a warning. A module's `icon` option is a theme path (`.png`, `.ico`, `.jpg`, `.jpeg`).

Every module accepts a `style` with `foreground`, `font`, `fontSize`, `fontWeight` and `padding` (space on
both sides of the module). Missing values come from the bar's `style`.

| Module | Options |
|--------|---------|
| `logo-menu` | `icon`: theme image. A click opens the system menu: About this PC, Settings, Sleep, Restart, Shut Down (these three ask for confirmation), Lock Screen. |
| `active-app` | `desktopName` (`"Desktop"`): shown when the desktop or the taskbar is in front. `names`: exe → display name, e.g. `{ "explorer.exe": "Files" }`. A click opens Close Window, Minimize, Quit. The app's own menus are never read. |
| `clock` | `format`: a .NET date/time format (`"HH:mm"`). Uses the Windows regional language. |
| `volume` | `showPercent` (`false`). Click mutes, the mouse wheel changes the level. `icons` states: `muted`, `off`, `low`, `medium`, `high`. |
| `network` | Click opens the network settings. `icons` states: `wifi`, `ethernet`, `offline`. |
| `battery` | `showPercent` (`true`). Hidden on PCs without a battery. `icons` states: `empty`, `low`, `medium`, `high`, `full`, `charging`. |
| `spacer` | `size`: number 0–1000 (`8`), empty space along the bar. |

Status modules show a glyph from the Windows icon font in the text color. A theme can replace it with its own
image per state: `"icons": { "muted": "assets/volume-muted.png", "high": "assets/volume-high.png" }`. Unknown
state names are ignored with a warning; missing states keep the glyph.

**Dock** (`"type": "dock"`):

| Field | Type | Notes |
|-------|------|-------|
| `iconSize` | integer 16–256 (`48`) | |
| `magnification.enabled` | boolean (`true` when the object is present) | |
| `magnification.maxScale` | number 1–3 (`1.5`) | |
| `magnification.radius` | integer 0–10 (`3`) | How many neighbor icons grow. |
| `cornerRadius` | number 0–100 (`0`) | Up to 8 uses native Windows rounding; larger values clip the window (sharper edge, no shadow). |
| `padding` | number 0–100 (`0`) | Space around the icons. The running indicator sits in the padding on the screen-edge side. |
| `margin` | number 0–200 (`0`) | Distance from the screen edge. |
| `reserveSpace` | boolean (`true`) | Maximized windows stay clear of the dock. |
| `style.spacing` | number 0–200 (`0`) | Gap between icons. |
| `runningIndicator.shape` | `dot` \| `line` (`dot`) | |
| `runningIndicator.color` | color | |
| `runningIndicator.size` | number 1–20 (`4`) | |
| `autoHide` | boolean (`false`) | |
| `defaultPinned` | array of strings | See below. |

Pinned entries can be:

- an exe file name (`"explorer.exe"`, found like the Run dialog does) or a full path;
- an AppUserModelID for Store and packaged apps (`"Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"`). Pin
  Store apps this way: a Store app's exe name does not match its running windows (`calc.exe` starts the
  Calculator app, which runs as `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`);
- `ms-settings:`, for Settings.

Running apps that are not pinned follow the pinned ones, in the order they were opened. Windows of the same
app are grouped by AppUserModelID, or by exe when the app has none.

### Icons

- `iconOverrides`: keys are `exe:<name.exe>` or `aumid:<AppUserModelID>`, values are image paths.
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

## Installing from a `.zip`

The zip holds the theme folder's contents, either at its root or inside a single folder. Limits:

- 100 MB unpacked in total, 30 MB per file, 2,000 files.
- Allowed files: `.json`, `.png`, `.ico`, `.jpg`, `.jpeg`, `.ttf`, `.otf`, `.txt`, `.md`, `LICENSE`, `CREDITS`.
- Paths outside the folder and links are rejected.
- macOS metadata (`__MACOSX/`, `.DS_Store`) is skipped.

The theme is validated before it is installed. An existing theme with the same `id` is replaced only when
the new one is valid.
