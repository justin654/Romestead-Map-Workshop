# Romestead Map Workshop

A community map-editing tool for [Romestead](https://store.steampowered.com/app/1805320/Romestead/).
It rips the game's `Content/` tree into a clean working folder, converts the XNB
textures to PNG, fixes tileset references, and launches Tiled with everything
pointing at the right place.

> **Note:** Standalone preview build by [justin654](https://github.com/justin654).
> Mod packing (install edited maps as a mod) is not in this release yet. A Romestead
> mod loader exists for development but is not published for general use; Pack will
> return in Map Workshop when that loader is ready to share. Until then you can rip,
> preview, and edit maps in Tiled.

## Features

- **Status pills** showing whether you've ripped the game Content, converted
  XNB textures to PNG, fixed tileset paths, and have Tiled installed.
- **Searchable map tree** rooted at your ripped `maps/` folder.
- **In-app preview** — composites tile layers from `.tsx` tilesets *and* image
  layers, matching how Tiled renders the map. Handles CSV and base64+zlib/gzip
  layer encodings, plus tile flip flags.
- **One-click "Open in Tiled"** — automatically runs XNB→PNG conversion and
  tileset-path repair before launching Tiled, so the map opens with all
  tilesets resolved.
- **Streamed log + progress bar** for long ops (full rips, batch xnb conversion).

## Requirements

- Windows 10 / 11 (64-bit)
- [Romestead](https://store.steampowered.com/app/1805320/Romestead/) installed
- [Tiled](https://www.mapeditor.org/) (free) — needed to actually edit maps

If you grabbed the *self-contained* release you don't need .NET installed.
If you grabbed the framework-dependent build you need the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

`xnbcli.exe` is downloaded automatically from
[LeonBlade/xnbcli](https://github.com/LeonBlade/xnbcli) the first time you
need it.

## Install

1. Grab the latest `MapWorkshop-win-x64.zip` from the
   [Releases](../../releases) page.
2. Extract it anywhere (Desktop, Documents, wherever).
3. Run `MapWorkshop.exe`.

On first launch the tool auto-detects your Romestead install via Steam.
If it can't find it (non-Steam install, unusual library folder), a folder
picker pops up — point it at the directory containing `Romestead.exe`.

The choice is remembered in `%LOCALAPPDATA%\Romestead.MapWorkshop\config.json`.
You can change it later with the **Game folder...** button.

## Typical workflow

1. **Rip game Content** — choose a profile and click *Rip*:
   - `MapAuthor`: maps + tilesets + media/tiles + media/map_backgrounds (smallest)
   - `Interiors`: same but only interiors_new and building_exteriors maps
   - `Full`: the entire Content tree (~1.5 GB)
2. **Pick a map** on the left, double-check the preview on the right.
3. **Open in Tiled** — Map Workshop converts any pending XNBs, fixes any
   broken tileset paths, then launches Tiled with the map.
4. Edit + save in Tiled.

(Packing maps into installable mods needs the Romestead mod loader, which is not
released publicly yet. Map Workshop will add Pack back when it is.)

## Where things live

| What | Where |
| --- | --- |
| Ripped game Content (working tree) | `%LOCALAPPDATA%\Romestead.MapWorkshop\workspace\ripped\Content` |
| xnbcli download | `%LOCALAPPDATA%\Romestead.MapWorkshop\tools\xnbcli` |
| Config & crash log | `%LOCALAPPDATA%\Romestead.MapWorkshop\` |

Nothing is written inside your game folder.

## Building from source

Requires the .NET 8 SDK.

```sh
git clone https://github.com/justin654/Romestead-Map-Workshop.git
cd Romestead-Map-Workshop
dotnet build -c Debug          # quick iteration
dotnet publish -c Release      # self-contained single-file exe (default in csproj)
```

The published binary lands at
`bin/Release/net8.0-windows/win-x64/publish/MapWorkshop.exe`.

`publish.ps1` is a one-line convenience wrapper around `dotnet publish`.

## Publishing a GitHub release

The workflow in `.github/workflows/release.yml` builds the Windows zip and attaches it
to a GitHub **Release**. It does **not** run on every push to `master` — only when you:

1. **Tag a release** (recommended for community downloads):

   ```sh
   git tag v0.1.0
   git push origin v0.1.0
   ```

   Use a `v`-prefixed semver tag (`v0.1.0`, `v1.2.3`, …). After the workflow finishes,
   check the [Releases](../../releases) page for `MapWorkshop-win-x64-0.1.0.zip`.

2. **Or run manually:** GitHub → **Actions** → **Release** → **Run workflow**. That
   uploads a zip under **Artifacts** on the run (not on the Releases page).

Until you do one of those, the Actions tab will show **0 workflow runs** even though
the workflow file is present — that is expected, not a broken `.github` folder.

## Defender false positives

Fresh, unsigned WinForms apps that download + extract a zip and then spawn the
extracted .exe will trip Microsoft Defender's behavioral heuristics
(`!MTB` family). If you see a quarantine warning right after first launch:

1. Open Windows Security → Protection history → click the detection.
2. Choose **Allow on device**, optionally submit it at
   [Microsoft's false-positive form](https://www.microsoft.com/wdsi/filesubmission).

Or, if you'd rather not deal with that, build from source yourself.

## License

Map Workshop is released under the [MIT License](LICENSE) (Copyright (c) 2026 Justin).

- **This license applies only to Map Workshop** — its source code and distributed
  binary. It does not grant rights to Romestead, its trademarks, or any game
  assets (maps, textures, audio, etc.).
- **Ripping Content** copies files from your own game install into a local workspace
  for personal editing. Do not redistribute ripped game files; follow Romestead's
  terms and your platform's rules.
- **Third-party tools:** [xnbcli](https://github.com/LeonBlade/xnbcli) (MIT) and
  [Tiled](https://www.mapeditor.org/) have their own licenses.

Romestead is a trademark of its respective owner. This is an unofficial fan tool
and is not affiliated with or endorsed by the game developers.
