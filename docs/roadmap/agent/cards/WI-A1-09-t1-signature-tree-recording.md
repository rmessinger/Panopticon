# Task Card `WI-A1-09`: T1 hierarchical signature-tree recording

| Field | Value |
|---|---|
| Card ID | `WI-A1-09` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, A1-5 remainder |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A1-07` (the per-tick hook and T0 format it builds on), plus the A5-4 bucketing design |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Emit the T1 tier of the run outputs: a per-frame hierarchical state signature tree (world to
partition to entity bucket) suitable for tolerance-aware divergence screening, per
[a5-visualization.md §5](../../deep-dives/a5-visualization.md#5-run-comparison--divergence-localization).

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md` §5: the tier definitions.
- `docs/roadmap/deep-dives/a2-reproducibility.md` §2.2: signatures are diagnostic, not parity gates.
- `src/OpenSage.Game/RunOutput.cs`: the T0 artifact this extends.
- `src/OpenSage.Game/SimulationHost.cs`: the per-tick hook.

## Steps

Unblocked: [a5-visualization.md §5.2](../../deep-dives/a5-visualization.md#52-design-points)
specifies the bucketing (stable keys from the domain topology: spatial cells for the RTS grid
domain, stable entity ids) and the tolerance shape (per-quantity ε), which
was the blocker this card was waiting on. The comparator UI stays A5-4.

## Files likely to change

- `src/OpenSage.Game/` (T1 recording types)
- `src/OpenSage.Game.Tests/` (round-trip test)

## Acceptance criteria

- [ ] A headless run writes a T1 signature tree whose reload and drill-down levels are testable.
- [ ] Tree size stays bounded per frame.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add a state-parity gate; signatures are diagnostic.
- No visualization. That is A5-3.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
