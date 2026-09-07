# Task Card `WI-A4-37`: Design the experiment harness

| Field | Value |
|---|---|
| Card ID | `WI-A4-37` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): experiment harness |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | B1 benchmark API and report contract must be implementation-ready |
| Diagrams | This card creates the A4 harness diagrams. |

## Goal

Define experiment configuration, implementation bindings, B1 execution, statistical result
envelope, artifact identity, and the A5 consumer contract.

## Context to read (and only this)

- Completed B1 implementation plan and benchmark API.
- `docs/roadmap/seams.md`.
- `docs/roadmap/deep-dives/a5-visualization.md` §§3.3 and 4.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ScenarioMatrix.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/StatisticalRunHarness.cs`.
- B1 benchmark runner and report types.

## Steps

1. Create `docs/roadmap/deep-dives/a4-experiment-harness.md`.
2. Define registry keys, typed bindings, scenario/benchmark references, sample settings, hardware
   context, output artifacts, and failure semantics.
3. Assign the implementation project and CLI/API ownership.
4. Define the A5-consumable report envelope and compatibility checks.
5. Add grounded class and sequence diagrams and index the deep dive.

## Files likely to change

- `docs/roadmap/deep-dives/a4-experiment-harness.md` (new)
- `docs/roadmap/track-a-foundations.md`
- `docs/roadmap/README.md`
- `docs/roadmap/implementation/track-a-a4-experiment-harness.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] One config can bind any registered seam implementation.
- [ ] B1 owns benchmark mechanics; A4 owns selection/orchestration.
- [ ] A5 can load results without knowing concrete implementations.
- [ ] Failure and compatibility behavior is explicit.

## Validation commands

```sh
cd docs/roadmap
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/a4-experiment-harness.md -o /tmp/a4-harness.svg
rm -f /tmp/a4-harness*.svg
```

## Out of scope / guardrails

Do not implement or redesign B1.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-38`.
