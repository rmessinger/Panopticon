# Task Card `WI-A5-26`: Track A5 throughput in B1

| Field | Value |
|---|---|
| Card ID | `WI-A5-26` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-5 authoring and visualization at scale |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A5-16`, `WI-A5-23`, `WI-A5-25`; B1 harness implemented |
| Diagrams | B1 diagrams and [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Add B1-owned benchmarks for procedural expansion, serialization, bundle loading, random seek, and
visible-frame rendering at A5 scale.

## Context to read (and only this)

- B1 benchmark API, report schema, and baseline policy.
- A5 scale-budget section in the implementation plan.
- Procedural generator, matrix bundles, indexed reader, and replay renderer.

## Steps

1. Add B1 scenarios for 10k entities, 10k commands, and a long recording.
2. Measure expansion, serialization, bundle open, random seek, and visible render projection.
3. Record time, throughput, allocations, statistical summary, and hardware context.
4. Establish documented baselines/tolerances using B1's process.
5. Link A5-5 budgets to these benchmark IDs.

## Files likely to change

- B1 benchmark project/files
- A5 implementation plan
- Benchmark tests or smoke checks

## Acceptance criteria

- [ ] All five A5 operations emit machine-readable B1 results.
- [ ] Scale fixtures meet the documented entity, command, and run-length sizes.
- [ ] Statistics, allocations, hardware, and thresholds use B1 infrastructure.
- [ ] A5 contains no standalone stopwatch gate.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~A5Benchmark"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not loosen thresholds to make an initial run pass without following B1 baseline policy.

## On completion

- Set Status above to `Done` or `Blocked`.
- Tick A5-5 when all scale cards are complete.
- Append a result line to `.exchange`.
