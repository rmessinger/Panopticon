# Task Card `WI-W4-03`: Implement the information audit

| Field | Value |
|---|---|
| Card ID | `WI-W4-03` |
| Roadmap phase | [Track W · W4](../../track-w-western-front.md): command and logistics experiment |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | W1 command cards |
| Diagrams | [W4 §6](../../deep-dives/w4-command-logistics-experiment.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once W1 command cards are Done and `EchelonView` exists.

Add runtime audit coverage that proves policies read only echelon-local information.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w4-command-logistics-experiment.md` §4.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §3.

## Steps

1. Add provenance markers to values exposed by `EchelonView`.
2. Record value reads during each policy call.
3. Fail a run when a policy reads unavailable global or future information.
4. Add one compliant reference policy test and one intentional violation test.

## Files likely to change

- Western Front command/audit files
- Tests

## Acceptance criteria

- [ ] Compliant policies pass with read provenance recorded.
- [ ] Intentional omniscient read fails with policy id and source detail.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~InformationAudit"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No learned-policy implementation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
