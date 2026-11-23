# Theme format (draft)

> The format is a draft and will change until the first release. This page will be completed in
> Phase 2 (theme engine).

A theme is a folder, or a `.zip` with the same structure:

```
my-theme/
  theme.json
  LICENSE        # or CREDITS: required for bundled themes, recommended for all
  assets/        # .png .ico .jpg .jpeg .ttf .otf
```

`theme.json` declares:

- **metadata**: `schemaVersion`, `id`, `name`, `author`, `version`, `license`;
- **windows** (optional): `wallpaper`, `appearance` (`light` | `dark` | `system`), `accentColor`;
- **fonts** and **tokens** (reusable values referenced as `"$name"`);
- **surfaces**: the layout. Each surface has an `id`, a `type` (`bar` or `dock`), an `edge`
  (`top`, `bottom`, `left`, `right`), a `monitor` and its own style. Bars list their modules in `start`,
  `center` and `end`;
- **icons**: `iconOverrides` (by `exe:` or `aumid:`) and an `iconFolder` where `discord.png` maps to
  `discord.exe`;
- **variants**: for example `dark`, which overrides tokens and settings.

Themes are data only. They cannot run code, and paths must stay inside the theme folder.
