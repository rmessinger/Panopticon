# Task Card `WI-A1-10`: T2 triggered detail capture with a bounded ring buffer

| Field | Value |
|---|---|
| Card ID | `WI-A1-10` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, A1-5 remainder |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-09` (the T1 signature tree trips the capture) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Emit the T2 tier: when the T1 signature tree exceeds its tolerance, capture detailed
per-entity and per-system deltas for the affected frame and region window into a bounded ring
buffer, live during the run (a re-run will not reproduce the divergence).

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §5: the tier definitions and the capture-on-trip
  rationale.
- `src/OpenSage.Game/RunOutput.cs` and the T1 recording produced by `WI-A1-09`.

## Steps

Unblocked with `WI-A1-09` done: the T1 trees trip the capture. The ring buffer is bounded by
frame window depth (default 30) and total windows (default 8), with a one-ring-depth cooldown
between trips and pre-marked windows via `WithMarkedWindow`.

## Files likely to change

- `src/OpenSage.Game/` (T2 capture types)
- `src/OpenSage.Game.Tests/` (trigger + bound test)

## Acceptance criteria

- [ ] A tolerance trip produces a bounded T2 capture; non-tripping runs produce none.
- [ ] Memory is bounded regardless of run length.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not persist the full world state; T2 is a windowed detail delta, not a snapshot.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
