# Task Card `WI-G9-03`: Replace the pre-rendered HUD with dynamic drawing

| Field | Value |
|---|---|
| Card ID | `WI-G9-03` |
| Roadmap phase | [Track G · G9](../../track-g-rendering-clarity.md): Resolution-independent 2D & UI, per the [hud-rework scope Decision](../../open-questions.md) |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `L` |
| Prerequisites | `WI-G9-02`, `WI-G2-02` (preset layer), `WI-G3-03` (golden frames); plus the G9 deep dive with populated architecture diagrams |
| Diagrams | Blocked on the G9 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Under the `Enhanced` preset, draw the control bar and HUD chrome from dynamic geometry and live
text instead of the pre-rendered 800x600 WND bitmaps, so the HUD is genuinely
resolution-independent rather than upscaled legacy art. `Faithful` keeps the original bitmaps
unchanged.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g9-resolution-independent-2d.md`: the `WI-G9-01` inventory
  of HUD draw sites and hit regions; this card replaces the art behind those sites, not the sites.
- `docs/roadmap/track-g-rendering-clarity.md` G9 row and the rendering-clarity scope Decision.
- `src/OpenSage.Game/Gui/Wnd/` (control bar window/layout, button states) and
  `src/OpenSage.Game/Gui/DrawingContext2D.cs` (the 2D draw path the dynamic chrome draws through).
- The WND control bar asset chain as loaded today (`.wnd` layouts plus window.big textures).

## Steps

1. Inventory the control bar's bitmap dependencies (panels, button up/over/down states, frames,
   pips) from the loaded WND data; record which are pre-rendered bitmaps versus already-drawn
   elements.
2. Define a dynamic-drawing equivalent per element: nine-slice or vector panels, procedural
   button states (fill, border, hover/press), live text where bitmaps embed glyphs.
3. Gate the switch on the G2 preset: `Enhanced` draws dynamic chrome, `Faithful` draws the
   original bitmaps. One switch point, no per-element conditionals scattered through the UI code.
4. Match hit regions and layout to `WI-G9-02`'s logical-point anchors exactly; the rework must
   not move or resize controls.
5. Add golden-frame captures under both presets at 1x and 4x native to the G3 set.

## Acceptance criteria

- [ ] Under `Enhanced`, a captured frame's control-bar region contains no HUD bitmap draws
      (verify via a draw-list audit or texture-use log).
- [ ] Captured frames are sharp at 4x native with no upscaled-art blur on HUD chrome.
- [ ] `Faithful` output is pixel-identical to the pre-rework capture.
- [ ] Hit regions and layout are unchanged from `WI-G9-02` anchors (input tests pass unchanged).
- [ ] G3 golden frames exist for both presets at 1x and 4x native and catch an injected chrome
      regression.
- [ ] `dotnet build src`, `dotnet test` (non-Interactive suites), `dotnet format src
      --verify-no-changes` all pass.

## Out of scope / guardrails

- No UX redesign: element positions, sizes, and behavior stay as the original control bar.
- No WND/APT framework replacement; the dynamic chrome draws through the existing 2D layer.
- No in-world UI (selection rings, health bars) beyond what the control bar already owns.
