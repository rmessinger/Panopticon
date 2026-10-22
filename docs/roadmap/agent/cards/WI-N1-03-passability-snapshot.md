# Task Card `WI-N1-03`: Versioned passability snapshot for workers

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-N1-03` |
| Roadmap phase | [N1](../../track-n-pathfinding-performance.md): Search core efficiency |
| Status | `Blocked` (on `WI-N1-02`) |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-N1-02` |
| Diagrams | [Track N architecture diagrams](../../deep-dives/n-multi-unit-pathfinding.md#8-architecture-diagrams) |

## Goal

Replace the per-tick `Graph.ClonePassability` worker snapshot with a versioned passability byte
array and one reusable search graph per worker.

## Context to read (and only this)

- `docs/roadmap/deep-dives/n-multi-unit-pathfinding.md` section 4 (N1).
- `src/Panopticon.Simulation/Navigation/PathRequestQueue.cs`, `PathRequest.cs`, `Navigation.cs`, `Graph.cs`.
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`.
- `docs/roadmap/implementation/track-b-b4-spatial-scaling.md`: "WI-B4-06 hierarchical gate" anchor.

## Steps

1. `UpdateAreaPassability` increments a passability version.
2. Add `PassabilitySnapshot` (version, passability bytes). `Submit` reuses the current snapshot
   while the version is unchanged instead of cloning per tick.
3. The worker keeps one `Graph` and applies a snapshot's bytes only when its version differs
   from the last applied one.
4. Tests: a request submitted before a passability change computes against the old
   passability; one submitted after uses the new; async results equal synchronous results on
   the corpus.
5. Re-run the B4 async anchor (same knobs as WI-B4-06) and record allocation per burst tick and
   submit-to-apply wait before and after in "WI-N1 results".

## Files likely to change

- `src/Panopticon.Simulation/Navigation/PathRequestQueue.cs`, `PathRequest.cs`, `Navigation.cs`, `Graph.cs`
- `src/Panopticon.Simulation/Navigation/PassabilitySnapshot.cs` (new)
- `test/Panopticon.Simulation.Tests/Navigation/AsyncPathRequestTests.cs`

## Acceptance criteria

- [ ] No `Node` allocation on submit in steady state.
- [ ] Snapshot semantics tests pass; async equals synchronous on the corpus.
- [ ] Anchor before/after numbers are recorded.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Navigation"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Drain order, frame bound, and cancellation semantics stay as they are.
- No gameplay caller moves to the queue (that is N4).

## On completion

- Set Status above to `Done` or `Blocked` with reason; set N1 `Done` in the track doc; set `WI-N2-01` and `WI-N4-01` to `Ready`.
- Tick N1-3 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
