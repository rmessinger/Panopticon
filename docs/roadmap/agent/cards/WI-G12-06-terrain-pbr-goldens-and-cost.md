# Task Card `WI-G12-06`: Terrain PBR goldens and cost

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-06` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-G12-05` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Guard terrain PBR with goldens and record its rendering cost.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) section 4.
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`, `TerrainZoomProofRunner.cs`; `docs/screenshots.md`.
- B1 report format used by `RenderingScaleProbe`.

## Steps

1. Add the `enhanced-materials-pbr` capture variant for `terrain-default` and `terrain-grazing` with the synthetic pack's surface maps; record goldens; inject a flipped-normal regression and confirm failure.
2. Measure the terrain pass with materials off, colour only, and PBR on Alpine Assault at default zoom and grazing view; record medians and p95 in the G12 plan Results.
3. Operator evidence: real-pack PBR captures beside colour-only and original, plus a GIF of a light sweep or camera orbit showing specular and normal detail.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`
- `docs/roadmap/implementation/track-g-g12-open-material-packs.md` (results)

## Acceptance criteria

- [ ] PBR goldens recorded and the injected regression fails.
- [ ] Cost table recorded; PBR terrain pass overhead stated against colour only.
- [ ] Captures and GIF shared.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Shader optimization beyond recording cost; a regression above 1 ms median gets its own card.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick G12-3 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
