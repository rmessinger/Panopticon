# Task Card `WI-B4b-07`: Bounded KML reader

| Field | Value |
|---|---|
| Card ID | `WI-B4b-07` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-3 local source readers |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-03` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement `KmlReader : IVectorSourceReader` for the bounded KML subset in §3.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §3 (KML subset), §7 (limits, Parse stage).
- `src/Panopticon.Geospatial/Interchange/`.

## Steps

1. Parse with `XmlReader` (`DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` from limits).
2. Support `Point`, `LineString`, `Polygon` (outer and inner rings), and `MultiGeometry` inside `Placemark`, recursing `Document` and `Folder`.
3. Stable feature id: placemark `id` attribute, else folder path plus placemark name plus document-order index.
4. Attributes: `name`, folder path, `ExtendedData/Data` and `SchemaData/SimpleData` values, `styleUrl`, and `altitudeMode`; altitude is kept as an attribute and geometry is always clamp-to-terrain.
5. Unsupported elements (`NetworkLink`, `Model`, `GroundOverlay` with `href`, `gx:Tour`, `Update`) produce `Parse` warnings and are skipped; malformed coordinates produce errors.
6. Enforce feature and coordinate count limits.
7. Add `test/Panopticon.Geospatial.Tests/Readers/KmlReaderTests.cs` with inline KML fixtures covering every geometry, nested folders, and each unsupported element.

## Files likely to change

- `src/Panopticon.Geospatial/Readers/KmlReader.cs`
- `src/Panopticon.Geospatial/GeoDiagnosticCodes.cs`
- `test/Panopticon.Geospatial.Tests/Readers/`

## Acceptance criteria

- [ ] A DTD-bearing document is rejected without resolving entities.
- [ ] Nested `MultiGeometry` flattens to the expected geometry tree with stable ids.
- [ ] Exceeding the coordinate limit fails with a `Parse` error before completing.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~KmlReader"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No KMZ, no styles to materials.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-08` and `WI-B4b-15` when their other prerequisites are `Done`.
