# Developer tools

Helper tools for theme authors and developers, such as converting an SVG icon set to PNG for a local
theme. Tools here are never used by the theme loader, and they must never write into the repository.

- `theme-art/make-art.ps1`: draws the original wallpapers and logo of a bundled theme into
  `themes/<theme>/assets` (`pwsh -File tools/theme-art/make-art.ps1 -Theme aqua`, or `-Theme strip`).
- `app-icon/make-icon.ps1`: draws Veneer's application icon into `src/Veneer.App/veneer.ico`
  (`pwsh -File tools/app-icon/make-icon.ps1`).
- `svg-icons`: converts the SVG icons named in an exe → svg map into 256 px PNGs for a theme's `iconFolder`
  (`dotnet run --project tools/svg-icons -- <map.json> <svg folder> <output folder>`). See
  `svg-icons/icon-map.example.json`. It refuses an output folder inside any git repository, SVGs under a `links`
  folder, links and paths that leave the SVG folder. Missing or broken icons are listed and skipped (exit code 1).
