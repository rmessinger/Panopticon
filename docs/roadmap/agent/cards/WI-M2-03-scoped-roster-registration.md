# Task Card `WI-M2-03`: Scoped EE2 roster registration

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M2-03` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-M2-01`, `WI-M2-02` |
| Diagrams | [M2 import pipeline](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Publish the authored EE2 reference roster beside Generals through the existing scoped publisher and Track C visual installer.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`: reference manifest and policy.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-mixed-source-ee2.json`.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitContentPublisher.cs`, `ImportedVisualInstaller.cs`.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`: publication tests.

## Steps

1. Reconcile the completed roster manifest and mark `Ready`. Add a committed roster import profile with exact unit IDs, epochs, regional bindings, conversions, and explicit exclusions.
2. Reuse `UnitContentImportRunner` and the existing adapter/resolution/materialization/publication flow. Add only the profile support needed for the manifest; no second import pipeline.
3. Verify model, animation, and texture installation for every reference entry. Errors reject publication rather than leaving partially registered content.
4. Test source-qualified names and Generals collision protection. Capture original native definition identities before import and assert they are unchanged after successful and failed imports.
5. Add explicit diagnostics for missing install, missing epoch/model rows, unsupported required gameplay, and conflicting names. Verify the original bounded Maceman profile still works.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/` (reference roster profile)
- `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/` (only required profile support)
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`
- `test/Panopticon.Tools.ReplaySketch.Tests/` (existing content-import integration tests)

## Acceptance criteria

- [x] One authored manifest publishes every reference entry through `UnitContentPublisher`. Six of seven: the infantry entry (`HeavyInfantry1UpgradeEpoch15`) is blocked on the Track C animation import guards (see the M2 implementation plan) and stays out of the manifest; including it fails verification and publishes nothing, which `Ee2RosterPublicationTests.BlockedInfantryRowFailsExplicitlyAndPublishesNothing` asserts.
- [x] Imported definitions and visual names are source-qualified; no Generals entry is overridden.
- [x] Failure does not leave partial roster registration or mutate native definitions.
- [x] Every model and required animation resolves through Track C installation. For the six published entries; the blocked infantry entry is covered by the note above.
- [x] Missing prerequisite and unsupported gameplay produce explicit failures; standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~UnitContentImport|FullyQualifiedName~Ee2"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No globally registered aliases, asset extraction commits, import fallback, or balance tuning.
- A publication gap blocks this card; bypassing scoped publication is not a workaround.

## On completion

- Mark `Done`, tick M2-3, append evidence to `.exchange`, and reconcile WI-M2-04.
