# Task Card `WI-W3b-02`: Validate W3b Somme payload metadata

| Field | Value |
|---|---|
| Card ID | `WI-W3b-02` |
| Roadmap phase | [Track W · W3b](../../track-w-western-front.md): Somme sector |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W3b-01`, `WI-B4b-18` |
| Diagrams | [W3b §5](../../deep-dives/w3b-somme-sector.md#5-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3b-01` and `WI-B4b-18` are Done and payload types exist.

Add validation that rejects a generated payload that does not match the W3b sector contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3b-somme-sector.md` §1-§2.
- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §3.
- B4b payload reader types from `WI-B4b-18`.

## Steps

1. Add a `SommeSectorPayloadValidator` or equivalent service in the Western Front geospatial integration area.
2. Validate bounds, EPSG:32631, `metersPerGameUnit` 0.3048, vertical datum metadata, chunk count bounds, and required feature layers.
3. Add synthetic payload tests for one valid payload and focused invalid payloads.

## Files likely to change

- Western Front geospatial integration files
- Western Front tests

## Acceptance criteria

- [ ] Invalid CRS, scale, datum, missing front line, and missing transport layers fail with actionable diagnostics.
- [ ] A synthetic valid payload passes without requiring operator data.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SommeSectorPayload"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No raw data loading beyond synthetic fixtures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
