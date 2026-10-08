# Task Card `WI-G8-02`: Four-cascade shadow tier with slope-scaled bias

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G8-02` |
| Roadmap phase | [Track G · G8](../../track-g-rendering-clarity.md): Shadow quality tier |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G8-01`, `WI-G2-03`, `WI-G3-04` |
| Diagrams | [G8 architecture](../../deep-dives/g8-shadow-quality.md#5-architecture-diagrams) |

## Goal

Remove shadow acne speckle and cascade blockiness under `Enhanced` by mapping the G2 shadow tier to
four cascades at 2048x2048 and adding a receiver-side slope-scaled term to the constant depth bias,
while `Legacy` keeps the current single-cascade result bit for bit.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g8-shadow-quality.md` §3 and the architecture diagrams.
- `docs/roadmap/implementation/track-g-g8-shadow-quality.md`: the `WI-G8-01` findings.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs:9-78`.
- `src/Panopticon.Simulation/Graphics/Shaders/GlobalShaderResources.cs:122-196`: `ShadowConstantsPS`.
- `src/Panopticon.Simulation/Assets/Shaders/Shadows.h:11-25,52-70,200-308`.
- The G2 `GraphicsSettings.ShadowTier` member and its application point from `WI-G2-03`.

## Steps

1. Add `ShadowSettings.SlopeScaledBias` (default 0) and `ShadowSettings.ApplyTier(ShadowQualityTier)`:
   `Original` sets `OneCascade`, 1024, 0; `High` sets `FourCascades`, 2048 and the tuned slope value.
   No other member changes.
2. Replace the trailing `_padding` float in `ShadowConstantsPS` and `_Padding` in
   `ShadowConstantsPSType` with `SlopeScaledBias`, copied in `Set`. The struct stays 240 bytes.
3. In `ShadowVisibility`, set `cosTheta = clamp(nDotL, 0, 1)` and compute
   `slopeFactor = min(sqrt(1 - cosTheta * cosTheta) / max(cosTheta, 1e-4),
   MAX_SLOPE_TANGENT)` and thread it through `SampleShadowCascade` into
   `SampleShadowMapOptimizedPCF`, where `bias = Bias + SlopeScaledBias * slopeFactor *
   shadowMapSizeInv.x`. No branch on the tier.
4. Call `ApplyTier` from the G2 wiring on scene creation and on preset switch; reallocation is
   handled by the existing `ShadowMapRenderer` size and cascade check.
5. Tune `SlopeScaledBias` (start 2, range 0.5 to 4) and `MAX_SLOPE_TANGENT` (start 4) against the
   `shadow-casters` scene; record the chosen values in the implementation plan.
6. Add unit tests: `Marshal.SizeOf<ShadowConstantsPS>() == 240`; `ApplyTier(Original)` leaves a
   default `ShadowSettings` unchanged member by member; `ApplyTier(High)` yields the tier values.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs`
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowMapRenderer.cs`
- `src/Panopticon.Simulation/Assets/Shaders/Shadows.h`
- `src/Panopticon.Simulation/Graphics/Shaders/GlobalShaderResources.cs`

## Acceptance criteria

- [ ] Shadow edges under `Enhanced` show no acne speckle or visible cascade blockiness at the default
      camera zoom, with cascade transitions not visible in the captured frame, satisfying the G8
      milestone.
- [ ] A `Legacy` capture is unchanged against its golden.
- [ ] The four-cascade path renders all four cascades rather than falling back to one.
- [ ] Sloped surfaces show no detached (peter-panned) contact shadows.
- [ ] An `Enhanced` capture with `VisualizeCascades` on shows four cascade colours.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not retune the cascade split depths or the shadow distance.
- Do not replace the shadow filtering technique.
- Do not change lighting or colour space while here.
- Do not change the normal offset, the depth format, or add caster alpha testing.
- If the shadow map texture cannot be reallocated at the higher size and layer count without
  restructuring its ownership, stop and mark `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G8 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G8 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
