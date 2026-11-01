# R2 standalone payload contract

Scope: `WI-B4b-18`. Payload layout and generation semantics: [B4b deep dive §6.1](../deep-dives/b4b-geospatial-map-generation.md#61-standalone-payload-and-dev-preview). H2/H3 packaging remains out of scope.

## Entry points

Namespace `Panopticon.Geospatial.Pipeline`:

```csharp
GeoImportResult GeospatialImporter.Run(string manifestPath, string outputDirectory, CancellationToken cancellationToken = default);
GeoImportResult GeospatialImporter.Run(string manifestPath, string outputDirectory, GeoImportOptions options, CancellationToken cancellationToken = default);
```

`GeoImportOptions` retains force, limits, and a copied ordered list of staged validators. `GeoImportResult` exposes success, immutable diagnostics, optional committed payload and retained-backup paths, and an optional report. Absolute paths and exception details remain runtime values, not serialized content.

Namespace `Panopticon.Geospatial.Payload`: `GeneratedMapPayloadWriter`, `GeneratedMapPayloadReader`, and `IGeneratedMapPayloadReader`. The interface exposes immutable metadata and report, chunk reads, game-height sampling, and CPU RGBA sampling. Validators and preview conversion consume the interface. Public models contain no engine, TIFF, or ImageSharp types.

## Metadata and reports

Kind: `panopticon-geospatial-payload`. Schema version: `1`.

JSON uses camelCase properties, strict kebab-case named enums, finite numbers, deterministic property and list order, and ordinal dictionary-key order. Unknown members, duplicate members, integer enums, and unsupported versions fail. Explicit internal DTOs convert immutable interchange values; shared attribute dictionaries are not cloned once per generated feature.

`MaxPayloadMetadataBytes` defaults to 128 MiB, separately applied to each JSON file. Reads and writes are byte-bounded. Structural chunk, feature, coordinate, and palette limits apply before DTO collection allocation. Serialization streams rather than constructing an unbounded intermediate byte array.

`MaxSourceFileBytes` defaults to 4 GiB per encoded input source. Hashing enforces this bound before reader invocation. Manifest and KML hashing also enforce their smaller format-specific byte limits. Vector feature and coordinate limits apply to the combined import, not only individual readers.

Source provenance retains manifest IDs, roles, kinds, normalized relative paths, hashes, actual CRS, embedded and declared vertical datums, labels, attribution, and accuracy declarations. Rebinding vector source IDs preserves archive-entry and folder attributes. Contradictory known datums fail; no geoid conversion runs. Classification-value material rules with a source ID must name the classification source.

The report retains diagnostic DTOs, actual kernel counts and observed GSD/spacing ranges, fill statistics, generation summaries, ordered validation checks, and the exact payload JSON hash. Diagnostic paths are manifest-relative. Wall clocks, output paths, exception objects, and a report self-hash are excluded.

## Reader validation

`GeneratedMapPayloadReader.Open` returns null with typed diagnostics for recoverable invalid input. Validation precedes access:

- Validate metadata, finite transforms, safe grid arithmetic, palette indices, feature structure, and provenance.
- Require the exact canonical file inventory. Reject symlinks before descent and paths differing from the declared chunk layout.
- Validate lengths, hashes, finite terrain heights, known mask bits, optional imagery consistency, PNG dimensions, and decode-allocation bounds.
- Compare the full three-sample terrain overlap bitwise, plus material and mask shared core edges. Imagery chunks are disjoint.
- Require the report's payload hash to match. Successful payloads contain no error diagnostics or failed validation checks.

Validation uses sequential reads and bounded neighbour re-reads. Lazy chunk reads recheck hashes. Terrain and decoded-imagery caches each retain at most four chunks. Height sampling reuses the counts-based `TerrainChunkHeightSampler`. Imagery sampling bilinearly interpolates the generated global pixel grid at pixel centres across chunk seams, with outer-boundary clamping.

## Storage transaction

Reject an output directory containing the manifest or any resolved input source, including with force. Resolve existing ancestor links for containment checks. Staging or backup moves must never relocate an input tree.

Stage in a unique sibling directory. Without force, an existing destination fails unchanged. With force, move the previous destination to a unique sibling backup only after staged validation succeeds. Always retain that backup; never automatically delete pre-existing destination contents.

Check cancellation immediately before the rename transaction. Move the destination to backup, then staging to destination. Restore the backup if the final rename fails. Preserve and report recovery paths if restoration also fails. Once renames begin, finish or restore without another cancellation check between moves. Cancellation racing a successful commit does not report the committed output as failed.

Disposal removes only owned staging. Cleanup failures after commit produce runtime warnings, not a failed import or mutations to the committed report. Rename-failure tests use per-instance internal I/O seams, not global hooks.

## Staged validation

`IStagedPayloadValidator.Validate` receives the reader interface, the same loaded manifest used by generation, diagnostics, and cancellation. It returns ordered checks containing category, name, status (`Passed`, `Failed`, `NotEvaluated`), optional expected/actual/tolerance numbers, units, and a message. RGB validation uses one check per channel. Any failed check blocks commit even without an error diagnostic.

Write and validate the initial payload and report, run validators in declared order, then rewrite the report. If callbacks ran, reopen and fully revalidate staged content before commit. No validation runs after commit.

## Orchestration and dependants

Hash and read confined local inputs; reject changed sources. Select the extent, build the grid, stream terrain and optional imagery, clip and map features using a bounded staged-terrain cache, then stream materials and masks. Build metadata and the report, validate, and commit.

After card 18 acceptance, cards 19, 20, and the card 23 preview library can proceed independently. Later CLI integration attaches validation and preview flags. Optional preview runs after payload commit and writes a separately transactional map outside the payload directory. Preview failure preserves the authoritative payload and is reported separately.
