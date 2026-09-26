# Task Card `WI-B4a-02`: Audit WorldBuilder large-map limits

| Field | Value |
|---|---|
| Card ID | `WI-B4a-02` |
| Roadmap phase | [Track B · B4a](../../track-b-scale.md): B4a-1 baseline and limits audit |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | A5-6 checklist item |
| Diagrams | [B4a diagrams](../../deep-dives/b4a-larger-maps.md#architecture-diagrams) |

## Goal

Measure and document the first practical limits in WorldBuilder map load, edit, render, save, and play paths.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4a-larger-maps.md`.
- `docs/roadmap/implementation-checklist.md` A5-6 and B4a items.
- `src/Panopticon.Tools.WorldBuilder.Core/EditorMap.cs`, `HeightBrushTool.cs`, `GameViewportRenderer.cs`.
- `src/Panopticon.Simulation/Terrain/Terrain.cs`.

## Steps

1. Confirm whether A5-6 baseline acceptance is complete.
2. Measure or specify normal-map and large-map procedures.
3. Record load time, save time, height-stroke latency, undo memory, preview render state, and engine-play outcome.
4. Add results or blocked evidence to the B4a deep dive.

## Files likely to change

- `docs/roadmap/deep-dives/b4a-larger-maps.md`

## Acceptance criteria

- [ ] The audit identifies the first failing constraint among C1 through C6.
- [ ] The audit records whether A5-6 is satisfied.
- [ ] The diagram renders.

## Validation commands

```sh
mkdir -p docs/roadmap/.validation-b1h
echo '{"args":["--no-sandbox"]}' > docs/roadmap/.validation-b1h/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p docs/roadmap/.validation-b1h/pp.json -i docs/roadmap/deep-dives/b4a-larger-maps.md -o docs/roadmap/.validation-b1h/card.svg
```

## Out of scope / guardrails

No code changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
