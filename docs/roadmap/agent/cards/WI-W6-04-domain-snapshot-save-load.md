# Task Card `WI-W6-04`: Save and load domain snapshot plus host save

| Field | Value |
|---|---|
| Card ID | `WI-W6-04` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W6-01`, `WI-A4-44`, W2 tactical cards |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-01`, `WI-A4-44`, and W2 tactical cards are Done.

Implement the W6 save/load coordinator that pairs a domain-state snapshot with a host save.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §3.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §5.

## Steps

1. Add save manifest with frame, theater time, schema version, active regions, and object-key mapping.
2. Write or select the domain-state snapshot for the save frame.
3. Call the existing host save path for active host objects.
4. On load, validate manifest agreement before restoring.
5. Add mismatch tests.

## Files likely to change

- Western Front save coordinator files
- Tests

## Acceptance criteria

- [ ] Frame, theater time, schema, active regions, and object-key mapping mismatches fail load.
- [ ] Successful load reconstructs domain state and host active objects.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontSaveLoad"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not persist all domain state through `StatePersister`.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
