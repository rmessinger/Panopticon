# Task Card `WI-B4-05`: Asynchronous path requests

| Field | Value |
|---|---|
| Card ID | `WI-B4-05` |
| Roadmap phase | [B4](../../track-b-scale.md): spatial and pathfinding scaling |
| Status | `Blocked` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-B4-01` |
| Diagrams | [Group movement sequence](../../deep-dives/b4-spatial-pathfinding-scaling.md#architecture-diagrams) |

## Goal

Add a queued, off-thread path request API beside synchronous `Navigation.CalculatePath`:
workers compute paths off the logic thread, completions land within a bounded frame count,
and application order stays deterministic.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4-spatial-pathfinding-scaling.md` section "Asynchronous path
  requests".
- `src/Panopticon.Simulation/Navigation/Navigation.cs` and `Graph.cs`.

## Files

- `src/Panopticon.Simulation/Navigation/` request queue
- `test/Panopticon.Simulation.Tests/` async request tests

## Steps

1. Author the request API: submit start and end, receive a handle; workers drain a queue.
2. Complete requests on the logic thread in submission order within an authored frame
   bound.
3. Keep `CalculatePath` synchronous and unchanged for existing callers.
4. Tests: completion ordering, frame bound, cancellation on object death, and seeded
   equality between a synchronous and an async-driven scripted run.

## Acceptance criteria

- [ ] Async completion is frame-bounded and applied in deterministic order.
- [ ] Cancelled requests (dead objects) do not apply.
- [ ] A seeded comparison run is byte-identical to its synchronous twin.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~PathRequest"
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No thread-affinity changes to game state: workers only search graphs.
- If deterministic completion cannot be held, stop and report; do not relax the seeded
  equality bar.

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
