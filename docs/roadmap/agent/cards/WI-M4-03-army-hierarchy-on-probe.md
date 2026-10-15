# Task Card `WI-M4-03`: Army hierarchy on the M1 probe

| Field | Value |
|---|---|
| Card ID | `WI-M4-03` |
| Roadmap phase | [M4](../../track-m-mass-battle.md): army-scale AI |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M4-01`, `WI-M4-02` |
| Diagrams | [M4 army hierarchy](../../deep-dives/mass-battle-1k.md#m4-army-hierarchy) |

## Goal

Both M1 armies run the hierarchical policy instead of scripted orders, sized per the WI-M4-01 decisions, and every ground unit stays assigned to a squad and commander at start and after every casualty (M4-1, ground half).

## Context to read (and only this)

- `docs/roadmap/implementation/track-m-m4-army-ai.md` (WI-M4-01 decisions).
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/SquadHierarchy.cs`, `Squad.cs`, `HierarchicalAIDecisionPolicy.cs`.
- `src/Panopticon.Simulation/Logic/AI/AIDecisionPolicyFactory.cs` and `PlayerSettings.AIPolicy`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/HeadlessHierarchicalMatchEvaluator.cs` lines 40-75: hierarchical player setup pattern.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: player setup, `DisableAI`, `IssueGeneralAttack`, jet re-issue loop.

## Steps

1. Apply the WI-M4-01 sizing decisions in `SquadHierarchy` (squad size, linear assignment, aircraft excluded from ground squads); keep assignment and promotion deterministic.
2. Apply the WI-M4-01 actuation decision so each soldier order reaches its own unit (per-squad selection before the squad's orders).
3. Add an army-AI mode to the capacity probe (config knob or new built-in scenario per WI-M4-01): players get `AIPolicy` `Hierarchical`, AI stays enabled, scripted ground orders are skipped. Aircraft keep the M1 scripted sortie loop until WI-M4-05.
4. Record per decision tick: squads, members, unassigned eligible units, squads without a live commander; write them to the engagement report.
5. Add install-free tests for assignment and promotion at 1000 units (synthetic objects), and a scaled Interactive test of the army-AI mode.

## Files likely to change

- `src/Panopticon.Simulation/Logic/AI/Hierarchy/SquadHierarchy.cs`
- `src/Panopticon.Simulation/Logic/AI/Hierarchy/HierarchicalAIDecisionPolicy.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeReport.cs`
- `src/Panopticon.Tools.TestHarness/TestHarnessConfig.cs`
- `test/Panopticon.Simulation.Tests/Logic/AI/Hierarchy/SquadHierarchyScaleTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Tools/TestHarness/MassBattleCapacityProbeScenarioTests.cs`

## Acceptance criteria

- [ ] At start and after every decision tick, zero eligible ground units are unassigned and every non-empty squad has a live commander.
- [ ] Each soldier order is applied to its own unit (test asserts unit target points after dispatch).
- [ ] Existing A4-3 hierarchy tests (`HierarchyReferenceContractTests` and siblings) pass unchanged or with documented updates.
- [ ] Scaled army-AI run: both sides close and take losses with no scripted ground orders.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~Hierarchy"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet test src --filter "FullyQualifiedName~MassBattleCapacityProbe"
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No group movement wiring (WI-M4-04) and no air commander (WI-M4-05).
- Same-seed determinism of the hierarchy must hold; a `SeededRunTests` failure is a suspected deviation (stop and report).

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - none expected.
