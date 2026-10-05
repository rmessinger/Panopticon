# Task Card `WI-H2a-01`: Audit unit-content loading

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-01` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md): unit-content import layer |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Deliverable supplies evidence for `WI-H2a-02`; no source edits |

## Goal

Record the existing unit-loading path and its compatibility obligations with file:line evidence.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`.
- `src/Panopticon.Simulation/Content/ContentManager.cs`: INI loading entry points.
- `src/Panopticon.Simulation/Content/SubsystemLoader.cs`: object, weapon, armor, and locomotor loading.
- `src/Panopticon.Simulation/Data/Ini/IniParser.cs`: defaults and reference parsing.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs`: parse, child/reskin, modules, cloning.
- `src/Panopticon.Simulation/Content/AssetStore.cs`, `ScopedAssetCollection.cs`, `LazyAssetReference.cs`: identity and scope lifetime.
- `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs`, `ValidationGroundRunner.cs`.
- `docs/system-investigations/EE2_UNIT_VISUAL_PIPELINE.md`: source record relationships.

## Steps

1. Add an evidence table to the H2a deep dive for loading, defaults, inheritance, references, module creation, scopes, map overrides, and P9 spawning.
2. Trace one object plus its weapon/armor/locomotor dependencies; name directly required neighboring implementations in the table.
3. Identify the native module data that cannot be lost in a source-neutral representation. Record unknowns without designing a replacement.
4. List candidate Generals archetypes and one EE2 ground-combat candidate, separating known support from unverified visual/state support.

## Files likely to change

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`.
- This card and the matching checklist item.

## Acceptance criteria

- [ ] Each loading stage and compatibility obligation has a real file:line citation.
- [ ] Existing construction/mutation restrictions and scope teardown behavior are recorded.
- [ ] The design card has exact evidence paths and unresolved risks, not guessed APIs.

## Validation commands

Read-only code investigation. Check cited paths and line ranges against the checkout; no build required.

## Out of scope / guardrails

No source code changes, archive extraction into the repository, or full H1 storage audit. Follow only directly referenced neighbors needed for this bounded trace.

## On completion

- Set Status to Done or Blocked and update H2a-1 in the checklist.
- Reconcile `WI-H2a-02` with the evidence before promoting it to Ready.
- Append the result and remaining risks to `.exchange`.
