# Task Card `WI-W3a-23`: Apply timeline orders

| Field | Value |
|---|---|
| Card ID | `WI-W3a-23` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-21`, `WI-W3a-22`, `WI-W1-06` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-21`, `WI-W3a-22`, and `WI-W1-06` are Done and policy interfaces are reconciled.

Issue historical orders from the simulated headquarters state and record applicability metrics.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §8.1-§8.2 and §13.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §4.1-§4.4.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §6.

## Steps

1. Add `TimelineOrderSource` that emits due orders from issuer headquarters positions.
2. Route orders through courier or telegraph communication links.
3. Extend recipient policies to classify delivered orders as `executed`, `adapted`, or `rejected` with reason.
4. Add metrics for `order-applicability` and `order-arrival-offset`.
5. Add tests for delivery, adaptation, rejection, and metric recording.

## Files likely to change

- Western Front timeline order source files
- Western Front command policy files
- Western Front metrics tests

## Acceptance criteria

- [ ] Orders issue at reported local mean time from simulated headquarters positions.
- [ ] Recipients classify each delivered order with a reason where needed.
- [ ] Metrics include order applicability and arrival offset.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TimelineOrderSource"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No forced historical outcome and no capitulation order handling.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
