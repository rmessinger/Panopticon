# Task Card `WI-B4a-07`: Design CDLOD terrain streaming seams

| Field | Value |
|---|---|
| Card ID | `WI-B4a-07` |
| Roadmap phase | [Track B · B4a](../../track-b-scale.md): B4a-4 terrain streaming and CDLOD |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-B4a-01` |
| Diagrams | [B4a diagrams](../../deep-dives/b4a-larger-maps.md#architecture-diagrams) |

## Goal

Draft: flip to Ready once the prerequisite cards are Done and the named types are reconciled. Write a design slice for CDLOD quadtree selection, continuous morph, and terrain patch streaming.

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

- [ ] The design names planned types and integration points.
- [ ] Current integration points have file:line references.
- [ ] The diagram renders.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/b4a-larger-maps.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No unrelated WorldBuilder or storage-format changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
