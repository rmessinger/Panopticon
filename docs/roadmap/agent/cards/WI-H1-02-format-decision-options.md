# Task Card `WI-H1-02`: Map format requirements and options

| Field | Value |
|---|---|
| Card ID | `WI-H1-02` |
| Roadmap phase | [Track H · H1](../../track-h-storage-formats.md): format requirements |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-H1-01` |
| Diagrams | none |

## Goal

Add requirements and container options to the H1/H2 deep dive and record open Decisions for the operator.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`.
- `docs/roadmap/deep-dives/b4a-larger-maps.md` chunk contract.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §6.1 payload.

## Steps

1. Add a requirements section: chunked terrain, streaming reads, schema evolution, provenance metadata, deterministic bytes, legacy import.
2. Compare at least three container options (directory of chunk files, single indexed binary, zip package) against the requirements in a table.
3. Record each unresolved choice under Open Questions in `open-questions.md` with options and a recommendation.

## Files likely to change

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`
- `docs/roadmap/open-questions.md`

## Acceptance criteria

- [ ] Requirements trace to B4a, B4b, and H6 consumers.
- [ ] Each Open Question has options and a recommendation.
- [ ] Standard validation passes.

## Validation commands

```sh
echo '{"args":["--no-sandbox"]}' > /tmp/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p /tmp/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o /tmp/mm/card.svg
```

## Out of scope / guardrails

No implementation.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-H2-01`.
