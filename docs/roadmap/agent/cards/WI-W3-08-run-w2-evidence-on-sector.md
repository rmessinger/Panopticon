# Task Card `WI-W3-08`: Run W2 evidence on generated sector

| Field | Value |
|---|---|
| Card ID | `WI-W3-08` |
| Roadmap phase | [Track W · W3](../../track-w-western-front.md): geospatial sector |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W3-07`, W2 evidence cards |
| Diagrams | [W3 §6](../../deep-dives/w3-geospatial-sector.md#6-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3-07` and W2 evidence cards are Done.

Run the W2 promotion and demotion evidence against generated cells.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3-geospatial-sector.md` §4.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §8.

## Steps

1. Choose one contested generated region pair.
2. Promote tactical soldiers on generated cells.
3. Run the all-tactical reference and mixed activation arms.
4. Reuse W2 conservation and transition assertions.

## Files likely to change

- Western Front evidence tests

## Acceptance criteria

- [ ] W2 transition evidence passes on generated topology.
- [ ] The all-tactical reference uses the same generated cell bounds as the mixed run.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneratedSectorW2Evidence"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No visual rendering changes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
