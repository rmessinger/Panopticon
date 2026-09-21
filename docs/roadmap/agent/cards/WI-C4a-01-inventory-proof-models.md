# Task Card `WI-C4a-01`: Inventory the EE2 proof models

| Field | Value |
|---|---|
| Card ID | `WI-C4a-01` |
| Roadmap phase | [Track C · C4a](../../track-c-external-assets.md): Empire Earth II NIF 10.1 proof of concept |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `none` |
| Diagrams | This card grounds the [C4a architecture diagrams](../../deep-dives/ee2-gamebryo-model-storage.md#10-architecture-diagrams). |

## Goal

Record complete, reproducible descriptors for the two C4a target models so later parser cards have
fixed expected counts, references, transforms, material flags, and LOD behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 2, 3, 7, 9, and 10.
- Niftools
  [`nif.xml`](https://github.com/niftools/nifxml/blob/develop/nifgen/formats/nif/nif.xml):
  version 10.1 block layouts named by the target inventories.

## Steps

1. Read `EMPIRE_EARTH_II_PATH/zips/graphics.zip` without extracting persistent files.
2. Decode `cache/nifcache/nwg1_galley.nifcache` and
   `cache/nifcache/ab11_TwinEngineBomber.nifcache` with a temporary script kept outside the
   repository.
3. Record for each target: block order and references, node names and parentage, local transforms,
   including every scale value, shape vertex/triangle/UV/color/normal counts, bounds, property
   values, source texture names, root count, and any disconnected blocks.
4. For the Bomber, record every `NiLODNode` child and `NiRangeLODData` near/far pair. Identify the
   child selected by the minimum near range.
5. Confirm every target shape carries normals. Determine source handedness, up axis, front axis,
   and unit scale from transforms and geometry. Record the single source-to-engine matrix that
   C4a will bake into each shape.
6. Update the target table and import contract in the deep dive. If either target requires an
   active block outside the listed C4a subset, mark this card `Blocked` and name that block.

## Files likely to change

- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md`
- `docs/roadmap/agent/cards/WI-C4a-01-inventory-proof-models.md`

## Acceptance criteria

- [ ] Both target descriptors include every count and reference needed for deterministic tests.
- [ ] The source-to-engine transform, all node scales, normal presence, and Bomber highest-detail
      LOD choice are explicit.
- [ ] Every target block is either mapped to C4a behavior or reported as a blocker.
- [ ] No EE2 file or extracted byte range is added to the repository.

## Validation commands

```sh
test -f "$EMPIRE_EARTH_II_PATH/zips/graphics.zip"
git diff --check
```

## Out of scope / guardrails

- Do not modify source code.
- Do not commit the temporary parser, extracted assets, hashes, or binary data.
- Do not inspect KF animation, `.texcache`, `.pcpatch`, DDF, damage variants, or patch archives.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C4a-02` and `WI-C4a-08` to `Ready`.
- Append a result line to `.exchange`.
