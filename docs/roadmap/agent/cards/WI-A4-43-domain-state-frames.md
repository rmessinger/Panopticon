# Task Card `WI-A4-43`: Persist, diff, and reconstruct domain state frames

| Field | Value |
|---|---|
| Card ID | `WI-A4-43` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-4 domain state projection |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-42` |
| Diagrams | [A4-4 §8](../../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams) |

## Goal

Make state records and frames persistable, add delta diff/apply and reconstruction, and add the
schema-driven conservation check.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §3-§4.
- `src/Panopticon.Simulation/Recording/WorldStateSnapshot.cs`: `WorldObjectState.Persist`,
  `WorldStateDelta`, and `WorldStateArtifact.Reconstruct` patterns to mirror.
- `src/Panopticon.Simulation/Simulation/State/`: contracts from `WI-A4-42`.

## Steps

1. Implement `IPersistableObject` on `DomainStateRecord` and `DomainStateFrame`; call
   `PersistVersion(1)` first; persist arrays as count plus elements.
2. Add `DomainStateDelta` (`Upserted`, `Removed`) with `Diff` (ordinal key comparison; a record is
   upserted when any value, reference, owner, or kind differs) and `Apply`.
3. Add `DomainStateReconstruction.Reconstruct(snapshots, deltas, frame)` mirroring
   `WorldStateArtifact.Reconstruct`; output records sorted by ordinal key.
4. Add `DomainConservation.Balances` and `Residuals` per §4.
5. Add tests: persist round trip through `StateWriter`/`StateReader` on a `MemoryStream`; diff then
   apply equals the target frame; reconstruction at a snapshot frame and between snapshots;
   residuals empty after a holding-to-holding move and non-empty after an unbalanced change.

## Files likely to change

- `src/Panopticon.Simulation/Simulation/State/*.cs`
- `test/Panopticon.Simulation.Tests/Simulation/DomainStateFrameTests.cs`

## Acceptance criteria

- [ ] Round trip preserves every key, kind, owner, integer, real, and reference.
- [ ] `Diff` followed by `Apply` reproduces the current frame exactly.
- [ ] Reconstruction before the first snapshot throws `ArgumentOutOfRangeException`.
- [ ] `Residuals` reports a moved amount as zero and an unbalanced edit as the exact difference per family and owner.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~DomainStateFrame"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No recorder, artifact file, or `RunRecording` change.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-44`.
