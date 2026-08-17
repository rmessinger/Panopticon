# Task Card `WI-G9-01`: Audit the 2D and UI resolution and sampling path

| Field | Value |
|---|---|
| Card ID | `WI-G9-01` |
| Roadmap phase | [Track G · G9](../../track-g-rendering-clarity.md): Resolution-independent 2D & UI |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: G9 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

Record the full path by which 2D art reaches the frame, from the size the window managers are given
through to the sampler used for the final draw, so the softening can be attributed to a specific
stage before any change is made. Read-only: no code changes.

## Context to read (and only this)

- `src/OpenSage.Game/Graphics/Rendering/RenderPipeline.cs:138-158`: the 2D pass, including the size
  and sampler passed to `DrawingContext2D.Begin`.
- `src/OpenSage.Game/Gui/DrawingContext2D.cs:12-60`: the constructor, its `SpriteBatch`, and `Begin`.
- `src/OpenSage.Game/Gui/Wnd/WndWindowManager.cs`: where it receives its size and how it lays out.
- `src/OpenSage.Game/Gui/Apt/AptWindowManager.cs`: the same, plus its size-dependent resource
  creation.
- `src/OpenSage.Game/GamePanel.cs` and `src/OpenSage.Game/GameWindow.cs:37-58`: `ClientBounds`,
  `WindowScale`, and `CursorScale`, which are the sizes those managers consume.
- `docs/roadmap/implementation/track-g-g1-high-dpi.md`: the G1 consumer classification, since G9's
  layout must stay in logical points while its drawing moves to pixels.

## Steps

1. Trace the size value from the window through the panel to each window manager, recording each
   transformation and whether the value is in logical points or physical pixels at each hop.
2. Record the sampler used for the 2D draw and where it comes from, naming it exactly as it appears
   in the code.
3. Record the native authored resolution of the WND and APT art and how it is scaled to the
   framebuffer: whether by a layout transform, a sampler-level scale, or an intermediate target.
4. Record every place a hit region or input coordinate is computed for the 2D layer, since those must
   stay in logical points when drawing moves to native resolution.
5. Record whether the 2D pass draws into the intermediate target or directly into the swapchain
   target, since that decides whether 2D would be affected by G4's MSAA and G6's output encode.
6. Write the findings into a new `docs/roadmap/implementation/track-g-g9-resolution-independent-2d.md`,
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g9-resolution-independent-2d.md` (new)

## Acceptance criteria

- [ ] The size trace records the space, points or pixels, at every hop.
- [ ] The 2D sampler is named exactly as it appears in the code, with its source.
- [ ] Every 2D hit-region computation site is listed.
- [ ] Whether the 2D pass targets the intermediate or the swapchain is answered definitively.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change the 2D path. That is `WI-G9-02`.
- Do not redo the G1 classification; reference it.
- Do not evaluate replacing the WND or APT art assets. G9 changes how existing art is drawn.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G9-02` already exists and depends on these findings.
