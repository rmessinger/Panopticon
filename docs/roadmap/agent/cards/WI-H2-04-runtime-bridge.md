# Task Card `WI-H2-04`: Bridge the runtime load path to the interchange

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2-04` |
| Roadmap phase | [Track H · H2](../../track-h-storage-formats.md): interchange abstraction |
| Status | `Draft` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-H2-03` |
| Diagrams | none |

## Goal

Materialize resident terrain, masks, and placed objects from `MapInterchange` at the existing load path, selected through the reader seam, with parity against the legacy load path asserted by the existing suites. Grounding from the H1 audit: the game setup readers at `src/Panopticon.Simulation/Game.cs:797` and `:808`, and the consumers at `src/Panopticon.Simulation/Scene3D.cs:313-318`.

## Context to read (and only this)

- [H2 map interchange design](../../deep-dives/h1-h2-map-storage.md): Scope and ownership sequence, Units, frames, and quantities, and Validation and error behavior.
- The H1 audit consumer tables in [the deep dive](../../deep-dives/h1-h2-map-storage.md): `Game.cs:797, :808`, `Scene3D.cs:313-318`, `TerrainVisual.cs:42-51`, `PartitionCellManager.cs:92`.
- Repo guidance for the P0 assertion layer in `AGENTS.md`.

## Steps

1. Add the runtime bridge role from the deep dive sequence: materialize heights, mask flags, and placed objects from a validated `MapInterchange`.
2. Select the map source through the seam at the existing load path, feeding the same resident structures the consumers read today.
3. Assert parity against the legacy load path with `MapFileTests` plus the P0 assertion layer per repo guidance.
4. Confirm failed validation publishes no resident content.

## Files likely to change

- `src/Panopticon.Simulation/Game.cs` load path selection.
- `src/Panopticon.Simulation/Content/Importing/Maps/` runtime bridge.
- `test/Panopticon.Simulation.Tests` parity tests.

## Acceptance criteria

- [ ] The load path resolves a map source through the seam and materializes resident terrain, masks, and placed objects from `MapInterchange`.
- [ ] `TerrainVisual`, `PartitionCellManager`, and `Scene3D` consume the same resident structures as before the bridge.
- [ ] Interchange loading matches legacy loading for the suite maps per `MapFileTests` and the P0 assertion layer.
- [ ] Failed model validation publishes no resident content and reports typed diagnostics.

## Validation commands

```sh
dotnet build src/Panopticon.Simulation -v minimal
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~MapFile"
# P0 and interactive runs need CNC_GENERALS_PATH set; before PR run the MCP test run per AGENTS.md.
```

## Out of scope / guardrails

No chunk residency or paging (B4a owns it), no H3 serialization, no write path, and no WorldBuilder lifecycle changes (H6 owns those).

## On completion

- Set Status above to `Done` or `Blocked`.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
