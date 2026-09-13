# Task Card `WI-C1-03`: Write the C1 deep dive and contract design

| Field | Value |
|---|---|
| Card ID | `WI-C1-03` |
| Roadmap phase | [Track C · C1: Format-agnostic import abstraction](../../track-c-external-assets.md) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C1-01`, `WI-C1-02` |
| Diagrams | This card's deliverable is the C1 deep dive with populated architecture diagrams (class + sequence), grounded in both audits. |

## Goal

Write `docs/roadmap/deep-dives/c1-import-abstraction.md`: the C1 contract design (interchange
types, `IModelImporter` / `IAnimationImporter`, loader wiring, error contract, W3D parity tests)
with class and sequence diagrams grounded in the audited current path. Design only: no source
changes.

## Context to read (and only this)

- `docs/roadmap/implementation/track-c-c1-import-abstraction.md`: both audits, the compatibility
  contracts, and the `Evidence required for C1 design` checklist this card must resolve.
- `docs/roadmap/track-c-external-assets.md`: the C1 scope and milestone.
- `docs/roadmap/seams.md`: the model/animation import seam row this design instantiates.
- `docs/roadmap/deep-dives/a1-headless-host.md`: section referencing
  `OnDemandAssetLoadStrategy` data-only loading the seam must preserve.
- `docs/roadmap/agent/task-card-template.md`: for the follow-up implementation cards.

## Steps

1. Resolve every item in the `Evidence required for C1 design` checklist: interchange ownership,
   importer selection, model/animation contract split, CPU-to-GPU boundary, error contract, and
   W3D parity tests. Record each as a decision with rationale.
2. Define the interchange types for meshes, skeletons, animations, and materials, and the
   `IModelImporter` / `IAnimationImporter` interfaces, without committing exact signatures beyond
   what the audits force.
3. Map every W3D-specific assumption listed in both audits to either an interchange-type
   responsibility or a W3D-implementation detail.
4. Write the class diagram (importer interfaces, interchange types, runtime `Model` /
   `ModelBoneHierarchy` / `ModelMesh` / animation types) and the sequence diagram (INI reference
   to runtime model/animation through the seam), grounded in the cited file:line evidence.
5. Add the deep dive to `docs/roadmap/deep-dives/`, link it from the C1 phase resources row in
   `docs/roadmap/track-c-external-assets.md`, and add it to the README Deep dives table.
6. Decompose implementation into follow-up cards (interchange types, importer interfaces, W3D
   refactor, parity tests) and list them under On completion.
7. Mark any decision the audits cannot ground as an open question in
   `docs/roadmap/open-questions.md` instead of guessing.

## Files likely to change

- `docs/roadmap/deep-dives/c1-import-abstraction.md` (new)
- `docs/roadmap/track-c-external-assets.md` (phase resources row)
- `docs/roadmap/README.md` (deep dives table)
- `docs/roadmap/open-questions.md` (only if open decisions remain)
- `docs/roadmap/agent/cards/` (follow-up cards)

## Acceptance criteria

- [ ] Every checklist item in `Evidence required for C1 design` is resolved or filed as an open
      question with a stated reason.
- [ ] Every W3D-specific assumption from both audits is assigned to the seam or to the W3D
      implementation.
- [ ] The deep dive has a populated `Architecture Diagrams` section with one class diagram and one
      sequence diagram that both render.
- [ ] Follow-up implementation cards exist with `Ready` gating on the diagrams.
- [ ] No `.cs` file is modified.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
# From docs/roadmap/: confirm diagrams render:
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/c1-import-abstraction.md -o /tmp/c1.svg && rm -f /tmp/c1*.svg c1-*.svg
```

## Out of scope / guardrails

- Do not implement the interchange types, importers, or refactor any loader.
- Do not design C2 retargeting, C3 material translation, or C4 glTF support beyond naming the
  seam boundary each consumes.
- Do not change the INI schema or runtime type names.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Do not change the C1 phase Status; implementation has not started.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - interchange types + importer interfaces implementation card
  - W3D refactor behind the seam card
  - W3D parity tests card
