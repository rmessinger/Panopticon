# Task Card `WI-B7-02`: Define replicated fidelity policy inputs

| Field | Value |
|---|---|
| Card ID | `WI-B7-02` |
| Roadmap phase | [Track B · B7](../../track-b-scale.md): variable simulation fidelity |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B7-01` |
| Diagrams | Draft: flip to Ready once prerequisite cards are Done and the named types are reconciled. |

## Goal

Define `IFidelityPolicy` and `FidelityPolicyContext` so shared runs classify only from replicated simulation state.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b7-variable-fidelity.md`: owning B7 design.
- `src/Panopticon.Simulation/Logic/GameLogic.cs`: live object enumeration and creation.
- `src/Panopticon.Simulation/IGame.cs:149-153`: object destruction event.

## Steps

1. Add the policy interface and context record.
2. Include object id, owner, kind, position, health, activity, local density, topology key, current fidelity, and shard hint.
3. Add validation that camera or view inputs are absent from shared-run policies.
4. Add a full-fidelity reference policy.

## Files likely to change

- `src/Panopticon.Simulation/Fidelity/`
- `test/Panopticon.Simulation.Tests/Fidelity/`

## Acceptance criteria

- [ ] Context fields are replicated simulation state.
- [ ] Full-fidelity policy classifies every object as `Full`.
- [ ] Shared-run policy construction rejects camera/view dependencies.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No view-driven policy implementation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Append a result line to repo-root `.exchange`.
