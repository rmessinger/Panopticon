# Task Card `WI-W4-01`: Define the W4 matrix axis contract

| Field | Value |
|---|---|
| Card ID | `WI-W4-01` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-15`, W2 evidence cards |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-A5-15` and W2 evidence cards are Done.

Create the declarative W4 matrix with 216 cells and 64 seeds per cell.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §1-§2.
- `docs/roadmap/deep-dives/a5-visualization.md` §3.3.

## Steps

1. Add a W4 matrix definition fixture or generator.
2. Encode the five axes and values exactly.
3. Validate that matrix expansion yields 216 cells before seed expansion and 13824 runs after seeds.
4. Add constraints only if needed for A5 syntax.

## Files likely to change

- W4 matrix fixture or generator
- A5 matrix tests

## Acceptance criteria

- [ ] Matrix expands to exactly 216 cells and 13824 seeded runs.
- [ ] Each run records all axis values.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~W4Matrix"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No policy or model changes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
