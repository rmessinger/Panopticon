# Task Card `WI-A4-05`: Audit simulation-domain assumptions

| Field | Value |
|---|---|
| Card ID | `WI-A4-05` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-1 Simulation Domain Model seam |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Not applicable: read-only audit that supplies evidence for `WI-A4-06`. |

## Goal

Map every RTS-specific assumption in the host, entity runtime, topology, interaction authority,
recording, metrics, and fidelity hooks before defining the domain seam.

## Context to read (and only this)

- `docs/roadmap/track-a-foundations.md`: A4-1 domain responsibilities.
- `docs/roadmap/seams.md`: canonical Simulation Domain Model seam.
- `docs/roadmap/deep-dives/b6-distributed-simulation.md`: topology and authority consumers.
- `src/OpenSage.Game/SimulationHost.cs`, `Logic/GameLogic.cs`, `Logic/Object/GameObject.cs`.
- `src/OpenSage.Game/Logic/PartitionCellManager.cs`, `Recording/`, and `RunOutput.cs`.

## Steps

1. Inventory assumptions under entity schema, topology, locality, interaction authority, metrics,
   recording buckets, visualization projection, and fidelity.
2. For each assumption, cite the owning type and classify it as domain contract, RTS adapter
   implementation, or host concern.
3. Identify the smallest transport/graph behavior needed to exercise every contract.
4. Write the findings below the divider in
   `docs/roadmap/implementation/track-a-a4-simulation-domain-model.md`.
5. Add the plan to `docs/roadmap/implementation/README.md`.

## Files likely to change

- `docs/roadmap/implementation/track-a-a4-simulation-domain-model.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every A4-1 responsibility has a cited current-code owner.
- [ ] Host concerns are separated from domain-specific behavior.
- [ ] The transport/graph validation scope covers graph topology, edge locality, owner authority,
      boundary metrics, and stable recording buckets.
- [ ] No source file changes.

## Validation commands

```sh
git status --short -- 'src/*'
```

## Out of scope / guardrails

Do not design interfaces or implement a second runtime. Record ambiguity for `WI-A4-06`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-06`.
