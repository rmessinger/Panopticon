# Task Card `WI-A4-30`: Run and report hierarchical co-evolution

| Field | Value |
|---|---|
| Card ID | `WI-A4-30` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3f hierarchical training harness |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-29` |
| Diagrams | [Hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) |

## Goal

Extend the flat evolution runner to advance the three rank populations according to the fixed
co-evolution contract and emit rank-aware reports.

## Context to read (and only this)

- Updated hierarchy deep dive and implementation plan.
- Flat evolution models and runner from `WI-A4-13` through `WI-A4-16`.
- Per-rank genome contracts and hierarchy adapter.

## Steps

1. Add a rank-population configuration and evaluation tuple.
2. Implement the documented schedule and partner sampling.
3. Aggregate rank and match fitness, then advance each due population.
4. Emit per-rank ancestry, best/mean/worst fitness, diversity, and match outcomes.
5. Add a deterministic fake-evaluator test and a small install-backed smoke test.

## Files likely to change

- Evolution types under `src/OpenSage.Tools.ReplaySketch.Core/`
- Tests under `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] Evaluation count and partner tuples match the design.
- [ ] Reports record all configuration, seeds, partner tuples, fitness samples, and ancestry needed
      to trace a run; simulation-derived reports are not required to be identical across reruns.
- [ ] Rank failures identify rank, genome, partners, and match seed.
- [ ] Report JSON round-trips.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~HierarchicalCoevolution"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not alter the network contracts or optimize training performance.

## On completion

- Set Status above to `Done` or `Blocked`.
- Mark A4-3 complete.
- Append a result line to `.exchange`.
