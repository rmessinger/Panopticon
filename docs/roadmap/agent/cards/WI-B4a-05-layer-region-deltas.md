# Task Card `WI-B4a-05`: Apply region deltas to terrain layers

| Field | Value |
|---|---|
| Card ID | `WI-B4a-05` |
| Roadmap phase | [Track B · B4a](../../track-b-scale.md): B4a-2 region-delta editing |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4a-03` |
| Diagrams | [B4a diagrams](../../deep-dives/b4a-larger-maps.md#architecture-diagrams) |

## Goal

Draft: flip to Ready once the prerequisite cards are Done and the named types are reconciled. Extend region deltas beyond height to blend/material and passability layers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4a-larger-maps.md`.
- `docs/roadmap/implementation-checklist.md` A5-6 and B4a items.

## Steps

1. Read the B4a deep dive section for this sub-phase.
2. Implement or document only the named slice.
3. Add focused tests or file:line audit evidence.
4. Leave dependent streaming, H3, or B1 work to its own card.

## Files likely to change

- `docs/roadmap/deep-dives/b4a-larger-maps.md`
- Relevant WorldBuilder or Terrain files named in the card steps

## Acceptance criteria

- [ ] The named slice is complete and covered by tests or audit evidence.
- [ ] Existing normal-map behavior is preserved.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~LayerRegionDelta"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No unrelated WorldBuilder or storage-format changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
