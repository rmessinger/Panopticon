# Task Card `WI-A5-03`: Define the declarative scenario artifact on top of the P0 harness

| Field | Value |
|---|---|
| Card ID | `WI-A5-03` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring & visualization, sub-phase A5-1 |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-06` (the host supplies the input-injection and initial-state path A5-1 needs, per [a5-visualization.md §7](../../deep-dives/a5-visualization.md#7-sequencing-notes)) |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) (placeholder; must be replaced before this card is `Ready`) |

## Goal

Define the declarative scenario artifact of
[a5-visualization.md §3.1](../../deep-dives/a5-visualization.md#31-what-a-scenario-is) as a
serializable type on top of the existing P0 harness scenario model, and make `SimulationHost`
consume it, so a scenario round-trips from file to run.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §3.1, §3.2, §3.4, §6 (A5-1 row), §8.
- `docs/roadmap/deep-dives/p0-playable-baseline-harness.md` §8: the existing scenario and command
  model this extends. A5-1 builds on P0; it does not replace it.
- `src/OpenSage.Tools.ReplaySketch.Core/`: the existing scenario, command, and assertion types, and
  their JSON serialization conventions.
- `src/OpenSage.Game/SimulationHost.cs`: produced by `WI-A1-06`; the consumer.

## Steps

1. Extend the existing P0 scenario type with the initial-state fields §3.1 names that it lacks.
   Extend; do not define a second scenario format.
2. Reuse the P0 command model for the ordered command sequence verbatim.
3. Add a loader that reads a scenario file and configures a `SimulationHost` run from it.
4. Add the pre-run validation of §3.4: reject a scenario naming an unknown object definition,
   player, or map before the run starts, with the offending field in the error.
5. Add a test that authors a scenario in code, serializes it, reloads it, runs it headlessly, and
   asserts the run reached the expected frame count.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/` (scenario type extension and loader)
- `src/OpenSage.Tools.ReplaySketch.Tests/` or `src/OpenSage.Game.Tests/` (new test file)

## Acceptance criteria

- [ ] A scenario file round-trips through serialize, reload, and run.
- [ ] Pre-run validation rejects an unknown definition, player, or map, naming the field.
- [ ] The P0 command model is reused, not duplicated.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add procedural or capture-from-run authoring. Those are separate A5-1 cards.
- Do not add the short preview of §3.4; validation only.
- Do not add sweep axes. That is A5-2, and P0-5b already provides the expansion primitive.
- Do not build any visualization.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Note partial progress on A5-1 in [`implementation-checklist.md`](../../implementation-checklist.md):
  capture-from-run is still outstanding.
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - Capture-from-run authoring (turn a completed run into a scenario).
  - Procedural seeding for large scenarios (pairs with A5-5).
  - Pre-run preview rendering.
