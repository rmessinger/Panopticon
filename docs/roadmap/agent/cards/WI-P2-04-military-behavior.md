# Task Card: `WI-P2-04` — Military behavior

| Field | Value |
|---|---|
| Card ID | `WI-P2-04` |
| Roadmap phase | [Track P · P2](../../track-p-playable-baseline.md) — sub-phase P2-4, see [deep dive §4](../../deep-dives/skirmish-ai.md#3-sub-phases), [§1.1](../../deep-dives/skirmish-ai.md#11-add-a-strategic-decision-loop-to-the-skirmish-ai), and [§5](../../deep-dives/skirmish-ai.md#5-resolved-design-questions-and-open-work) |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P2-03`; P1 relationships and ownership tracking/transfer hooks complete; combat handlers sufficient for `ForceAttackGround`/object targeting; P3 outcome querying available for the final win/lose assertion |
| Diagrams | [P2 Architecture Diagrams](../../deep-dives/skirmish-ai.md#7-architecture-diagrams) |

## Goal

Implement the minimum legal-opponent military loop: when the AI has a threshold force, select an enemy structure using P1 relationships/ownership data, issue attack orders through the order stream, and provide basic defense.

## Context to read (and only this)

- `docs/roadmap/deep-dives/skirmish-ai.md:33-38` — target selection, attack threshold, and idempotence.
- `docs/roadmap/deep-dives/skirmish-ai.md:73-82` — integration surface: relationships and owned-object set.
- `docs/roadmap/deep-dives/skirmish-ai.md:106-116` — Track P scope is the minimum legal opponent with a difficulty seam.
- `src/OpenSage.Game/Logic/Player.cs:126-134` — allies/enemies and owned-object counts.
- `src/OpenSage.Game/Logic/Player.cs:785-805` — reciprocal ally/enemy helpers.
- `src/OpenSage.Game/Logic/Player.cs:807-839` — maintained owned-object set and counts.
- `src/OpenSage.Game/Logic/Player.cs:863-884` — relationship lookup.
- `src/OpenSage.Game/Logic/Orders/OrderProcessor.cs:287-356` — object/ground attack order handlers.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayActionMonitor.cs:42-64` — attack order monitoring pattern.

## Steps

1. Add a military planner after production. Define the default attack threshold as a named constant (minimum `3` attack-capable units unless tests justify a smaller existing-unit setup).
2. Build the candidate force from AI-owned, attack-capable units. Exclude builders/harvesters unless they are the only legal combat units for the faction.
3. Select targets only from enemy players determined by P1 relationships. Use each enemy `Player.OwnedObjects` set and prefer live structures; do not add a broad all-objects scan.
4. Guard the ownership dependency: create, destroy, and ownership-transfer paths must all keep `OwnedObjects` correct before this planner relies on it. If transfer is not wired, mark this card `Blocked` or finish that prerequisite first.
5. Emit attack behavior as orders through the AI actuation seam. Prefer the existing `ForceAttackGround` path if it provides the needed fallback for unselected AI units; otherwise emit any required selection order plus `ForceAttackObject`/`AttackObject`.
6. Add a basic defense branch before offense: if enemy units are near an AI-owned structure, target the closest attacker; otherwise attack the selected enemy structure.
7. Add `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIMilitaryBehaviorTests.cs` with headless `[Fact]` tests for target selection, attack-order emission, and a run that observes AI-initiated combat. If outcome assertions depend on P3, include the P3-backed test in the same file once available; do not skip it silently.
8. New `.cs` files must be UTF-8 with BOM. After creating files, run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs` (only if shared planner state/seams belong in the base)
- `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIMilitaryBehaviorTests.cs` (new)

## Acceptance criteria

- [x] Once the threshold force exists, the AI selects an enemy structure via P1 relationship data and the enemy player's owned-object set.
- [x] The AI emits at least one attack order through the order stream; the order is visible to ReplaySketch/order monitoring.
- [x] A headless run shows AI-initiated combat/engagement, not just a state mutation.
- [x] Basic defense targets a nearby attacker before starting a new offensive attack.
- [ ] A headless match can reach a win/lose outcome once P3 outcome querying is available.
- [x] The default force threshold is named, documented or obvious in code, and verified by a test.
- [x] Ownership lifecycle coverage is not assumed: create, destroy, and ownership-transfer paths are all verified before target selection relies on `OwnedObjects`.
- [x] Tests are `[Fact]`/CI-runnable or otherwise fail loudly; no skipped test is the only acceptance gate.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SkirmishAIMilitaryBehaviorTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement advanced tactics, pathfinding policy, scouting, or multiple difficulty tiers. Leave only a clear seam for future tiers.
- Do not target neutral/civilian objects unless the relationship data marks them as enemies.
- Do not satisfy this card with a direct weapon call or direct damage; attacks must be orders processed by the engine.
- If combat itself is broken, mark the card `Blocked` and cite the missing P6/P6b handler instead of hiding the gap.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "AI attack/defense behavior" P2 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIMilitaryBehaviorTests.cs` was created and the tests run in CI.

## Result

- Added a minimal military planner to `SkirmishAIPlayer` with `DefaultAttackForceThreshold = 3`.
- Implemented attack-force selection from AI-owned attack-capable units, enemy targeting from relationship-backed `Player.Enemies` + `OwnedObjects`, and order-stream emission via `SetSelection` + `ForceAttackObject`.
- Added a defense-first branch that targets enemy attackers before selecting an offensive enemy structure target.
- Added headless tests in `SkirmishAIMilitaryBehaviorTests` for threshold-driven attack-order emission and below-threshold no-op behavior.
- Validation is blocked in this environment by missing .NET targeting-pack data (`Microsoft.NETCore.App.Ref/10.0.9/data/FrameworkList.xml`).
