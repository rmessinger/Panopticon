# Task Card `WI-X3-01`: Open-source cursor theme evaluation and examples

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X3-01` |
| Roadmap phase | [X3](../../track-x-fixes-tweaks.md): cursor modernization |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Not required: this read-only card produces no source edits. Its findings ground the loader diagrams required before any X3 source-edit card becomes `Ready` |

## Goal

Evaluate the recommended replacement path for the original cursor set: Bibata (GPL-3.0) for base pointer and scroll cursors, and game-icons.net SVGs (CC BY 3.0) for RTS action cursors. Produce candidate cursors and side-by-side comparison images for operator inspection, plus a recorded licensing and loader assessment.

## Context to read (and only this)

- `src/Panopticon.Simulation/Input/Cursors/CursorManager.cs:40-80`: `MouseCursor.Image` resolves to `Data/Cursors/<name>.ani`, with a lowercase fallback.
- `src/Panopticon.Simulation/Input/Cursors/Cursor.cs:23-95`: SDL hardware colour cursors; non-unity `CursorScale` resizes the single source image with ImageSharp.
- `src/Panopticon.Simulation/Input/Cursors/MouseCursor.cs`: INI fields `Image`, `Texture`, `Directions`, `HotSpot`.
- `src/Panopticon.Simulation/Data/Ani/CursorFile.cs`, `AniFile.cs`, `IconChunkContent.cs` under `src/Panopticon.Simulation/Data/Ani/`: supported `.ani`/`.cur` subset, frame rates in 1/60 s jiffies, and handling of multi-size icon frames.
- `src/Panopticon.Simulation/GameWindow.cs:70-73`: `CursorScale` derivation.
- `src/Panopticon.Simulation/Logic/OrderGenerators/Cursors.cs`: which cursor names gameplay requests.
- Installed `Data/Cursors/` (52 `.ani` files) and the `MouseCursor` blocks in `INI.big` (`Data/INI/Mouse.ini`). Inspect without committing source assets.
- [Track G](../../track-g-rendering-clarity.md) G1 and G2 rows: high-DPI cursor scale and the `Faithful`/`Enhanced` preset convention.
- `docs/screenshots.md`: shared-directory layout for inspection artifacts.

## Steps

1. Inventory the original set. For each installed cursor, record the `MouseCursor` name(s), file, frame count, frame size, hotspot, frame rate, and role (`pointer`, `scroll`, `action`, `invalid-action`). Note cursors with `Directions` > 1.
2. Choose an example subset of 12 or fewer covering every role: pointer, select, move, attack, attack-move, guard, no-action, one scroll direction, and at least two animated cursors.
3. Pin sources. Record the Bibata Windows release version, download URL, and license file; record each game-icons.net icon used, its author, and URL. Assess whether each can be committed under `LICENSE.md` and `LICENSE-EA.md`, or must remain a user-installed optional pack. Do not commit any third-party asset.
4. Build candidates outside the repo (for example `/tmp/x3-cursors/`). Bibata supplies Windows `.cur`/`.ani` directly; rasterize game-icons.net SVGs with an outline/shadow treatment that keeps contrast on terrain. Produce each candidate at 32, 48, and 64 px with hotspots matching the original semantics, and preserve the original animation timing for animated cursors.
5. Load-check every candidate through `CursorFile.FromFileSystemEntry` (scratch test or script, not committed). Record any parse failure, ignored size, or unsupported chunk.
6. Produce inspection artifacts in `/mnt/pit2/Network Storage/temp/`:
   - `x3-cursor-contact-sheet.png`: one row per subset cursor, columns for the original at 1x, the original at 2x through the current ImageSharp resize path, and the candidate at 32/48/64 px. Each cell is drawn over both a light and a dark terrain crop, with the hotspot marked and every row labeled.
   - `x3-cursor-animated.gif`: the animated subset, original next to candidate, at recorded frame rates.
   - Per-cell PNGs under `frames/x3-cursors/`.
   Hardware cursors do not appear in framebuffer captures, so compose the sheet directly from the decoded frames.
7. Assess the loader. Determine whether the current path can show 48/64 px candidates sharply at `CursorScale` 1.0 and 2.0, or whether it needs multi-size frame selection, DPI-aware size choice, or a PNG sprite-sheet format. Record the smallest change that would work. Scope any opt-in so the original set stays the default, aligned with the Track G `Faithful` preset.
8. Record the inventory, sources, licenses, artifact paths, loader assessment, and reproduction commands in the [X3 plan](../../implementation/track-x-x3-cursor-modernization.md). Draft follow-up cards (`Draft`) for each evidenced change, without readying them.

## Files likely to change

- `docs/roadmap/implementation/track-x-x3-cursor-modernization.md`.
- This card.
- Follow-up `Draft` cards under `docs/roadmap/agent/cards/`, with matching checklist, tree, and progress entries.

## Acceptance criteria

- [ ] The inventory covers all 52 installed cursors with role, size, frames, hotspot, and rate.
- [ ] Every candidate source has a pinned version or URL, license, and attribution requirement, plus a ship-in-repo or user-installed verdict.
- [ ] The contact sheet and animated GIF exist in the shared directory, cover the full subset at all sizes, and show hotspots over light and dark backgrounds.
- [ ] Every candidate has a `CursorFile` load-check result.
- [ ] The loader assessment states whether sharp high-DPI cursors need a code change, and names the smallest one with exact files.
- [ ] Follow-up cards are `Draft` and keep the original cursors as the default.

## Validation commands

Read-only code, archive, and asset inspection. Record the artifact-generation commands and tool versions in the X3 plan. Validate Markdown links and run `git diff --check`.

## Out of scope / guardrails

- No runtime source edits.
- No committed third-party or EA cursor assets.
- Do not write into the Generals install directory. An in-game trial with loose `Data/Cursors/` overrides is an operator step: record the command, do not perform it.
- Do not replace the full set. Stop at the example subset.
- Do not change the default cursor behavior or `CursorScale`.

## On completion

- Set Status to `Done` once the artifacts and plan entries are recorded. Otherwise set it to `Blocked` and state the missing source or license evidence.
- Tick X3-1 in the checklist, update X3 progress, and append a result line to `.exchange`.
- Follow-up cards: none until the evidence names a change.
