# Task Card `WI-G12-07`: Model texture replacement (investigation)

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G12-07` |
| Roadmap phase | [Track G · G12](../../track-g-rendering-clarity.md): Open material packs |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `M` |
| Prerequisites | `WI-G12-04` |
| Diagrams | [G12 architecture](../../deep-dives/g12-open-material-packs.md#5-architecture-diagrams) |

## Goal

Decide how open materials replace W3D model textures (painted metal, concrete, wood, fabric) and write the G12-4 implementation cards.

## Context to read (and only this)

- [G12 design](../../deep-dives/g12-open-material-packs.md) sections 2 and 3.5.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dMaterialConverter.cs`, `Graphics/ModelMesh.Imported.cs:93-213`.
- `src/Panopticon.Simulation/Content/Loaders/OnDemandTextureLoader.cs`.
- `src/Panopticon.Simulation/Graphics/Shaders/PbrMetallicRoughnessShaderResources.cs`.

## Questions to answer

1. How many distinct model textures do Generals and Zero Hour units and structures reference, and how do they cluster by surface (metal, concrete, wood, fabric, glass, decals, team-colour masks)?
2. How is house colour applied (texture alpha, separate pass, vertex colour), and what must a replacement preserve?
3. Do model textures tile or are they unique UV atlases? Atlases rule out direct material swaps and point to detail overlays instead.
4. Which is better: in-place colour replacement under `FixedFunction`, detail maps blended over the original atlas, or converting matched meshes to the C3 PBR family with original colour plus open normal and roughness detail?
5. Cost and golden impact for one infantry, one vehicle, and one structure.

## Steps

1. Extend the inventory with a model texture pass (names, sizes, users, alpha use, team-colour use).
2. Prototype the two leading options on the Crusader tank and the USA barracks on a throwaway branch.
3. Capture original and prototypes under versioned names.
4. Record answers and a recommendation in the G12 plan Results and add G12-4 cards.

## Files likely to change

- `docs/roadmap/implementation/track-g-g12-open-material-packs.md`, new G12-4 cards
- No source changes on `main`.

## Acceptance criteria

- [ ] All five questions answered with counts and captures.
- [ ] Recommendation and G12-4 cards written.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario TerrainMaterialInventory --headless
```

## Out of scope / guardrails

- Production code; imported (non-W3D) models already use C3 PBR.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick G12-4 investigation in the checklist and update the README progress row.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
