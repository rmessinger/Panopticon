# Task Card `WI-P9-03`: Direct-spawn host

| Field | Value |
|---|---|
| Card ID | `WI-P9-03` |
| Roadmap phase | [Track P · P9](../../track-p-playable-baseline.md): P9-3 Direct-spawn host, see [deep dive §2.2](../../deep-dives/p9-unit-validation-ground.md#22-direct-spawn-instead-of-production) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-P9-02` |
| Diagrams | [P9 Architecture Diagrams](../../deep-dives/p9-unit-validation-ground.md#6-architecture-diagrams) |

## Goal

Add `ValidationGroundHost` in `Panopticon.Tools.TestHarness` that applies a `SpawnPlan` to a live
`IGame` through `GameLogic.CreateObject`, sampling terrain height for each position, and runs the
settle stage before any order is issued.

## Context to read (and only this)

- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, §2.1 (four run stages), §2.2, and the
  "Direct-spawn seam" and "Host integration points" entries in the appendix.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:76`: `public GameObject CreateObject(ObjectDefinition, Player)`.
- `src/Panopticon.Simulation/Logic/GameLogic.cs:412`: an existing `CreateObject` call site, for the
  definition-lookup and ownership pattern.
- `src/Panopticon.Tools.TestHarness/Program.cs:206-260`: `RunWindowed`, including `game.StartRun()` at
  `:217` (the spawn hook point) and the per-frame poll block at `:253-255`.
- `src/Panopticon.Tools.TestHarness/EngineActionRecorder.cs:60-90`: how the recorder resolves objects
  and seeds per-object state from the live game.
- `src/Panopticon.Tools.ReplaySketch.Core/Model/SpawnPlan.cs`: the plan shape from `WI-P9-02`.

## Steps

1. Add `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs`.
2. Implement `Spawn(SpawnPlan plan, IGame game)`: resolve each `DefinitionName` through the
   `AssetStore` object-definition collection, resolve the owning `Player` from the placement's player
   index, sample terrain height for the planar position, and call `GameLogic.CreateObject`.
3. Record the resulting `ObjectId` for each placement so `WI-P9-04` can key timings by unit.
4. If a definition or player cannot be resolved, record it as a named gap and continue; do not throw
   and do not abort the run for one bad definition.
5. Implement the settle stage: expose the scenario's `SettleFrames` so the caller advances that many
   frames before orders are issued, and expose an `IssueOrders(frame)` entry point that dispatches
   each unit's `PrimaryAction` as a real `Order` on a single frame.
6. Spawn targets and supply sources as neutral or opposing owners so combat units can legally attack
   them.
7. Ensure any new `.cs` file is UTF-8 with BOM. Run `dotnet format src`, then verify
   `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs` (new)

## Acceptance criteria

- [ ] After `Spawn`, every resolvable placement in the plan exists in `GameLogic` with the planned
      owner and terrain-sampled position.
- [ ] Unresolvable definitions or players produce a named gap entry and do not abort the run.
- [ ] Targets and supply sources are owned such that combat units can legally attack them.
- [ ] No order is dispatched before the settle stage has elapsed.
- [ ] Orders are dispatched as real engine `Order` values, not by directly mutating unit state.
- [ ] Any new `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and
      `dotnet format src --verify-no-changes` passes.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not measure durations here; that is `WI-P9-04`.
- Do not wire this into `Program.cs` yet; that is `WI-P9-05`.
- Do not bypass `OrderProcessor` by mutating `AIUpdate` or health directly. Only spawning is a
  shortcut; the actions must be real orders.
- If directly spawned aircraft do not reach a valid state, spawn a faction airfield rather than
  special-casing the unit or the timer, and note it in the card result.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the P9-3 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.

## Result

<!-- append after completing -->
