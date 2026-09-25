# ModCollector Tool Plan

Status: work in progress; last updated 2026-06-22.

## What it is

A CLI tool (`Panopticon.Tools.ModCollector`) that downloads and assembles a local asset
collection from ModDB for use with Panopticon. It follows the same pattern as
`Panopticon.Tools.BigTool` and `Panopticon.Tools.Sav2Json`.

## Design decisions

### Tool type

CLI: `CommandLine` + `CommandParser`, `Program.cs` entry point. Match
`src/Panopticon.Tools.BigTool/Panopticon.Tools.BigTool.csproj` exactly (no game install required).
TFM is inherited from `src/Directory.Build.props` (`net10.0`); do not set it in the project file.

Suggested project path: `src/Panopticon.Tools.ModCollector/`

### Scope

All supported games:

| SageGame | ModDB game slug |
|---|---|
| CncGenerals | `command-and-conquer-generals` |
| CncGeneralsZeroHour | `command-and-conquer-generals-zero-hour` |
| Bfme | `the-lord-of-the-rings-the-battle-for-middle-earth` |
| Bfme2 | `battle-for-middle-earth-ii` |
| Bfme2Rotwk | `battle-for-middle-earth-ii-rise-of-the-witch-king` |

### Source

ModDB only. No official REST API exists; scraping is the only programmatic option.

First version: curated static manifest (JSON embedded resource) of known-good download
URLs, names, game, and category. This is more robust than scraping for an initial
implementation.

### Output layout

```
<collection-root>/
  cnc-generals-zero-hour/
    maps/
    mods/
    addons/
  battle-for-middle-earth-ii/
    maps/
    mods/
    addons/
  ...
```

### Post-download processing

Extract `.big` files from downloaded `.zip` archives; discard the archive.
Leave `.big` files engine-ready so the user can point `CNC_GENERALS_ZH_PATH` (etc.)
at a game+category directory and Panopticon picks them up via `SkudefReader`.

Key refs:
- `src/Panopticon.IO/SkudefReader.cs`: recursively scans a root dir for `.big` files
  in alphabetical order.
- `src/Panopticon.FileFormats.Big/`: `BigArchive` reads the `.big` format.
- `src/Panopticon.Tools.BigTool/Program.cs:201-221`: `ExtractMode` logic to borrow.

### ModDB access strategy

Three options evaluated:

1. Call the Python `moddb` PyPI library as a subprocess; fragile, adds a Python dep.
2. Reimplement the relevant HTTP scraping in C#; maintainable long-term.
3. Curated static JSON manifest of known-good download URLs; most robust for v1.

Decision: start with option 3 (curated manifest). Upgrade to option 2 later.

### Error handling

If a download fails (HTTP error, timeout, network error): retry once after a 2-second delay.
If the retry also fails: log the error to stderr and skip that entry; continue with remaining
downloads. Exit code 0 if any entries succeeded; non-zero if all failed.

### Work item card prefix

Cards for this tool use the `WI-MC-` prefix (`MC` = ModCollector). Cards live in
`docs/roadmap/agent/cards/`.

## CLI surface

```
mod-collector --list [--game <slug>] [--category maps|mods|addons]
mod-collector --download [--game <slug>] [--category maps|mods|addons] --output <dir>
mod-collector --extract --output <dir>   # unpack any downloaded zips to .big in place
```

## Curated manifest seed content

JSON embedded resource. Initial entries:

| Mod | Game | Category |
|---|---|---|
| Contra | CncGeneralsZeroHour | mods |
| Shockwave | CncGeneralsZeroHour | mods |
| NProject Mod | CncGeneralsZeroHour | mods |
| Edain Mod | Bfme2Rotwk | mods |
| Age of the Ring | Bfme2Rotwk | mods |
| (handful of map packs per game) | all | maps |

Actual ModDB download URLs to be filled in during implementation; verify each link
is a direct `.zip` download before committing.

## Implementation steps

1. Create `src/Panopticon.Tools.ModCollector/` with `.csproj` matching BigTool
   (`OutputType=Exe`, `CommandLineParser`, net8.0, reference `Panopticon.FileFormats.Big`).
2. Add to `src/Panopticon.sln`.
3. Create `manifest.json` as an embedded resource with the seed mod entries above
   (schema: `[{ "name", "game", "category", "moddbUrl", "downloadUrl" }]`).
4. Implement `--list` mode (reads manifest, prints a table via `ConsoleTables`).
5. Implement `--download` mode (HTTP GET each `downloadUrl`, save `.zip` to
   `<output>/<game>/<category>/<name>.zip`).
6. Implement `--extract` mode (for each `.zip` in output tree, find `.big` files inside
   and extract them to the same directory; delete the `.zip`). Borrow `ExtractMode`
   logic from BigTool.
7. Wire up `Program.cs` with `CommandLine` verbs or flags following the BigTool pattern.

## Out of scope for v1

- Live ModDB scraping (no dynamic discovery of new mods).
- Installer/launcher integration.
- Automatic `SageGame` path env-var configuration.
- Mod dependency resolution.
