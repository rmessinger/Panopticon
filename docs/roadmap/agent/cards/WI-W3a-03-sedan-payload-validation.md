# Task Card `WI-W3a-03`: Validate the Sedan payload

| Field | Value |
|---|---|
| Card ID | `WI-W3a-03` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-02`, `WI-B4b-18` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-02` and `WI-B4b-18` are Done and the named payload types are reconciled.

Add Sedan-specific validation for the B4b generated payload contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §1, §3, §4, and §12.
- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5-§6.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §5.

## Steps

1. Add a Sedan payload validation profile for bounds, CRS, scale, vertical datum, and chunk layout.
2. Require `NGF-IGN69`, EPSG:32631, and `metersPerGameUnit` 0.3048.
3. Require generated layers for roads, rail, rivers, villages, woods, `bridge`, `pontoon`, `fortress`, `inundation`, and `border`.
4. Add tests for wrong CRS, wrong scale, wrong datum, out-of-bounds payloads, and missing W3a layers.

## Files likely to change

- Western Front payload validation files
- Western Front tests

## Acceptance criteria

- [ ] Validation rejects wrong CRS, scale, datum, or bounds.
- [ ] Validation rejects payloads missing required W3a layers.
- [ ] Validation accepts a minimal synthetic Sedan payload with all required metadata.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SedanPayloadValidation"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No operator source data or generated payload is committed.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
