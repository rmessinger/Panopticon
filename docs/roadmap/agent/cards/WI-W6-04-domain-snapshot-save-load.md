# Task Card `WI-W6-04`: Save and load campaign checkpoints

| Field | Value |
|---|---|
| Card ID | `WI-W6-04` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W6-01`, `WI-A4-44`, `WI-A1-16`, `WI-W3a-31`, W2 tactical cards |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-01`, `WI-A4-44`, `WI-A1-16`, `WI-W3a-31`, and W2 tactical cards
are Done.

Implement the W6 save/load coordinator over the A1-6 engine checkpoint, which holds the host
save and the domain-state snapshot in one file.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §3.
- `docs/roadmap/deep-dives/a4-4-domain-state-projection.md` §5.
- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.3 and §6.1.

## Steps

1. Add save manifest with frame, theater time, schema version, active regions, and object-key mapping.
2. Write an A1-6 checkpoint at the save frame, or select the campaign-boundary checkpoint when the
   save frame is one.
3. On load, validate manifest agreement with the checkpoint engine and domain sections, then
   restore through the A1-6 host restore.
4. Add mismatch tests.

## Files likely to change

- Western Front save coordinator files
- Tests

## Acceptance criteria

- [ ] Frame, theater time, schema, active regions, and object-key mapping mismatches fail load.
- [ ] Successful load reconstructs domain state and host active objects.
- [ ] A loaded run steps to the next operational boundary and satisfies conservation.
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
