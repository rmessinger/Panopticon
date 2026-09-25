# Task Card `WI-B6-03`: Design the boundary message format

| Field | Value |
|---|---|
| Card ID | `WI-B6-03` |
| Roadmap phase | [Track B · B6](../../track-b-scale.md): distributed simulation |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [B6 §9](../../deep-dives/b6-distributed-simulation.md#9-architecture-diagrams) |

## Goal

Define the shared boundary message schema used by B6-2 JSON and B6-6 compact binary transport.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b6-distributed-simulation.md` §4 and §6.
- `docs/roadmap/deep-dives/w5-distributed-regional-simulation.md` §4.

## Steps

1. Specify envelope fields: schema, frame, mode, source, target, payload type, and checksum or hash.
2. Specify payload groups for halo, handoff, effects, acknowledgements, and invariant summaries.
3. Define deterministic ordering rules.
4. Create JSON and compact-binary compatibility fixtures in the design artifact.

## Files likely to change

- B6 implementation plan or design artifact

## Acceptance criteria

- [ ] Schema supports JSON and compact binary encodings.
- [ ] Deterministic ordering is specified for every repeated field.

## Validation commands

```sh
python docs/roadmap/scripts/check_links.py docs/roadmap/deep-dives/b6-distributed-simulation.md || true
```

## Out of scope / guardrails

No transport implementation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
