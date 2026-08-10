# Task Card `WI-G9-02`: Draw the 2D layer at native resolution with point-anchored layout

| Field | Value |
|---|---|
| Card ID | `WI-G9-02` |
| Roadmap phase | [Track G · G9](../../track-g-rendering-clarity.md): Resolution-independent 2D & UI |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G9-01`, `WI-G1-04`, `WI-G3-03`, plus a G9 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the G9 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Keep the control bar and HUD sharp on a scaled display by drawing the 2D layer at the framebuffer's
native pixel resolution with higher-quality upscaling, while layout and hit regions stay anchored in
the logical points G1 established.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g9-resolution-independent-2d.md`: the `WI-G9-01` size trace
  and hit-region list, which is the exact change list.
- `docs/roadmap/implementation/track-g-g1-high-dpi.md`: the points-versus-pixels classification this
  card must not contradict.
- The G9 deep dive and its architecture diagrams.
- `src/OpenSage.Game/Graphics/Rendering/RenderPipeline.cs:138-158`.
- `src/OpenSage.Game/Gui/DrawingContext2D.cs:12-60`.

## Steps

1. Pass the pixel-space output size to the 2D pass, so the draw resolution follows the framebuffer
   rather than the logical window size.
2. Keep every window manager's layout input in logical points, per the G1 classification, and apply
   the scale at draw time rather than at layout time.
3. Replace the 2D sampler with a higher-quality upscaling filter for the paths the audit identified
   as scaled art. Leave paths that already draw at native size on the existing sampler.
4. Leave every hit-region computation the audit listed in logical points, so input behaviour is
   unchanged.
5. Validate with the G3 harness at a unity scale and at a non-unity scale, comparing the sharpness
   metric on the control bar region, and confirming the HUD occupies the same fraction of the frame
   at both scales.

## Files likely to change

- `src/OpenSage.Game/Graphics/Rendering/RenderPipeline.cs`
- `src/OpenSage.Game/Gui/DrawingContext2D.cs`
- `src/OpenSage.Game/Gui/Wnd/WndWindowManager.cs`
- `src/OpenSage.Game/Gui/Apt/AptWindowManager.cs`

## Acceptance criteria

- [ ] The control bar and HUD text stay sharp at native resolution on a scaled display, satisfying
      the G9 milestone.
- [ ] Layout and hit regions are unchanged relative to the pre-card build, verified by the G1-04
      picking assertions still passing.
- [ ] The HUD occupies the same fraction of the frame at scale 1 and scale 2.
- [ ] A `Faithful` capture at unity scale is unchanged against its golden.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change the WND or APT art assets or their authored resolution.
- Do not re-author layout definitions; the change is where the scale is applied, not the layout data.
- Do not move any hit region into pixel space; that would undo G1.
- If the 2D pass targets a surface whose size cannot be decoupled from the layout size, stop and mark
  `Blocked`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G9 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G9 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
