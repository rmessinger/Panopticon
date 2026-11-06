# Task Card `WI-H2-03`: Route the legacy map reader through the seam

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2-03` |
| Roadmap phase | [Track H · H2](../../track-h-storage-formats.md): interchange abstraction |
| Status | `Done` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-H2-02` |
| Diagrams | none |

## Goal

Add the reader-source seam interface and the legacy `MapFile` adapter that produces `MapInterchange` with the per-section dispositions from the [preservation table](../../deep-dives/h1-h2-map-storage.md): typed homes for `HeightMapData`, `ObjectsList`, `WaypointsList`, and `GlobalVersion`; retained-opaque sections held as `RetainedSection`; `SidesList` below version 2 rejected with a typed diagnostic. No behavior change: the existing `MapFileTests` round-trips stay green and the existing load path behavior is unchanged.

## Context to read (and only this)

- [H2 map interchange design](../../deep-dives/h1-h2-map-storage.md): Preservation per legacy section, Payload mapping, Units, frames, and quantities, and the load sequence under Scope and ownership.
- `src/Panopticon.Simulation/Data/Map/` reader and section classes from the H1 audit.
- `test/Panopticon.Simulation.Tests/Data/Map/MapFileTests.cs`.

## Steps

1. Define the reader-source seam interface that opens a map source, verifies source digests, and returns a validated `MapInterchange`.
2. Implement the legacy `MapFile` adapter with one disposition per audited section, following the preservation table in dispatch order.
3. Apply the stated adapter behavior: engine XY and Z read as `GameUnit` without conversion, legacy elevation scales, waypoint engine-XY positions, per-object dictionaries preserved on `PlacedObject`.
4. Keep the existing load path untouched and run the round-trip suite to prove no behavior change.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Maps/` (seam interface and legacy adapter).
- A new test file under `test/Panopticon.Simulation.Tests`.

## Acceptance criteria

- [x] Each audited section takes exactly one disposition from the preservation table: typed, retained opaque with name, version, bytes, and digest, or rejected with a typed diagnostic.
- [x] `HeightMapData`, `ObjectsList`, `WaypointsList`, and `GlobalVersion` land in their typed homes, and `SidesList` below version 2 fails import without partial publication.
- [x] Source values are preserved as read; conversion happens only where the design assigns it to the adapter.
- [x] `test/Panopticon.Simulation.Tests/Data/Map/MapFileTests.cs` round-trips stay green.
- [x] The existing load path behavior is unchanged.

## Validation commands

```sh
dotnet build src/Panopticon.Simulation -v minimal
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~MapFile"
# Before PR: MCP test run per docs/roadmap/agent guidance in AGENTS.md.
```

## Out of scope / guardrails

No runtime materialization (WI-H2-04), no H3 package reader or writer, no change to load behavior, and no write path to the legacy format.

## On completion

- Result: 2026-11-06 done: added the `IMapReader` seam and the `LegacyMapReader` adapter under `src/Panopticon.Simulation/Content/Importing/` producing a validated `MapInterchange` (declared-lattice chunking with partial edge chunks for real map sizes, heights scaled by the legacy `VerticalScale` with edge-clamped aprons, positional `obj-`/`wp-` stable ids because legacy names are not unique, retained-opaque sections with per-section bytes and SHA-256 including `WaypointsList` path records, `SidesList` below version 2 rejected via typed diagnostic, `MapImportException` publishes no partial content). Validation: focused suites 69/69 with the corpus installed and 69/69 with corpus tests vacuously skipped, `dotnet build` 0 errors with pre-existing warnings only, `dotnet format --verify-no-changes` clean on both projects, brace and MapFile-boundary checks clean, corpus evidence: deepdivide 660x460 lattice to 6x4 chunks with 584 objects, Alpine Assault validates with 251 positional waypoint ids and 9 duplicate names, 3 validating corpus models. MCP run `20261106T195932Z-313009dc` on `track-h-h2-3-legacy-reader-seam` 4825 passed / 4843 total / 10 known failures (TF-01..TF-10) / 8 not executed / 0 new failures / 0 suspected deviations; two earlier same-ref attempts are accounted for: one transient unrelated Navigation timeout that did not reproduce and one environmental test-host Vulkan crash, artifacts preserved. Follow-up: WI-H2-04 runtime bridge and parity.
- Set Status above to `Done` or `Blocked`.
- Add a `Result:` line as the first bullet under `## On completion`: date, what changed, validation outcome, follow-up cards.
