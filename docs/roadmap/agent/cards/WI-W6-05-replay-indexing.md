# Task Card `WI-W6-05`: Index long campaign artifacts for replay

| Field | Value |
|---|---|
| Card ID | `WI-W6-05` |
| Roadmap phase | [Track W · W6](../../track-w-western-front.md): long-run campaign experiment |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W6-01`, `WI-A5-08`, `WI-A5-18`, `WI-A1-20`, `WI-A1-23` |
| Diagrams | [W6 §6](../../deep-dives/w6-long-run-campaign.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W6-01`, `WI-A5-08`, `WI-A5-18`, `WI-A1-20`, and `WI-A1-23` are
Done.

Add replay indexing for long Western Front operational recordings.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w6-long-run-campaign.md` §3-§4.
- `docs/roadmap/deep-dives/a5-visualization.md` §4.
- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §5.2 and §6.

## Steps

1. Extend the operational-stream footer index with theater time.
2. Store nearest checkpoint and domain-snapshot offsets and summary counts.
3. Use the index in A5 scrubbing; route tactical-frame requests to the A1-6 reconstructor.
4. Test seek performance on a generated large synthetic artifact.

## Files likely to change

- A5 indexed reader files
- Western Front replay tests

## Acceptance criteria

- [ ] A5 seeks early, middle, and late frames without scanning the whole artifact.
- [ ] A tactical frame request re-simulates at most one operational interval once the cache is warm.
- [ ] Index metadata records schema and campaign key.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontReplayIndex"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Granular frames come only from verified A1-6 reconstruction; no unverified rerun-based replay.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
