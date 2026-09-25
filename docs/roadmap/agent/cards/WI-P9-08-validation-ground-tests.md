# Task Card `WI-P9-08`: Validation-ground test coverage

| Field | Value |
|---|---|
| Card ID | `WI-P9-08` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-8 Test coverage, see [deep dive §3](../../deep-dives/p9-unit-validation-ground.md#3-placement-in-the-codebase) |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-P9-07` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Lock the validation ground with install-free tests for the engine-free logic and one install-gated
end-to-end test for the live run, and document the workflow in the TestHarness agent playbook.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §3 and the "Why the mocked host is
  insufficient" appendix entry.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs`: the install-free host. `Scene3D` is uninitialized and
  `Update`/`Render`/`Step` throw, which is why only engine-free logic can be tested here.
- `test/Panopticon.Simulation.Tests/GameFactAttribute.cs`: the install-gated test attribute.
- `test/Panopticon.Simulation.Tests/GameTestDiscoverer.cs:22-44`: how `[GameFact]` tests are skipped when the
  game is not installed.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: the agent validation playbook to extend.
- The types delivered by `WI-P9-01` through `WI-P9-07`.

## Steps

1. Consolidate or add `[Fact]` coverage so the following hold without a game install: layout
   layout rules, roster completeness against `UnitCatalog`, primary-action mapping totality, and
   baseline comparison including the minimum frame floor.
2. Add a `[GameFact(SageGame.CncGenerals)]` test running the validation ground headless end to end
   and asserting every roster unit either completes within its baseline tolerance or is a reported
   gap or timeout.
3. Make the live test fail on regression rather than logging it, and make it report which units
   failed so the failure is actionable without re-running.
4. Confirm the install-free tests run in CI (which skips `[GameFact]`) and that the suite passes with
   `--filter Category!=Interactive`.
5. Add a Unit Validation Ground section to `src/Panopticon.Tools.TestHarness/AGENTS.md`: how to run it
   windowed, how to run it headless, how to refresh the baseline, and how to read gaps and timeouts.
6. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `test/Panopticon.Simulation.Tests/Tools/ReplaySketch/ValidationGroundTests.cs` (new)
- `src/Panopticon.Tools.TestHarness/AGENTS.md`

## Acceptance criteria

- [ ] Install-free `[Fact]` tests cover layout rules, roster completeness, action-mapping
      totality, and baseline comparison including the frame floor.
- [ ] A `[GameFact(SageGame.CncGenerals)]` test runs the ground end to end and asserts every unit
      completes within tolerance or is a reported gap or timeout.
- [ ] The live test fails on regression and names the failing units.
- [ ] No acceptance gate is a permanently skipped `[Fact(Skip=...)]`.
- [ ] `dotnet test src --filter Category!=Interactive` passes with no game install present.
- [ ] `AGENTS.md` documents windowed run, headless run, baseline refresh, and how to read gaps and
      timeouts.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ValidationGround"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not try to run the live scenario under `MockedGameTest`; `Scene3D` is null there and
  `Update`/`Step` throw, so movement and combat cannot resolve.
- Do not weaken the live test to a warning when a unit regresses.
- Do not fix gaps discovered by the live test in this card. Unimplemented handlers escalate to P6b;
  spawn-versus-production differences escalate to P5. Record them in `.exchange`.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-8 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Update the P9 phase Status in [`track-p-playable-baseline.md`](../../track-p-playable-baseline.md)
  if this card completes the phase.
- Append a result line to repo-root `.exchange`, listing any unit escalated as a gap.

## Result

<!-- append after completing -->
