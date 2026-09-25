# Task Card `WI-G4-01`: Inventory every sample-count-bearing description

| Field | Value |
|---|---|
| Card ID | `WI-G4-01` |
| Roadmap phase | [Track G · G4](../../track-g-rendering-clarity.md): Multisample antialiasing |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: G4 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

Produce the complete, counted list of every description that must carry a sample count for MSAA, and
every place a multisampled target must be resolved before it is sampled or presented. Read-only: no
code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:23-25,92-100,160-168`:
  `GameOutputDescription`, the intermediate colour and depth targets, and the final
  `TextureCopier` blit.
- Every `GraphicsPipelineDescription` construction in `src/`. Find them with a repository search;
  there are nine, one of which belongs to the ImGui renderer rather than the game.
- `src/Panopticon.Simulation/Graphics/Shaders/`: the shader resource classes owning those descriptions.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowMapRenderer.cs`: the shadow depth pass, which
  must be considered separately from the main colour pass.

## Steps

1. List every `GraphicsPipelineDescription` construction: file:line, the owning class, and the pass
   it belongs to. State the total count.
2. List every `TextureDescription` and `FramebufferDescription` construction that participates in
   the main scene pass, and record whether its sample count is currently fixed at one.
3. Record every place the intermediate colour texture is sampled or copied. Each is a point where an
   MSAA resolve must occur first, since a multisampled texture cannot be sampled directly.
4. Record which passes must not become multisampled: the shadow depth pass, any offscreen texture
   used as data rather than as an image, and the 2D pass if it composites onto an already-resolved
   target.
5. Record whether `GameOutputDescription` is shared between passes with different sample-count needs.
   Its consumers are listed by a search for the symbol.
6. Write the inventory into a new `docs/roadmap/implementation/track-g-g4-msaa.md`, following the
   format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g4-msaa.md` (new)

## Acceptance criteria

- [ ] Every `GraphicsPipelineDescription` construction in `src/` appears with its owning class and
      pass, and the total is stated.
- [ ] Every sample point of the intermediate colour texture is listed as a required resolve site.
- [ ] Passes that must stay single-sampled are named with the reason.
- [ ] Whether `GameOutputDescription` is shared across differing needs is answered definitively.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not thread a sample count through anything. That is `WI-G4-02`.
- Do not evaluate alpha-to-coverage. That is G5, which depends on this phase.
- Do not judge performance cost; this card is a structural inventory only.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the pipeline-description count.
- Follow-up cards to create:
  - None; `WI-G4-02` already exists and depends on this inventory.
