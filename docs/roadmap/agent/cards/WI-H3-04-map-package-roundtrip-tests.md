# Task Card `WI-H3-04`: Add map package round-trip tests

| Field | Value |
|---|---|
| Card ID | `WI-H3-04` |
| Roadmap phase | [Track H · H3](../../track-h-storage-formats.md): new map storage format |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-H3-01`, `WI-H3-02`, `WI-H3-03` |
| Diagrams | H3 diagrams to be created by `WI-H2-01` or `WI-H3-01` |

## Goal

Draft: flip to Ready once the prerequisite cards are Done and the named types are reconciled. Lock H3 round-trip behavior for terrain, blend/material, masks, and placed objects.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h1-h2-map-storage.md`.
- `docs/roadmap/deep-dives/b4a-larger-maps.md` chunk contract.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.1a and §6.1.

## Steps

1. Read the H2/H3 contracts from the deep dive.
2. Implement or specify only this H3 slice.
3. Add fixtures or tests for valid and invalid packages.
4. Keep runtime residency in B4a, not H3.

## Files likely to change

- H3 map package source or docs
- H3 tests or fixtures

## Acceptance criteria

- [ ] The H3 slice matches the settled directory package format.
- [ ] Raw chunk arrays are little-endian where applicable.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MapPackageRoundTrip"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No runtime chunk residency; B4a owns it.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
