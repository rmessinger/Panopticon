# Task Card `WI-B5-03`: Distance LOD gate and sort-once culling

| Field | Value |
|---|---|
| Card ID | `WI-B5-03` |
| Roadmap phase | [Track B · B5](../../track-b-scale.md): B5-3 LOD/culling |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-B5-02` |
| Diagrams | [B5 diagrams §6](../../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams) |

## Goal

Items whose projected screen size is negligible stop entering any render bucket (LOD
floor), and per-bucket sorting happens once per frame instead of once per pass.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md`: sections 3.2, 3.3, 6.
- `src/Panopticon.Simulation/Graphics/ModelMesh.cs`: `BuildRenderListWithWorldMatrix`
  entry, `BoundingBox`/`BoundingSphere`.
- `src/Panopticon.Simulation/Graphics/Cameras/Camera.cs`: `FieldOfView`, `ScreenSize`,
  `Position`, `GetVerticalFieldOfView`.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItemCollection.cs`: `CullAndSort`,
  call sites in `RenderPipeline.DoRenderPass` and water-map passes.
- `src/Panopticon.Simulation/Physics/PhysicsBackendPolicy.cs` (mutable-static-options
  pattern), new `Rendering/Instancing/RenderScaleOptions.cs` from `WI-B5-02`.

## Steps

1. Add the projected-radius gate: a static helper (camera, world-space bounding sphere)
   returning screen-radius pixels; `ModelMesh.BuildRenderListWithWorldMatrix` returns
   early below `RenderScaleOptions.Current.SmallFeatureCullingPx` (default 1.0 px) when
   `SmallFeatureCulling` is on (default on). Gate before any item add so Opaque, Shadow,
   and Transparent buckets all skip the mesh.
2. Split `CullAndSort` into `SortAll()` (once per frame per bucket, called from
   `RenderPipeline.Execute` after list build) and `FilterVisible(frustum, clip1, clip2,
   target)` (per pass; fills the visible index list preserving sorted order). Keep the
   public shape (`CulledItemIndices`) so `DoRenderPass` changes minimally.
3. Statistics: count gate-skipped meshes and per-pass filtered counts.
4. Tests: gate math unit test (sphere at known distance/fov → expected pixels; threshold
   boundary); CPU test that a bucket sorted once filters to the same visible set and order
   as the old per-pass sort for a synthetic item list (golden order comparison).
5. Probe evidence: rerun the probe at 10k with LOD on/off; item counts drop when the camera
   is angled so part of the field is far; record numbers in deep dive §7 (WI-B5-05 finalizes).

## Files likely to change

- `src/Panopticon.Simulation/Graphics/ModelMesh.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/RenderItemCollection.cs`,
  `RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/Instancing/RenderScaleOptions.cs`
  (or `Graphics/RenderScaleOptions.cs` if `WI-B5-02` placed it there)
- `test/Panopticon.Simulation.Tests/Graphics/Rendering/` (gate + filter tests)

## Acceptance criteria

- [x] With the gate on, a far-away model produces zero items in all buckets; a near model
      is unchanged (unit test with a synthetic camera).
- [x] Visible set and order after the split equal the old `CullAndSort` output for a
      synthetic bucket (order-sensitive assertion).
- [x] Per-frame sort count per bucket is 1 regardless of pass count (assert via statistics
      or by construction).
- [x] Standard validation passes.

## Result

Done 2026-10-13. `ModelLod.ProjectedRadiusPx` (world-scale-aware sphere projection,
`Camera.VerticalFieldOfView` now public) gates whole meshes below 1 px in
`ModelMesh.BuildRenderListWithWorldMatrix`; a degenerate viewport (the headless panel's 1x1
fallback before `EnsureFrame`) disables the gate, which also keeps headless GPU tests
(muzzle-flash visibility) rendering. `RenderItemCollection` splits `CullAndSort` into
`SortAll` (once per frame, from `RenderPipeline.Execute`) plus order-preserving
`FilterVisible` per pass; four-cascade shadows no longer re-sort per cascade. Evidence:
500-unit matched-frame captures byte-identical with the gate on and off (gate skipped
nothing at near framing) and, with the gate disabled, identical to the pre-card build
(sort-once is pixel-neutral in isolation); at the 10k framing the gate skips 30257
sub-pixel meshes (intended visible change), items 91515 to 61282, draws 176 to 155. The
probe now sizes its panel (1280x800) before warmup: measurements before this card ran with
the 1x1 fallback viewport, so WI-B5-05 re-runs the baseline-vs-after matrix with the sized
panel. Probe fix note: `FrameCamera` unchanged; `RenderScaleOptions.LodSkippedMeshes`
surfaces in `RenderStatistics.MeshesSkippedByLod`.

## Validation commands

```sh
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet test src --filter 'Category!=Interactive'
```

## Out of scope / guardrails

- No impostor rendering and no multi-LOD asset selection (recorded gaps in the deep dive).
- The gate must not alter near-field pixels: with default threshold, close-up captures are
  byte-identical (probe A/B with gate on/off on a framed-near camera).
- Do not cull the Transparent bucket by screen size differently than Opaque; one gate.

## On completion

- Set Status to `Done`.
- Append a `.exchange` line.
- Follow-up cards: none new (WI-B5-04 next).
