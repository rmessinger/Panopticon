# Task Card `WI-A5-05`: Localize the first divergence between two runs

| Field | Value |
|---|---|
| Card ID | `WI-A5-05` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring & visualization, sub-phase A5-4 |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-04` and `WI-A5-07`; T1 recording is already implemented by `WI-A1-09` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) (placeholder; must be replaced before this card is `Ready`) |

## Goal

Implement the §5 drill-down: given two run outputs, walk their T1 signature trees and report the
first subtree whose divergence exceeds tolerance, with the frame and the locus. This is the tool
that makes B3 parallelism and B6 sharding debuggable.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §5, §5.1, §5.2, §6 (A5-4 row), §8.
- `docs/roadmap/open-questions.md`: the determinism-priority Decision. Comparison is diagnostic,
  never a correctness gate.
- `src/OpenSage.Game/Recording/SignatureTree.cs`: the landed T1 input format.
- `src/OpenSage.Tools.ReplaySketch.Core/`: where the comparison logic lives, headlessly testable.

## Steps

1. Add a comparator to `ReplaySketch.Core` that first verifies matching domain key, bucket schema,
   cell settings, and recording-time tolerances from `WI-A5-07`.
2. Compare stored hashes frame by frame and descend into differing subtrees. Report the first frame,
   leaf-most path, and signatures. Exact per-quantity values are `WI-A5-22`'s T2 responsibility.
3. Treat a missing or extra bucket under a compatible schema as a divergence. Reserve unusable
   comparison for incompatible recording configurations.
4. Surface the report in the results view added by `WI-A5-04`.
5. Add a headless `.Core` test built on a synthetic known-divergent pair: two recordings identical
   until a chosen frame and locus. Assert the comparator finds exactly that frame and locus.
6. Add a second test asserting that two within-tolerance runs report no divergence.
7. Validate the UI with ReplaySketch screenshot mode and confirm the map panel still renders.
8. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/`
- `src/OpenSage.Tools.ReplaySketch.Maui/`
- `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] Given a synthetic known-divergent pair, the comparator reports exactly the seeded frame and
      locus.
- [ ] Two within-tolerance runs report no divergence.
- [ ] Missing or extra buckets under a compatible schema are reported as divergences.
- [ ] Mismatched domain, bucket schema, cell settings, or recording-time tolerances are reported as
      incompatible, not as divergences.
- [ ] Comparison logic lives in `.Core` and is covered by headless tests.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/a5-divergence.png dotnet run --project src/OpenSage.Tools.ReplaySketch.Maui
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not inspect T2 detail. `WI-A5-21` configures capture and `WI-A5-22` owns inspection.
- Do not assert bit-equality anywhere, including in the tests.
- Do not apply a new post-recording tolerance to T1 hashes. Tolerances are recording metadata and
  must match before comparison.
- Do not extend this to distributed or sharded runs. B6 consumes this tool; it does not shape it.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A5-4 in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - `WI-A5-21`: regional T2 capture configuration.
  - `WI-A5-22`: T2 inspection from the divergence report.
