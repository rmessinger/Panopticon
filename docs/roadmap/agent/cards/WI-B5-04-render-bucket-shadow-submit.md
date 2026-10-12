# Task Card `WI-B5-04`: Render-bucket/shadow submit improvements

| Field | Value |
|---|---|
| Card ID | `WI-B5-04` |
| Roadmap phase | [Track B · B5](../../track-b-scale.md): B5-4 submit/shadow |
| Status | `Blocked` (flips to `Ready` when `WI-B5-03` is `Done`) |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-B5-03` |
| Diagrams | [B5 diagrams §6](../../deep-dives/b5-rendering-at-scale.md#6-architecture-diagrams) |

## Goal

Remove per-item submit overhead that does not depend on item state: index-buffer binds per
draw and per-item debug-group recording, and verify the shadow path (cascades, G8 tier)
keeps its output and tests green.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b5-rendering-at-scale.md`: section 3.3.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`: `DoRenderPass`
  (SetIndexBuffer per item, `PushDebugGroup($"Render item: {name}")` per item,
  material-change detection), `CalculateWaterShaderMap`.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowMapRenderer.cs`,
  `ShadowSettings.cs`; `test/Panopticon.Simulation.Tests/Graphics/Rendering/Shadows/`.
- `src/Panopticon.Simulation/Graphics/Rendering/Instancing/RenderScaleOptions.cs` (add
  `DebugItemGroups` flag here).

## Steps

1. Hoist `SetIndexBuffer`: track the last bound index buffer across the pass (reset when
   the framebuffer/pipeline group changes where Veldrid requires it; index buffer binding
   persists across pipelines in Vulkan/D3D/Metal, so a per-pass hoist is safe) and bind only
   on change.
2. Gate per-item debug groups behind `RenderScaleOptions.Current.DebugItemGroups` (default
   off); keep pass-level groups (`Opaque`, `Transparent`, `Shadow pass`) unconditionally.
   Remove the per-item interpolated string when disabled (no allocation).
3. Statistics: count index-buffer binds and debug groups recorded per frame.
4. Verify the shadow path: run the probe with the Enhanced four-cascade tier and the
   Legacy single-cascade tier; shadow-mapped pixels unchanged (probe A/B before/after this
   card, byte-compare); `ShadowSettingsTests`, `ShadowFrustumCalculatorTests`, and the G8
   suites stay green.
5. Record submit-span deltas in deep dive §7 (final numbers by WI-B5-05).

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/RenderStatistics.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/Instancing/RenderScaleOptions.cs`
- `test/Panopticon.Simulation.Tests/Graphics/Rendering/` (bind-count test if feasible; at
  minimum statistics assertions)

## Acceptance criteria

- [ ] Index-buffer bind count per frame is at most distinct-index-buffer count per pass,
      not item count (statistics assertion on a probe run).
- [ ] With `DebugItemGroups` off (default), no per-item string allocation occurs in
      `DoRenderPass` and output pixels are byte-identical to before this card.
- [ ] Shadow-tier A/B (Original vs High) probe runs complete; shadow pixels byte-identical
      before/after this card.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet test src --filter 'Category!=Interactive'
dotnet test src --filter 'FullyQualifiedName~Shadow'
```

## Out of scope / guardrails

- No shadow algorithm changes: cascade count, sizes, biases, and `ShadowSettings` untouched.
- No reordering of draws within a bucket beyond what WI-B5-02/03 already introduced.
- Veldrid state-tracking assumptions (index buffer persists across pipeline changes) must
  hold on all backends the project targets; if a backend requires rebind, rebind per
  pipeline change, never per item.

## On completion

- Set Status to `Done`.
- Append a `.exchange` line.
- Follow-up cards: none new (WI-B5-05 next).
