# Task Card `WI-H2a-14`: Validate Generals behavior parity

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-14` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-H2a-13` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Prove the Generals reference adapter preserves authored values and existing gameplay contracts.

## Context to read (and only this)

- H2a deep dive: reviewed roster and compatibility assertions.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md`, `p9-unit-validation-ground.md`.
- `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs`, `ValidationGroundRunner.cs`.
- Card 13's adapter tests and the existing P6b/P9 test files named by card 02.

## Steps

1. Add authored-field assertions for the six reference archetypes and selected abilities/production/upgrades.
2. Exercise inherited defaults and scoped overrides in the existing P0/P9 host.
3. Record actual install prerequisites and behavioral evidence in the H2a deep dive.

## Files likely to change

- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/GeneralsUnitParityTests.cs`.
- Existing P6b/P9 test fixtures named by card 02.
- `docs/roadmap/deep-dives/h2a-unit-content-import.md`: evidence.

## Acceptance criteria

- [ ] Produce/move/attack/weapon modes/selected abilities/die contracts retain existing expected behavior.
- [ ] Relevant production/upgrades, defaults, and overrides retain authored semantics.
- [ ] Validation uses behavioral invariants and values, not new same-seed identity requirements.
- [ ] Standard validation and the explicit real-install proof pass; missing assets are reported as unmet prerequisites.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~GeneralsUnitParityTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

Do not rewrite expected behavior or regenerate golden baselines to mask a mismatch. Preserve artifacts and stop on suspected determinism deviations under repository policy.

## On completion

Update Status and H2a-14, reconcile card 17, and append proof results to `.exchange`.
