# Task Card `WI-G8-02`: Four-cascade shadow tier with slope-scaled and normal-offset bias

| Field | Value |
|---|---|
| Card ID | `WI-G8-02` |
| Roadmap phase | [Track G · G8](../../track-g-rendering-clarity.md): Shadow quality tier |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G8-01`, `WI-G3-03`, `WI-G2-03`, plus a G8 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the G8 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Remove shadow acne speckle and cascade blockiness under `Enhanced` by defaulting to four cascades at
a higher map resolution and replacing the constant depth bias with a slope-scaled and normal-offset
bias, while `Faithful` keeps the current single-cascade result.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g8-shadow-quality.md`: the `WI-G8-01` findings, which fix the
  exact plumbing endpoints and single-cascade assumptions to change.
- The G8 deep dive and its architecture diagrams.
- `src/OpenSage.Game/Graphics/Rendering/Shadows/ShadowSettings.cs:9-83`.
- `src/OpenSage.Game/Assets/Shaders/Shadows.h:55-70`.
- The G2 settings shadow-tier member.

## Steps

1. Source the cascade count and map size from the G2 shadow tier rather than the `ShadowSettings`
   defaults, keeping `Faithful` on one cascade at the current size and `Enhanced` on four cascades at
   the higher size.
2. Fix the single-cascade assumptions the audit listed, so the four-cascade path is exercised rather
   than silently falling back.
3. Replace the constant bias subtraction with a slope-scaled bias computed at the shader stage the
   audit named, scaled by the angle between the surface normal and the light direction.
4. Apply the normal offset if the audit found it is carried but unused, offsetting the sample
   position along the surface normal before the depth comparison.
5. Keep the cascade split depths from the existing settings rather than retuning them, so a visible
   transition is attributable to the bias change and not to a split change.
6. Validate with the G3 harness at the default camera zoom on a scene with both flat ground and
   sloped terrain, under both presets.

## Files likely to change

- `src/OpenSage.Game/Graphics/Rendering/Shadows/ShadowSettings.cs`
- `src/OpenSage.Game/Graphics/Rendering/Shadows/ShadowMapRenderer.cs`
- `src/OpenSage.Game/Assets/Shaders/Shadows.h`
- `src/OpenSage.Game/Graphics/Shaders/GlobalShaderResources.cs`

## Acceptance criteria

- [ ] Shadow edges under `Enhanced` show no acne speckle or visible cascade blockiness at the default
      camera zoom, with cascade transitions not visible in the captured frame, satisfying the G8
      milestone.
- [ ] A `Faithful` capture is unchanged against its golden.
- [ ] The four-cascade path renders all four cascades rather than falling back to one.
- [ ] Sloped surfaces show no peter-panning introduced by the normal offset.
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
- If the shadow map texture cannot be reallocated at the higher size and layer count without
  restructuring its ownership, stop and mark `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G8 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G8 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
