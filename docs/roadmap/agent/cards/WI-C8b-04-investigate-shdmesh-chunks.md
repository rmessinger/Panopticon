# Task Card `WI-C8b-04`: Investigate the `SHDMESH` chunk family

| Field | Value |
|---|---|
| Card ID | `WI-C8b-04` |
| Roadmap phase | [Track C · C8b](../../track-c-external-assets.md): BFME-family static meshes |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C8a-02` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Decode enough of the top-level `0x0B00` chunk in `gbmtwalld`, `gbmtwalldramp`, and `gbmtwalle` (BFME and BFME2) to decide whether C8 imports it or keeps a permanent diagnostic.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` section 4.
- `src/Panopticon.FileFormats.W3d/W3dUnknownChunk.cs` from `WI-C8a-02`: raw bytes access.
- `src/Panopticon.FileFormats.W3d/W3dMesh.cs`, `W3dMeshHeader3.cs`: comparison layouts.

## Steps

1. Dump the sub-chunk tree of each file's `0x0B00` chunk from `W3dUnknownChunk.Data`. Observed in `gbmtwalldramp` (BFME2): `0x0B01` name (`GBMTWALLDRAMP.GBMTWALLDRAMP`), `0x0B02` 80-byte header, `0x0B03`, container `0x0B20` holding `0x0B21`, container `0x0B40` (`0x0B41`, container `0x0B42` with `0x16490430` and `0x16490450`), then `0x0B43`, `0x0B44`, `0x0B47`, `0x0B49`, `0x0B4A`, `0x0B4B`, `0x0B45`.
2. Match sizes against the header's vertex and triangle counts to identify positions, normals, UVs, indices, and the material reference.
3. Check whether `gbmtwalleramp` (no `0x0B00`) is the same wall built from ordinary meshes, and whether any map or INI references the three `SHDMESH` models (names only).
4. Record the layout and a decision in deep dive section 4: import as an `ImportedMesh` (add follow-up card `WI-C8b-06`) or keep the `shdmesh-render-object` manifest entries as permanent.

## Files likely to change

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md`
- `docs/roadmap/agent/cards/WI-C8b-06-*.md` (new, only if importing)

## Acceptance criteria

- [ ] Section 4 records the sub-chunk layout with field sizes and the decision.
- [ ] A follow-up card exists if and only if the decision is to import.

## Validation commands

```sh
# Documentation only. Scratch tools stay outside the repository.
```

## Out of scope / guardrails

- No source edits. Use a throwaway reader outside the repository.
- Do not commit game files or extracted bytes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C8b-03` is done, tick C8b in `docs/roadmap/implementation-checklist.md` and set C8b `Done` in the track doc (unless a follow-up card is open).
- Append a result line to `.exchange`.
