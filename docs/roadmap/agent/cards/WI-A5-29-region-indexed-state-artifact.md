# Task Card `WI-A5-29`: Add region-indexed state records

| Field | Value |
|---|---|
| Card ID | `WI-A5-29` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 bounded-memory replay |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A5-08`, `WI-A5-12` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Make snapshot and delta records addressable by stable domain region so replay can avoid decoding a
whole world frame when only a viewport subset is needed.

## Context to read (and only this)

- Region-record layout in the A5 implementation plan.
- Indexed frame reader from `WI-A5-08`.
- Domain topology keys from `WI-A5-12`.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs` and `RunRecording.cs`.

## Steps

1. Add region/chunk directories or offsets to the state-artifact format.
2. Write added, removed, and changed entities under stable domain region keys.
3. Support frame plus region queries while retaining whole-frame reconstruction.
4. Version the artifact and reject incompatible region schemas.
5. Add multi-region, moved-between-region, long-run, and bounded-decode tests.

## Files likely to change

- `src/Panopticon.Simulation/Recording/`
- Run manifest metadata
- Game and ReplaySketch recording tests

## Acceptance criteria

- [ ] A frame-region query decodes only requested regions plus required metadata.
- [ ] Entities moving between regions reconstruct exactly once.
- [ ] Whole-frame reconstruction remains correct.
- [ ] Decoded entity count can remain bounded independently of world size.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~RegionIndexedState"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No viewport culling, rendering, or compression redesign.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A5-24`.
