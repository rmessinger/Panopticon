# Task Card `WI-W1-03`: Add deterministic streams and Poisson sampler

| Field | Value |
|---|---|
| Card ID | `WI-W1-03` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W1-01` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W1-01` is Done and `System.IO.Hashing` is available.

Add XxHash64 stream derivation, `SageRandom` construction, and exact Poisson sampling.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3 and §8.2.
- `src/Panopticon.Mathematics/IRandom.cs:6-135`: `IRandom` and `SageRandom`.
- `src/Panopticon.Simulation.WesternFront/Parameters/` from `WI-W1-01`.

## Steps

1. Add `WesternFrontRandomStreams` deriving XxHash64 over UTF-8 `runSeed|regionPairKey|tickIndex|streamName`.
2. Fold the 64-bit hash to a deterministic `uint` seed and initialize `SageRandom`.
3. Add `PoissonSampler` using Knuth for `lambda < 10` and Hörmann PTRS for `lambda >= 10`.
4. Add deterministic tests for identical inputs, changed stream names, changed ticks, and stable sample sequences.
5. Add distribution-smoke tests with bounded sample mean tolerance.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Random/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontRandomTests.cs`

## Acceptance criteria

- [ ] Same tuple derives the same first 100 draws across two runs.
- [ ] Changing only `streamName` changes the stream.
- [ ] Tests cover both Poisson algorithm branches.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontRandom"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No combat logic.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
