# Task Card `WI-C1-02`: Audit the W3D animation import path

| Field | Value |
|---|---|
| Card ID | `WI-C1-02` |
| Roadmap phase | [Track C · C1: Format-agnostic import abstraction](../../track-c-external-assets.md) |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C1-01` |
| Diagrams | This card completes the current-path evidence needed to ground the C1 architecture diagrams. |

## Goal

Produce the authoritative trace from an INI animation reference to runtime bone updates, including
uncompressed and compressed W3D conversion. Identify the format-specific assumptions C1 must
isolate while preserving the gameplay animation-state contract. Read-only: no source changes.

## Context to read (and only this)

- `docs/roadmap/implementation/track-c-c1-import-abstraction.md`: the model-path audit and C1
  boundary findings from `WI-C1-01`.
- `src/Panopticon.Simulation/Data/Ini/IniParser.cs:716-727`: animation-reference parsing and lazy lookup.
- `src/Panopticon.Simulation/Content/AssetStore.cs:137-140,311-314`: animation collection and loader
  registration.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandAnimationLoader.cs`: animation key parsing, path
  resolution, W3D parsing, and name validation.
- `src/Panopticon.Simulation/Graphics/Animation/W3DAnimation.cs`: W3D animation-chunk dispatch and the
  runtime asset surface.
- `src/Panopticon.Simulation/Graphics/Animation/W3DAnimation.W3dAnimation.cs`: uncompressed channel
  conversion.
- `src/Panopticon.Simulation/Graphics/Animation/W3DAnimation.W3dCompressedAnimation.cs`: time-coded,
  adaptive-delta, and motion-channel conversion.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationClip.cs`: the runtime clip shape.
- `src/Panopticon.Simulation/Graphics/Animation/AnimationInstance.cs:13-55,160-335`: runtime playback,
  interpolation, and bone updates.
- `src/Panopticon.Simulation/Logic/Object/AnimationState.cs:90-125`: INI animation block fields.
- `src/Panopticon.Simulation/Logic/Object/Draw/W3dModelDraw.cs:210-240,365-390`: gameplay animation-state
  selection and attachment to a `ModelInstance`.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs:81-136,160-235`: hierarchy allocation, animation
  updates, and skinning transforms.

## Steps

1. Trace an INI `AnimationName` from `IniParser.ParseAnimationReference()` through the lazy
   `AssetStore.ModelAnimations` lookup, `OnDemandAnimationLoader`, `W3dFile` parsing,
   `W3DAnimation`, and `AnimationInstance`.
2. Record each stage in a table with: input type, output type, owning method, file:line, whether it
   is format-specific, and failure behavior.
3. Cover all supported W3D representations: uncompressed transform channels, bit/visibility
   channels, time-coded channels, adaptive-delta channels, and motion channels. Record unsupported
   or explicitly ignored channel behavior.
4. Document the current identity and compatibility contracts: `hierarchy.animation` key splitting,
   animation name validation, bone index addressing, clip types, frame-rate-to-time conversion,
   `ModelBoneInstance` count/order, animation modes/flags, and visibility updates.
5. Separate the import-time responsibilities from runtime playback and gameplay-state selection.
   Identify which existing runtime types can remain importer-neutral without renaming them and
   which W3D-named types expose a format assumption.
6. Add a `## Current animation import path` section below the `---` divider in
   `docs/roadmap/implementation/track-c-c1-import-abstraction.md`. Include the trace,
   compatibility contracts, format-specific boundary findings, and unresolved ambiguities.
7. Add a `## Evidence required for C1 design` checklist naming the decisions the later C1
   deep-dive must make: interchange ownership, importer selection, model/animation contract split,
   CPU-to-GPU boundary, error contract, and W3D parity tests. Do not resolve those decisions.
8. Mark anything not determinable from the listed context as `ambiguous` with a one-line reason.

## Files likely to change

- `docs/roadmap/implementation/track-c-c1-import-abstraction.md`

## Acceptance criteria

- [ ] The trace connects the INI reference to runtime bone transforms without skipping lazy asset
      lookup or gameplay animation-state selection.
- [ ] Every supported W3D animation representation in step 3 is classified.
- [ ] Naming, bone-index, timing, mode, flag, and visibility compatibility contracts are explicit.
- [ ] Import-time responsibilities are clearly separated from runtime playback responsibilities.
- [ ] The C1 design-evidence checklist covers all six decisions in step 7.
- [ ] No `.cs` file is modified.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not design or implement `IAnimationImporter`, interchange DTOs, retargeting, or bone-name
  remapping.
- Do not rename `W3DAnimation` or change the INI schema.
- Do not add glTF support, material mapping, or preview tooling.
- Do not treat C2 retargeting as part of the importer seam; only record the compatibility contract
  C2 will consume.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Do not change the C1 phase Status; the phase has not started implementation.
- Append a result line to repo-root `.exchange`, including the count of `ambiguous` findings.
- Follow-up cards to create:
  - `WI-C1-03`: write the C1 deep dive, contract design, and grounded architecture diagrams from
    both audits.
