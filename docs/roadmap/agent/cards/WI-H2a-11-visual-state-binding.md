# Task Card `WI-H2a-11`: Bind imported visual states

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-11` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-08` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Connect unit visual-state definitions to Track C assets and native gameplay draw behavior.

## Context to read (and only this)

- H2a deep dive: visual-state contract and selected proof bindings.
- `src/Panopticon.Simulation/Content/Loaders/ImportedModelMaterializer.cs`, `ImportedAnimationMaterializer.cs`.
- `src/Panopticon.Simulation/Content/Importing/IModelImporter.cs`, `IAnimationImporter.cs`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`: existing NIF/KF asset loading.
- Exact native draw module-data paths identified by card 02.

## Steps

1. Materialize model and idle/move/attack/death bindings through existing importers and draw modules.
2. Resolve model/texture/animation keys against the correct source filesystem, separately from runtime content namespaces.
3. Test required missing states/assets and explicit optional presentation policy; keep logic loading GPU-independent.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitVisualMaterializer.cs`.
- Reviewed native draw-data construction seams.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitVisualMaterializerTests.cs`.

## Acceptance criteria

- [ ] Gameplay conditions select imported idle/move/attack/death animations through native draw behavior.
- [ ] Source roots cannot accidentally resolve textures or animations from a different game.
- [ ] Required missing bindings fail explicitly; headless logic does not require GPU upload.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No new NIF/KF feature implementation or UI panel. Optional effects do not gate the EE2 combat proof. Reconcile filenames before Ready.

## On completion

Update Status and H2a-11, reconcile card 12, and append validation to `.exchange`.
