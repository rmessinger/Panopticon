# Task Card `WI-G9-02`: Improve native-pixel 2D quality with proportional layout

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G9-02` |
| Roadmap phase | [Track G · G9](../../track-g-rendering-clarity.md): Resolution-independent 2D & UI |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-G9-01`, `WI-G1-04`, `WI-G2-03`, `WI-G3-04` |
| Diagrams | [G9 architecture](../../deep-dives/g9-resolution-independent-2d.md#5-architecture-diagrams) |

## Goal

Keep the control bar and HUD sharp on a scaled display by drawing the 2D layer at the framebuffer's
native pixel resolution with higher-quality upscaling, while layout and hit regions retain
G1's proportional pixel-space convention.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g9-resolution-independent-2d.md`: the `WI-G9-01` size trace
  and hit-region list, which is the exact change list.
- `docs/roadmap/implementation/track-g-g1-high-dpi.md`: the points-versus-pixels classification this
  card must not contradict.
- The G9 deep dive and its architecture diagrams.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:138-158`.
- `src/Panopticon.Simulation/Gui/DrawingContext2D.cs:12-60`.

## Steps

1. Preserve G1's pixel-sized 2D pass and proportional WND/APT layout; add no DPI conversion.
2. Rasterize live text at effective pixel size and snap borders in final pixel space. Cache fonts
   by family, style and pixel size, following the audit's exact draw sites.
3. Add Enhanced-only role-aware upscaling for identified legacy art, validated against fixed
   crops. Faithful and already-native images retain their existing sampling.
4. Keep hit regions in the same pixel space as converted mouse input. Retain original client
   rectangles and test equivalent control targeting at scales 1 and 2.
5. Validate with the G3 harness at a unity scale and at a non-unity scale, comparing the sharpness
   metric on the control bar region, and confirming the HUD occupies the same fraction of the frame
   at both scales.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs`
- `src/Panopticon.Simulation/Gui/DrawingContext2D.cs`
- `src/Panopticon.Simulation/Gui/Wnd/WndWindowManager.cs`
- `src/Panopticon.Simulation/Gui/Apt/AptWindowManager.cs`

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
- Do not re-author layout definitions or convert layout/input back to logical points.
- Do not add a second DpiScale transform; G1 already established pixel-space input and layout.
- If a draw callback mixes spaces, stop and record it in the audit before changing it.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- G9 remains incomplete until `WI-G9-03` finishes; update only this card's status.
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
