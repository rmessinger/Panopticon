# Task Card `WI-C10c-02`: Granny float-curve clips as C1 TRS channels

| Field | Value |
|---|---|
| Card ID | `WI-C10c-02` |
| Roadmap phase | [Track C · C10c](../../track-c-external-assets.md): AoE3 DE skinned animation |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C10b-01` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`GrannyAnimationImporter` (`IAnimationImporter`, id `GR2`) imports per-clip `.gr2` files with `DaK32fC32f` transform tracks as bone-name TRS channels.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 1 and 3.
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs`, `ImportedAnimation.cs`, `ImportedAnimationChannel.cs`, `ImportedAnimationTarget.cs` (`CreateBoneName`), `ImportedKeyframe.cs`.
- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` (exact-name binding).

## Steps

1. Read `Animations[0]` (duration, time step) and its `TrackGroups` transform tracks.
2. Decode `CurveDataHeader_DaK32fC32f` (degree, knots, controls) for Position, Orientation, and ScaleShear; evaluate the B-spline at the track group's sample rate and emit linear translation, rotation, and uniform scale keyframes; non-uniform scale-shear is recorded as a diagnostic and reduced to uniform.
3. Apply the `GrannyAxisSystem` basis change from the clip's `ArtToolInfo`.
4. Any other curve format fails with clip, track, and curve type name.
5. Interactive tests: `march_rifleman` and `volley_standing_fire` import; every target name exists on the musketeer skeleton.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Granny/GrannyAnimationImporter.cs`, `GrannyCurveDecoder.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/GrannyAnimationImportTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic curve tests match analytic B-spline values.
- [ ] Proof clips import with 0 missing bone targets on the musketeer skeleton.
- [ ] Unsupported curve codecs fail with context.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- Compressed curve codecs (C10d review).
- Text or morph tracks.
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10c-04` to `Ready` if `WI-C10c-01`, `WI-C10c-03`, and `WI-C10b-04` are done.
- Tick C10c in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
