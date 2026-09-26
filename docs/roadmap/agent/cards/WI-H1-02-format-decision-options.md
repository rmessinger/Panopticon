# Task Card `WI-H1-02`: Confirm settled H3 format choices against the audit

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

Check the settled H3 choices against the H1 audit: directory package with `manifest.json` plus one file per chunk per layer, using raw little-endian arrays and JSON metadata.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`.
- `docs/roadmap/deep-dives/b4a-larger-maps.md` chunk contract.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1a and §6.1.
- `docs/roadmap/track-h-storage-formats.md` H1-H3.

## Steps

1. Add requirements to the H1/H2 deep dive.
2. Record the settled H3 container and chunk layer encoding.
3. Compare those choices against the audit findings.
4. Raise `Blocked` and a Decision request only if the audit contradicts the settled choices.

## Files likely to change

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`

## Acceptance criteria

- [ ] Requirements trace to B4a, B4b, H2, H3, and H6 consumers.
- [ ] Settled choices are recorded as confirmed unless the audit contradicts them.
- [ ] Any contradiction is written as a specific `Blocked` reason.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/h1-h2-map-storage.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

Do not add new container options unless the audit proves a contradiction.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
