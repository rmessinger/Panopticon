# Task Card `WI-P6-06`: Control-bar live dispatch verification

| Field | Value |
|---|---|
| Card ID | `WI-P6-06` |
| Roadmap phase | [Track P · P6](../../track-p-playable-baseline.md): see [deep dive](../../deep-dives/p4-p6-economy-production-combat.md#p6-combat-and-command-verification), sub-phase P6-6 |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P6-01` |
| Diagrams | [P4/P6 Architecture Diagrams](../../deep-dives/p4-p6-economy-production-combat.md#architecture-diagrams) |

## Goal

The track-p goal for P6 is orders issued "through the control bar end-to-end". `WI-P6-01`'s
`P6CommandCoverageTests` only asserts that each `CommandType` maps to an expected `OrderType` with
a real (non-default) `OrderProcessor` handler, and statically cross-checks `CommandButtonCallback`
wiring — it never executes a real `GeneralsControlBar` button click/dispatch. 7 of 9 baseline
actions (move, attack, force-attack, sell, repair, special-power, rally point) are recorded as
named Scene3D-dependent gaps rather than driven. Close this gap: exercise real control-bar dispatch
headlessly for as many of these 7 actions as the mocked host can support.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md:55-64` and `:74-78`: the button→`OrderType`→handler design intent.
- `docs/roadmap/agent/cards/WI-P6-01-command-coverage-matrix.md`: prior card; its Result section documents exactly which actions are gaps and why (Scene3D dependency).
- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6CommandCoverageTests.cs`: the existing static-mapping tests and the `HandledOrderTypes` curated set to build real dispatch coverage on top of.
- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs`: `ApplyCommandSet`, `CommandButtonCallback`, and how a button click ultimately calls into order generation — the real path to drive instead of asserting only the static mapping.
- `src/OpenSage.Game/Logic/OrderGenerators/OrderGenerator.cs` and concrete order generators: what stands between a control-bar click and an `Order` being created; identify the minimum Scene3D/selection state the mocked host needs to drive this live.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-70`: headless test base; check what `Scene3D`/selection seams already exist (note: `WI-P5-01`'s Result section added several null-guards for headless object creation — check if those are sufficient here, or if more are needed).

## Steps

1. For each of the 7 currently-gap actions (move, attack, force-attack, sell, repair, special-power, rally point), determine whether the mocked headless host can support a real control-bar click → order-generator → `OrderProcessor` path, or whether it genuinely requires a live Scene3D/renderer.
2. For every action where headless live dispatch is feasible, add a test that invokes the real `GeneralsControlBar` callback (or the order-generator path it calls into) and asserts the resulting `Order` reaches `OrderProcessor` and produces the expected effect (reuse existing per-order-type assertions from `WI-P6-02`/the P6 scenario matrix where applicable).
3. For any action that genuinely cannot be driven headlessly, keep the existing disclosed-gap test but do not represent it as anything more than a gap; do not weaken previously-passing assertions to force a false pass.
4. Update `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md`'s P6-1/P6-6 notes to reflect exactly which actions now have live dispatch coverage vs. remain disclosed gaps.
5. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game.Tests/Tools/ReplaySketch/P6CommandCoverageTests.cs`
- `src/OpenSage.Game.Tests/MockedGameTest.cs` (only if additional headless seams are required)
- `docs/roadmap/deep-dives/p4-p6-economy-production-combat.md`

## Acceptance criteria

- [ ] Each of the 7 previously-gap control-bar actions is either (a) driven live headlessly through the real `GeneralsControlBar` callback with an asserted effect, or (b) still a disclosed, named gap with a specific technical reason it cannot be driven headlessly.
- [ ] No previously-passing assertion is weakened to fake a pass.
- [ ] The deep-dive P6 notes accurately state which actions have live dispatch coverage.
- [ ] Headless `[Fact]` tests are not skipped and do not require game files.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~P6CommandCoverageTests"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build a full renderer or windowed test path; this must remain CI-runnable headlessly.
- Do not remove the existing static `OrderType`-mapping tests; add live-dispatch tests alongside them.
- Do not claim an action is "driven live" if it is still only checked via static mapping.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P6-6 row in [`deep-dives/p4-p6-economy-production-combat.md`](../../deep-dives/p4-p6-economy-production-combat.md#p6-sub-phases).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
