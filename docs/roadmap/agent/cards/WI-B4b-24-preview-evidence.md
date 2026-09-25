# Task Card `WI-B4b-24`: Dev preview CLI verb and play evidence

| Field | Value |
|---|---|
| Card ID | `WI-B4b-24` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-6b dev preview |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-B4b-20` and `WI-B4b-23` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Add a `preview` verb and a game-backed test proving a generated map loads and runs.

## Context to read (and only this)

- `src/Panopticon.Tools.GeoImport/`.
- `docs/screenshots.md`.
- An existing `[GameFact(SageGame.CncGenerals)]` test that starts a skirmish, for the fixture pattern.

## Steps

1. Add `preview --payload <dir> --textures <map.json> [--crop cx0,cy0,cx1,cy1] --out <maps dir>` writing the `.map`.
2. Add a `[GameFact(SageGame.CncGenerals)]` test that builds the synthetic payload, writes the preview map, starts a skirmish on it, runs 300 logic frames, and asserts terrain height at a control point.
3. Capture one screenshot of the generated map per `docs/screenshots.md` and attach it to the PR.

## Files likely to change

- `src/Panopticon.Tools.GeoImport/`
- `test/Panopticon.Simulation.Tests/` or the preview test folder

## Acceptance criteria

- [ ] `preview` writes a `.map` the engine loads.
- [ ] The game-backed test passes locally with `CNC_GENERALS_PATH` set.
- [ ] A screenshot shows the generated terrain.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneratedMapPreview"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No operator real-world proof (B4b-9).

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
