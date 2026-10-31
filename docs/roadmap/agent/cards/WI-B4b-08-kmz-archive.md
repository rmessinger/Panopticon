# Task Card `WI-B4b-08`: Safe KMZ archive reader

| Field | Value |
|---|---|
| Card ID | `WI-B4b-08` |
| Roadmap phase | [Track R · R1](../../track-r-real-world-terrain.md): B4b-3 local source readers |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-07` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Implement `KmzArchiveReader : IVectorSourceReader` that safely opens a KMZ and delegates to `KmlReader`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §7 (Archive stage, limits).
- `src/Panopticon.Geospatial/Readers/KmlReader.cs`.

## Steps

1. Open with `ZipArchive` read-only; check entry count, per-entry and total expanded size, and compression ratio before extraction.
2. Reject entries with rooted paths, `..` segments, or backslashes; never extract to disk.
3. Select `doc.kml` or else the first `.kml` entry in archive order; stream it into `KmlReader`.
4. Record archive entry name in feature provenance.
5. Add `test/Panopticon.Geospatial.Tests/Readers/KmzArchiveReaderTests.cs` building zips in memory.

## Files likely to change

- `src/Panopticon.Geospatial/Readers/KmzArchiveReader.cs`
- `test/Panopticon.Geospatial.Tests/Readers/`

## Acceptance criteria

- [ ] A zip bomb fixture (ratio over the limit) fails with an `Archive` error without full expansion.
- [ ] A `../evil.kml` entry is rejected.
- [ ] A KMZ wrapping the WI-B4b-07 fixture yields identical features.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~KmzArchiveReader"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No archive resources beyond the KML.

## Implementation notes

ZIP64 and multi-disk archives are rejected with an Archive diagnostic. Central-directory counts are checked before `ZipArchive` materializes entries. Stream inputs must be seekable.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-18` when their other prerequisites are `Done`.
