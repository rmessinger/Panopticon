# Task Card `WI-P6b-03`: Aircraft archetype contract

| Field | Value |
|---|---|
| Card ID | `WI-P6b-03` |
| Roadmap phase | [Track P · P6b](../../track-p-playable-baseline.md): P6b-3 Aircraft archetype contract, see [deep dive §3 Design](../../deep-dives/p6b-unit-contract-validation.md#2-design) and [§4 Sub-phases](../../deep-dives/p6b-unit-contract-validation.md#3-sub-phases) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | Track P phases P0, P4, P5, and P6 complete |
| Diagrams | [P6b Architecture Diagrams](../../deep-dives/p6b-unit-contract-validation.md#6-architecture-diagrams) |

## Goal

Create a CI-running P0-harness contract scenario test for the USA Comanche (`ObjectKinds.Aircraft`) that proves the full six-point aircraft archetype contract and fails loudly for missing handlers.

## Context to read (and only this)

- `docs/roadmap/track-p-playable-baseline.md:62`: P6b phase dependency and pass/fail intent.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:31-67`: archetype definitions and the six-point unit contract.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:72-77`: unimplemented special powers must surface as explicit gaps.
- `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:192-223`: current code appendix for tags, orders, weapons, powers, and death cleanup.
- `docs/roadmap/implementation-checklist.md:299-335`: checklist ACs to mirror.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs:912` and `src/Panopticon.Simulation/Logic/Object/ObjectKinds.cs:34-50,103-104,148-160,217-218,289-290`: `KindOf` tags for archetype detection.
- `src/Panopticon.Simulation/Gui/ControlBar/CommandSet.cs:7-34` and `src/Panopticon.Simulation/Gui/ControlBar/CommandButton.cs:30-47,87-104,219-285`: command-set buttons, special powers, and weapon slots.
- `src/Panopticon.Simulation/Logic/Orders/OrderType.cs:50-54,87-101,111-115` and `src/Panopticon.Simulation/Logic/Orders/Order.cs:197-240`: order names and factory argument shapes.
- `src/Panopticon.Simulation/Logic/Orders/OrderProcessor.cs:73-105,179-204,247-260,287-426,452-560`: move, build, produce, repair, attack, special-power, enter, and unimplemented-order dispatch.
- `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponSlot.cs:5-23`, `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponTemplateSet.cs:7-53`, `src/Panopticon.Simulation/Logic/Object/Weapon/WeaponSet.cs:21-62`, `src/Panopticon.Simulation/Logic/Object/Weapon/Weapon.cs:64-126`: weapon-slot and firing state.
- `src/Panopticon.Simulation/Logic/Orders/SpecialPower/SpecialPowerApplicator.cs:13-37`, `src/Panopticon.Simulation/Logic/Orders/SpecialPower/SpecialPowerAtLocationApplicator.cs:26-89`, `src/Panopticon.Simulation/Logic/Orders/SpecialPower/SpecialPowerAtObjectApplicator.cs:22-50`: implemented vs. throwing special-power handlers.
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs:291-330,897-900,1284-1337,1360-1435`: owner, selection flag, special-power behavior lookup, kill/die/destroy/container cleanup.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:71-128,178-199` and `src/Panopticon.Simulation/Logic/Player.cs:130-141,231-246,807-839`: object create/destroy owner tracking and selection cleanup.
- `src/Panopticon.Tools.ReplaySketch.Core/HarnessRunner.cs:32-133,296-320`: P0 run-and-assert entry point and handle resolution.
- `src/Panopticon.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs:20-84,101-218` and `src/Panopticon.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs:7-16`: assertion vocabulary and code-predicate escape hatch.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs:24-141` and `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P0HarnessAcceptanceTests.cs:450-513`: current test helpers; note P6b must add CI-running `[Fact]` coverage rather than relying only on Interactive tests.

## Steps

1. Create `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bAircraftContractTests.cs` as a real xUnit `[Fact]` test file that runs under `dotnet test src --filter Category!=Interactive`. Base the test on `MockedGameTest` or another no-game-install harness seam; a `[GameFact]` may be added only as supplemental coverage, never as the acceptance gate.
2. Build one P0-harness `ReplayScenario` in code for the USA Comanche. Use the reference unit from `docs/roadmap/deep-dives/p6b-unit-contract-validation.md:31-41`; if exact asset names need resolution, resolve them through loaded `ObjectDefinition.KindOf`/`CommandSet` data instead of hardcoding an unrelated unit.
3. Add code-defined predicates via `CodeAssertionRegistry` (or extend `ScenarioAssertion` only where necessary) for produced-object lookup, position delta, health delta, weapon-slot observation, command-set special-power enumeration, gap reporting, and death cleanup.
4. Execute the contract in ordered phases: produce, move/static check, land/takeoff cycle and airborne movement, attack/decline, weapon modes plus every command-set `SpecialPowerType`, then death/sell cleanup.
5. For every special-power or order handler that throws `NotImplementedException`, `ArgumentOutOfRangeException`, reaches the default unimplemented branch, or has no observable effect after the frame budget, record a failure message beginning `P6b gap:`. Do not skip, swallow, or convert it into a passing no-op.
6. Preserve all existing branches/guards in any production-code touch. If you must add defaults or sentinel values, state them in the test and assert the default path explicitly.
7. New `.cs` files must be UTF-8 with BOM. Run `dotnet format src`, then verify with `dotnet format src --verify-no-changes`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bAircraftContractTests.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Assertions/ScenarioAssertion.cs` (only if new assertion paths are required)
- `src/Panopticon.Tools.ReplaySketch.Core/Assertions/CodeAssertionRegistry.cs` (only if reusable predicates need registration helpers)

## Acceptance criteria

- [ ] Produced: order the reference aircraft, wait for production/spawn, and assert it exists in `GameLogic` and owner tracking.
- [ ] Moves: issue an airborne `MoveTo`, advance enough frames, and assert the aircraft changes position while preserving valid airborne/default locomotor state.
- [ ] Archetype action: exercise the land/takeoff or repair-airfield cycle used by the reference aircraft; missing handler/state is an explicit `P6b gap:` failure.
- [ ] Attack: issue `AttackObject`/`ForceAttackObject` against a dummy enemy and assert target health decreases.
- [ ] Weapon modes and special abilities: exercise every exposed `WeaponSlot` and every command-set `SpecialPowerType` (for example rocket pods if present); unimplemented/no-effect handlers fail with `P6b gap:`.
- [ ] Die with cleanup: kill the selected aircraft and assert removal from `GameLogic`, owner tracking, and selection state.
- [ ] The test is a real CI-running `[Fact]` path; it is not skipped and does not rely solely on `[GameFact]` or `Category=Interactive`.
- [ ] Unimplemented special-power handlers are logged/reported as explicit `P6b gap:` failures, not silent passes.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)), or the card is marked `Blocked` with the exact failing `P6b gap:` entries.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6bAircraftContractTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not weaken the contract by skipping no-op abilities or converting missing handlers into success.
- Do not use `[Fact(Skip=...)]`, Interactive-only tests, or placeholder assertion values as the acceptance gate.
- Do not refactor unrelated order, weapon, or special-power behavior; preserve every existing branch/guard.
- Keep this card to one archetype. Cross-archetype consolidation belongs in `WI-P6b-08`.

## On completion

- Set Status above to `Done` (or `Blocked` + exact `P6b gap:` reason).
- Tick the matching P6b-3 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange` listing the scenario/test file created and any special-power gaps.
- Confirm `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/P6bAircraftContractTests.cs` exists and the targeted test command above ran.

## Result

<!-- append after completing -->
