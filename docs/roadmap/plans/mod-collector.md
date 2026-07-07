# ModCollector Tool Plan

> Design authority for `OpenSage.Tools.ModCollector` and the WI-MC task cards. Cards reference
> this document for conventions and rationale. The executable specification lives in the card
> steps.
>
> Used by [Track D: Gameplay Content Expansion](../track-d-gameplay-expansion.md), phase D3,
> which stages mod archives through this tool before wiring them into the engine's `AssetStore`.
>
> Audience: human overview of the ModCollector design.

---

## Purpose

ModCollector is a command-line tool that downloads and stages community mods for use with
OpenSAGE. Without it, a user must find a mod's direct download URL, extract the archive, identify
the `.big` files inside, and place them where the engine can find them. ModCollector automates
those steps.

The tool is a single-operator personal-use utility, matching the
[framework audience decision](../open-questions.md). It is not a package manager, does not manage
mod versions beyond the static embedded manifest, and has no update mechanism beyond shipping a new
build with a new manifest.

---

## Design overview

Three verbs, run in sequence:

```
modcollector list   [--game <slug>] [--category <slug>]
modcollector download --output <dir> [--game <slug>] [--category <slug>]
modcollector extract --output <dir>
```

`list` reads the embedded manifest and prints a filtered table : no network access.

`download` iterates the manifest (filtered), fetches each `downloadUrl` as a `.zip`, saves it
under `<output>/<game>/<category>/<name>.zip`, skips already-present files, and retries once
on failure. Entries whose `downloadUrl` is empty or whose `status` is `"unverified"` are
skipped with a diagnostic message.

`extract` walks the output tree, opens each `.zip`, extracts any `.big` entries into the same
directory as the archive, then deletes the `.zip`. The result is a directory tree of `.big`
files that OpenSAGE can consume directly via `SkudefReader` / the virtual file system.

---

## Engine integration

After `extract`, the output tree has the shape:

```
<output>/
  <game-slug>/
    <category>/
      <ModName>.big
      ...
```

Set the matching game path environment variable, for example `CNC_GENERALS_ZH_PATH`, to
`<output>/<game-slug>/<category>` or to `<output>/<game-slug>` if multiple categories share one
path. OpenSAGE then picks up the `.big` files through the existing `SkudefReader`; the
`src/OpenSage.Mods.*/` projects already use `SkudefReader` to enumerate additional `.big` files.
No engine changes are required.

---

## Game slug table

Game slugs are the string values stored in `manifest.json` entries and accepted by `--game`.
They are derived from the `SageGame` enum (`src/OpenSage.Game/SageGame.cs`) by lowercasing and
replacing camel-case word boundaries with hyphens. Games not (yet) supported for mod download
are listed for completeness.

| `SageGame` enum value | Manifest slug | Notes |
|---|---|---|
| `CncGenerals` | `cnc-generals` | C&C Generals |
| `CncGeneralsZeroHour` | `cnc-generals-zero-hour` | C&C Generals: Zero Hour |
| `Bfme` | `bfme` | Battle for Middle-earth |
| `Bfme2` | `bfme2` | Battle for Middle-earth II |
| `Bfme2Rotwk` | `bfme2-rotwk` | Rise of the Witch-king |
| `Cnc3` | `cnc3` | Not targeted : no mod entries in initial manifest |
| `Cnc3KanesWrath` | `cnc3-kanes-wrath` | Not targeted : no mod entries in initial manifest |
| `Ra3` | `ra3` | Not targeted : no mod entries in initial manifest |
| `Ra3Uprising` | `ra3-uprising` | Not targeted : no mod entries in initial manifest |
| `Cnc4` | `cnc4` | Not targeted : no mod entries in initial manifest |

---

## Manifest schema

`manifest.json` is an embedded resource : a JSON array of objects. Each object has:

| Field | Type | Description |
|---|---|---|
| `name` | string | Human-readable mod name, used as the filename stem (`<name>.zip` / `<name>.big`). |
| `game` | string | Game slug (see table above). |
| `category` | string | One of `mods`, `maps`, `addons`. |
| `moddbUrl` | string | ModDB page URL for attribution/reference (may be empty if not on ModDB). |
| `downloadUrl` | string | Direct `.zip` download URL. Empty string if not yet verified. |
| `status` | string | `"ok"` if `downloadUrl` is a verified direct download; `"unverified"` otherwise. |

Entries with `status: "unverified"` or an empty `downloadUrl` still appear in `list` so the user
knows they exist, but `download` skips them. The agent implementing WI-MC-02 must verify each
`downloadUrl` with `curl -I <url>` before setting `status: "ok"`.

---

## Manifest seed entries

These are the intended initial entries. URLs must be verified by the WI-MC-02 agent before
marking `status: "ok"`. Do not commit `status: "ok"` without a successful `curl -I` check
confirming a direct `.zip` (or equivalent archive) response.

### C&C Generals: Zero Hour (`cnc-generals-zero-hour`)

| Name | Category | ModDB page |
|---|---|---|
| `contra` | `mods` | https://www.moddb.com/mods/contra |
| `shockwave` | `mods` | https://www.moddb.com/mods/shockwave |
| `nproject-mod` | `mods` | https://www.moddb.com/mods/nproject-mod |

### Battle for Middle-earth II (`bfme2`)

| Name | Category | ModDB page |
|---|---|---|
| `edain-mod` | `mods` | https://www.moddb.com/mods/edain-mod |

### Battle for Middle-earth II: Rise of the Witch-king (`bfme2-rotwk`)

| Name | Category | ModDB page |
|---|---|---|
| `age-of-the-ring` | `mods` | https://www.moddb.com/mods/age-of-the-ring |

Map packs for each game should be added once at least one mod entry per game is confirmed
working end-to-end; they follow the same schema with `category: "maps"`.

---

## Non-goals

- Versioning or upgrading already-downloaded mods. Re-downloading requires manually deleting
  the existing file.
- Conflict detection between mods.
- Automatic manifest updates at runtime : the manifest is embedded at build time.
- Support for mods distributed as `.exe` installers or requiring patching steps.
- Any GUI. The tool is CLI-only.

---

## Project layout

```
src/OpenSage.Tools.ModCollector/
  OpenSage.Tools.ModCollector.csproj
  Program.cs
  manifest.json                  (embedded resource)
  ManifestEntry.cs
  ManifestLoader.cs
  ListOptions.cs
  DownloadOptions.cs
  Downloader.cs
  ExtractOptions.cs
  Extractor.cs
```

Dependencies: `CommandLineParser`, `ConsoleTables`, `OpenSage.FileFormats.Big` (needed only
for verifying `.big` entries during extract; the actual extraction uses `System.IO.Compression`).

---

## Task card index

| Card | Description | Status |
|---|---|---|
| `WI-MC-01` | Scaffold project skeleton | Ready |
| `WI-MC-02` | Manifest model and `list` verb | Ready |
| `WI-MC-03` | `download` verb | Ready |
| `WI-MC-04` | `extract` verb | Ready |
