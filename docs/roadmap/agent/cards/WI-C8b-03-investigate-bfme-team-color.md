# Task Card `WI-C8b-03`: Investigate the BFME-family team-color source

| Field | Value |
|---|---|
| Card ID | `WI-C8b-03` |
| Roadmap phase | [Track C · C8b](../../track-c-external-assets.md): BFME-family static meshes |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C8b-02` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Find how BFME, BFME2, and RotWK mark team-colored surfaces, given that no `HOUSECOLOR`-prefixed mesh exists, and decide how the import exposes it.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 4.1 and 5.
- `src/Panopticon.Simulation/Content/Importing/W3d/W3dMeshConverter.cs:32`: Generals `hasHouseColor` rule.
- `src/Panopticon.Tools.TestHarness/SageW3dProofScenario.cs` from `WI-C8b-02`.

## Steps

1. For the eight proof models, list meshes, shader families, texture names, and any texture-name, mesh-name, or shader-property pattern that differs between team-colored and plain parts (compare with in-game reference screenshots or the INI `ObjectTint`/house-color fields, citing them by name only).
2. Check diffuse texture alpha for the `NormalMapped` proof meshes: report whether alpha is a mask (sparse, aligned with team-colored regions) or opacity.
3. Write the finding and a decision into deep dive section 6: either a mask source the importer sets `hasHouseColor` or a per-part flag from, or team color recorded as unsupported with a diagnostic.
4. If the decision requires code, add one follow-up card `WI-C8b-05` with steps and acceptance criteria; otherwise state that no card is needed.

## Files likely to change

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md`
- `docs/roadmap/agent/cards/WI-C8b-05-*.md` (new, only if needed)

## Acceptance criteria

- [ ] Section 6 records the team-color source and decision with evidence (model names, counts).
- [ ] A follow-up card exists if and only if the decision requires code.

## Validation commands

```sh
# Documentation only. Validate Mermaid if a diagram changes:
echo '{"args":["--no-sandbox"]}' > /tmp/pp.json
npx -y @mermaid-js/mermaid-cli@latest -p /tmp/pp.json -i docs/roadmap/deep-dives/c8-sage-family-w3d.md -o /tmp/c8.md
```

## Out of scope / guardrails

- No source edits.
- Do not commit game files, textures, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C8b-04` is done, tick C8b in `docs/roadmap/implementation-checklist.md` and set C8b `Done` in the track doc (unless `WI-C8b-05` exists and is open).
- If done, change `WI-C8c-04` to `Ready` when its other prerequisites are done.
- Append a result line to `.exchange`.
