# Task Card `WI-B4-02`: Allocation-free, pair-once partition update

| Field | Value |
|---|---|
| Card ID | `WI-B4-02` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Ready` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-B4-01` |
| Diagrams | [Partition class and sequence](../../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams) |

## Goal

Remove the allocation and duplicate-pair costs from `PartitionCellManager.Update` so the
span holds its budget at contact density: reusable buffers, O(1) membership removal,
pair-once delivery.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` section "Partition hot path"
  and its "Changes" list.
- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs` in full.

## Files

- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs`
- `test/Panopticon.Simulation.Tests/` partition and persistence tests

## Steps

1. Keep the candidate pair list across ticks: clear it, do not reallocate.
2. Replace `List.Remove` membership with an indexed structure: per-cell slot lists plus a
   per-object back-index so removal is swap-remove. `Persist` stores cell values and shroud
   reveals only, so membership storage is free to change.
3. Emit each pair at most once per tick: canonical ordering over partition objects; skip
   pairs already emitted and pairs where both sides are static cell mates.
4. Replace the per-object `HashSet<PartitionCell>` and the `GetCells()` iterator in the
   update path with a small reused list and non-allocating enumeration. Public query paths
   may keep their shape.
5. Keep the counters from `WI-B4-01` correct through the rewrite.

## Acceptance criteria

- [ ] `Update` performs no per-tick list, set, or iterator allocations on the steady path.
- [ ] An install-free test proves pair-once delivery: two objects sharing multiple cells, and
  two dirty objects sharing a cell, each produce one `OnCollide` exchange per tick.
- [ ] `Persist` roundtrip and existing partition query tests stay green.
- [ ] Seeded determinism and golden baseline suites stay green; any failure is a suspected
  deviation: preserve artifacts, report, and wait for the operator before changing baselines.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Partition"
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No changes to `QueryObjects` semantics or targeting behavior.
- No throttling or approximation: every real collision still reports exactly once per tick.
- Pair-once removes today's duplicate `OnCollide` calls; if a gameplay module depended on
  duplicates, stop and report rather than compensating silently.

## On completion

- Set Status above to `Done`.
- Reconcile `WI-B4-03` from `Blocked` to `Ready`.
- Append a result line to repo-root `.exchange`.
