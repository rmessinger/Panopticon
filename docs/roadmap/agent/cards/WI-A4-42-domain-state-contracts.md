# Task Card `WI-A4-42`: Add domain state projection contracts

| Field | Value |
|---|---|
| Card ID | `WI-A4-42` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-4 domain state projection |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [A4-4 §8](../../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams) |

## Goal

Add the A4-4 schema, record, frame, and projection contracts with schema validation, without
changing any existing recording behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §2-§3: contract shapes and validation rules.
- `src/Panopticon.Simulation/Simulation/ISimulationDomainModel.cs`: sibling seam and doc-comment style.
- `src/Panopticon.Simulation/Simulation/Types/DomainMetrics.cs`: record/type file conventions.

## Steps

1. Create `src/Panopticon.Simulation/Simulation/State/` with `DomainStateSchema.cs` (enums
   `DomainQuantityType`, `DomainConservationRole`; records `DomainStateField`, `DomainRecordKind`,
   `DomainStateSchema`), `DomainStateRecord.cs`, `DomainStateFrame.cs`, and
   `IDomainStateProjection.cs`, all in namespace `Panopticon.Simulation`.
2. Implement `DomainStateSchema.Validate()` throwing `InvalidOperationException` naming the kind
   and field for each rule in §3.
3. Add `DomainRecordKind.IntegerFieldCount` and `RealFieldCount` helpers and
   `DomainStateSchema.FindKind(name)`.
4. Leave `Persist` methods for `WI-A4-43`; do not implement `IPersistableObject` yet.
5. Add `test/Panopticon.Simulation.Tests/Simulation/DomainStateSchemaTests.cs` covering one valid
   schema and each rejection rule.

## Files likely to change

- New files under `src/Panopticon.Simulation/Simulation/State/`
- `test/Panopticon.Simulation.Tests/Simulation/DomainStateSchemaTests.cs`

## Acceptance criteria

- [ ] A schema with a `Real` field carrying `Holding` fails validation with a message naming the field.
- [ ] Duplicate kind names, duplicate field names within a kind, empty unit, empty layer, missing family on a conserved field, and a family on a `None` field each fail validation.
- [ ] A valid schema passes and reports correct integer and real field counts.
- [ ] No existing file under `Recording/` or `RunOutput.cs` changes.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~DomainStateSchema"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No persistence, recorder, run-output change, or adapter implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A4-43` and `WI-W0-02`.
