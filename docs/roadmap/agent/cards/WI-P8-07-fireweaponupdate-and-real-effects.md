# Task Card `WI-P8-07`: FireWeaponUpdate implementation and real observable-effect assertions

| Field | Value |
|---|---|
| Card ID | `WI-P8-07` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-7, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P6 combat/command](../../deep-dives/p4-p6-economy-production-combat.md#p6-combat-and-command-verification) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P6-02`, `WI-P6-04` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

Two related gaps: (1) `FireWeaponUpdate.Update()` remains a no-op stub, so `WI-P6-02`'s
`CombatCoverageTests` verify the damage terminal (`AttemptDamage`→`ActiveBody`→`Kill`/`OnDie`) but
never the weapon-fire path that is supposed to drive it; (2) the P6 scenario matrix
(`WI-P6-04`/`P6ScenarioMatrixTests.cs`) has 9 of its 15 cells (move/attack/special-power ×
faction) assert only that a handler is non-default and emit a "GAP …" message as a *passing*
assertion, rather than asserting the real observable effect the deep dive describes (unit moves,
target health decreases, ability effect observed). Implement the weapon-fire path and replace the
gap-message cells with real effect assertions.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:65-72` and the P6 scenario-matrix section: weapon-fire→damage design intent and the "expected observable effect" assertion the matrix is supposed to make.
- `src/OpenSage.Game/Logic/Object/Update/FireWeaponUpdate.cs`: the no-op stub to implement; trace what should invoke it (weapon cooldown/range checks) and what it should call into (`Weapon`/`WeaponTemplate`/`DamageModule`).
- `src/OpenSage.Game/Logic/Object/Weapon/Weapon.cs`, `WeaponTemplate.cs`: the weapon-fire model `FireWeaponUpdate` should drive.
- `src/OpenSage.Game.Tests/Logic/Object/CombatCoverageTests.cs`: existing damage-terminal tests (`WI-P6-02`); extend these once real weapon fire exists, without breaking the existing direct-`AttemptDamage` assertions.
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6ScenarioMatrixTests.cs:180-220` (or wherever the 9 gap-message cells live): the current "GAP" placeholder assertions to replace with real effect checks for move/attack/special-power cells.
- `docs/roadmap/agent/cards/WI-P6-02-combat-coverage.md` and `WI-P6-04-scenario-matrix.md`: prior cards; their Result sections document exactly what was skipped and why.

## Steps

1. Implement `FireWeaponUpdate.Update()` so it drives real weapon fire (cooldown/range/ammo as applicable) into the existing `DamageModule`/`ActiveBody` path already covered by `WI-P6-02`. Keep scope tight: this closes the specific disclosed gap, not a general weapons-system rewrite.
2. Add a headless test that fires a weapon through `FireWeaponUpdate` (not by calling `AttemptDamage` directly) and asserts the target's health decreases, extending `CombatCoverageTests.cs`.
3. In the P6 scenario matrix, replace the 9 gap-message cells (move/attack/special-power × faction) with real observable-effect assertions: unit position changes for `MoveTo`, target health decreases for `AttackObject` (now backed by step 1's real fire path), and a concrete ability effect for `SpecialPower` (or a narrower, explicitly-scoped effect if a given special power genuinely cannot be asserted headlessly; document exactly which and why, per cell).
4. Update `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md`'s P6 scenario-matrix notes once gap cells are closed, removing stale "GAP"/caveat language for cells that are fixed.
5. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/Object/Update/FireWeaponUpdate.cs`
- `src/OpenSage.Game.Tests/Logic/Object/CombatCoverageTests.cs`
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6ScenarioMatrixTests.cs`
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md`

## Acceptance criteria

- [ ] `FireWeaponUpdate.Update()` is no longer a no-op; it drives real weapon fire into the existing damage path.
- [ ] A headless test fires a weapon through `FireWeaponUpdate` (not `AttemptDamage` directly) and asserts a real health decrease.
- [ ] The P6 scenario matrix's move/attack/special-power cells assert a real observable effect instead of emitting a passing "GAP" message, or, for any cell where this is genuinely infeasible headlessly, the cell states the specific technical reason rather than silently passing.
- [ ] Existing `WI-P6-02` damage-terminal assertions still pass unmodified in behavior.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~CombatCoverageTests|FullyQualifiedName~P6ScenarioMatrixTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not rewrite the weapon/damage-type system broadly; implement only what closes the disclosed `FireWeaponUpdate` stub gap.
- Do not weaken or remove existing passing assertions to make new ones easier; add alongside them.
- Do not represent a still-unclosed gap cell as passing; keep a clear, named "GAP" marker for anything genuinely infeasible headlessly.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-7 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
