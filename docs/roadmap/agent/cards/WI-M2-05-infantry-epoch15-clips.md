# Task Card `WI-M2-05`: Infantry epoch-15 clip import

| Field | Value |
|---|---|
| Card ID | `WI-M2-05` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-M2-04` (blocked follow-up recorded there) |
| Diagrams | none (policy constant change) |

## Audience

Agent.

## Goal

Accept the epoch-15 infantry animation controller flag pattern (`0x0025`) in the NIF animation
import policy so the reference infantry row (`HeavyInfantry1UpgradeEpoch15`, visual
`HeavyInfantry1_15`, four clips Idle/Walk/Attack1/Death) publishes its visual, removing the first
M2 blocker.

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`: Known Track C limitation paragraph
  (the recorded blocker and its evidence).
- `src/Panopticon.Simulation/Content/Importing/Nif/NifLegacyVersionPolicy.cs`: the flag policy.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationConverter.cs`
  `ValidateController` (~line 206): the guard site and its sibling guards.

## Files

- `src/Panopticon.Simulation/Content/Importing/Nif/NifLegacyVersionPolicy.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2InventoryTests.cs`
  (`ReferenceRosterMaterializesWithTypedModules` blocker assertions)

## Steps

1. Add `0x25` to the accepted controller flags in the modern branch of
   `NifLegacyVersionPolicy.IsAcceptedControllerFlags`; update `DescribeAcceptedFlags` text.
   `0x25` is `0x01 | 0x04 | 0x20`, the same three bit families as the accepted `0x2C` pattern
   (`0x04 | 0x08 | 0x20`) minus interpolation-type bits; the C4c survey measured it at 4728
   controllers corpus-wide. The flags are not consumed semantically; playback mode comes from
   the visual-state bindings.
2. Run the installed inventory test. If the four infantry clips trip other `ValidateController`
   guards (frequency, phase, chained controllers, target refs, keys outside duration), stop and
   report: those are separate Track C parse questions, not this card's scope.
3. Flip the `Ee2InventoryTests.ReferenceRosterMaterializesWithTypedModules` assertions from
   expecting the blocker message (`controller flags 0x0025`) to expecting the infantry clips
   present and the roster clean.

## Acceptance criteria

- [x] `ReferenceRosterMaterializesWithTypedModules` asserts all seven reference rows import with
      clips; no `0x0025` rejection remains.
  - The death clip also required skipping skeleton-superset targets (it addresses `Bone10`,
    which the epoch-15 model lacks; idle/walk/attack target only present bones). The skip
    mirrors the converter's existing property-controller skip; duplicate targets still throw.
- [x] The bounded maceman profile stays green.
  - `Ee2RealUnitImportTests` (the maceman closure) passes in the affected-filter run.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).
  - Affected filter 232/233: only the pre-existing `Ee2MorphImportTests` NIF parse-guard
    failure, identical on clean main.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Ee2InventoryTests"
```

## Out of scope / guardrails

- Epochs 9-14 legacy-header skinned clips stay excluded (C4i scope).
- No animation semantics change: flags gate import only.

## On completion

- Set Status to `Done`, append `.exchange`, and record the infantry row as publishable in the M2
  implementation doc (removing the Track C limitation paragraph).
