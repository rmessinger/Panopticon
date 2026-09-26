# Task Card `<CARD-ID>`: <short title>

> Copy this file into [`cards/`](cards/) named `WI-<PHASE>-<NN>-<slug>.md`
> (e.g. `WI-P0-01-catalog-replaysketch-vocabulary.md`). Fill every field. Delete this quote block and
> any guidance in angle brackets. Keep the card small enough for a low-effort model. If it
> needs deep design reasoning, split it or escalate to a roadmap deep dive.

| Field | Value |
|---|---|
| Card ID | `WI-<PHASE>-<NN>` (e.g. `WI-P0-01`) |
| Roadmap phase | [`<phase>`](../track-p-playable-baseline.md): link to the exact phase row |
| Status | `Draft` \| `Ready` \| `In progress` \| `Done` \| `Blocked` |
| Type | `investigation` \| `pattern-replication` \| `mechanical-refactor` \| `test-addition` |
| Effort | `S` (suitable for a low-effort model). If it isn't S, it doesn't belong here |
| Prerequisites | other card ids that must be `Done` first, or `none` |
| Diagrams | link to the populated `Architecture Diagrams` section before a source-edit card is marked `Ready`; a read-only audit/design card may state that its deliverable creates or grounds the diagrams |

## Goal

<1–2 sentences. What concrete, verifiable outcome does this card produce? Avoid strategy: link the
roadmap phase for the "why".>

## Context to read (and only this)

<Exact files and line ranges. The agent should not need anything beyond these.>

- `path/to/file.cs:10-60`: <why>
- `path/to/other.cs`: <why>

## Steps

<Explicit, ordered, low-ambiguity. A cheap model should be able to follow these literally.>

1. …
2. …
3. …

## Files likely to change

<Exact list. Anything outside this is out of scope.>

- `path/to/file.cs`

## Acceptance criteria

<Concrete, checkable. Include the specific assertions, not just "it works".>

- [ ] <specific check>
- [ ] Standard validation passes (see [guide](README.md#standard-validation)). Omit for
      investigation-only cards.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

<For investigation cards, replace with the command(s) that produce the artifact, or "read-only".>

## Out of scope / guardrails

<What this card must NOT do. Where to stop. What to escalate.>

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the roadmap phase Status if this card completes it.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - <follow-up>
