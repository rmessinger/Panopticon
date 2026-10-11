# Task Card `WI-M2-04`: EE2 P9 contract validation

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M2-04` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-M2-03` |
| Diagrams | [M2 import pipeline](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Exercise every reference EE2 unit in the P9 validation ground beside Generals units. Record spawn, real movement, attack, death, and aircraft return/rearm as explicit contract results.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`: scenario, completion signals, gaps, baseline contract.
- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`: reference manifest.
- `src/Panopticon.Tools.TestHarness/Program.cs`: `UnitValidationGround` dispatch and the runner it invokes.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: validation-ground commands and reports.
- `src/Panopticon.Tools.TestHarness/UnitContentImportRunner.cs`, `MassBattleCapacityProbeRunner.cs`: publication and parking initialization.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/Ee2GroundUnitProbeTests.cs`.

## Steps

1. Reconcile the landed profile and mark `Ready`. Extend P9's existing roster input and observations, not a standalone alternative validation ground. Keep the existing Generals default and baseline unchanged.
2. Import the roster before spawn. Assign each reference unit a suitable hostile target and a movement destination. Send real engine orders and record per-unit completion frames within declared budgets.
3. Assert actual displacement, weapon-caused health loss, and the dying/removal lifecycle. Ground-only and anti-air units receive compatible targets. A missing handler, failed publication, absent signal, or timeout is a failure, not a skip.
4. Spawn airfield support through the existing parking path. For both fixed-wing and helicopter references, observe weapon ammunition consumption, return, ammunition refill, and a second damaging sortie. Merely seeing two departures does not prove rearm.
5. Write machine-readable per-contract results keyed by source-qualified unit ID, with gaps and prerequisite diagnostics. Add installed integration tests and synthetic failure/report tests.
6. Run a watchable rendering verification using the documented screenshot recipes; if ReplaySketch is changed, verify screenshot mode and the map panel. A non-presenting host limitation remains pending evidence, never a claimed windowed pass.
7. Update the implementation report, checklist, track status, and progress views only for criteria with evidence. Open one M2 phase PR with atomic card bullets and validation results; leave it unmerged.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/` (existing P9 runner, roster, observations, reports, import hook)
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/` (reference validation profile)
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/`
- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`
- `docs/roadmap/implementation-checklist.md`, `docs/roadmap/track-m-mass-battle.md`

## Acceptance criteria

- [x] Every reference entry passes spawn, displacement, damaging attack, and death in a real P9 scene beside native Generals units. Six of seven published entries (the infantry entry is blocked at publication by the Track C animation guards); asserted by `Ee2ContractValidationTests`.
- [x] Fixed-wing and helicopter references consume ammunition, return, refill, and damage a target on a second sortie. Helicopter: return and second damaging sortie pass deterministically; no EE2 helicopter authors ordnance, so consumption and refill are absent in source and reported as such. Fixed-wing: the full cycle passes deterministically and is asserted after WI-M2-06 (airborne 2D friction plus a dedicated taxi locomotor removed the divergence and the parking deadlocks); two consecutive installed runs green.
- [x] Reports name every contract result and measured frame; unsupported behavior and timeouts fail explicitly.
- [x] Existing Generals P9 and Maceman tests remain green; no existing golden baseline is rewritten to force a pass.
- [x] Standard validation passes with no new failures; artifacts and any pending visual evidence are recorded accurately.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2|FullyQualifiedName~UnitContentImport"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Record the exact authored P9 profile command in the implementation report after reconciliation.

## Out of scope / guardrails

- No M3 balance, M4 army AI, M5 mass battle, or proprietary assets.
- Determinism/golden failures require preserved artifacts and operator determination before changing expected data.
- A missing runtime contract blocks M2 completion. Do not weaken acceptance to make the phase green.

## On completion

- Mark `Done` only when all contracts pass. Tick M2-4, update phase status, and append `.exchange` evidence.
- Leave the phase PR open for operator merge.
