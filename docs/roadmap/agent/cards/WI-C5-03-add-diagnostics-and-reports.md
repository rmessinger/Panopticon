# Task Card `WI-C5-03`: Add diagnostics and reports

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-03` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-10`, `WI-C5-01` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add immutable structured diagnostics, deterministic failure mapping and ordering, separate preview
eligibility, and a versioned external model report contract.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 6 and 10.
- `docs/roadmap/deep-dives/c4-gltf-importer.md` section 9.
- `src/OpenSage.Game/Content/Importing/ExternalAssetCatalog.cs` (new, from `WI-C5-01`).
- `src/OpenSage.Game/Content/Importing/ExternalAssetInspectionDiagnostic.cs` (new, from
  `WI-C5-01`).
- `src/OpenSage.Game/Content/Importing/ExternalFormatSelectionResult.cs` (new, from `WI-C5-01`).
- `src/OpenSage.Tools.ReplaySketch.Core/Assertions/AssertionReport.cs`: immutable report precedent.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ValidationGroundReport.cs`: JSON report precedent.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/Vector3JsonConverter.cs`.

## Steps

1. Add enums for diagnostic severity, operation stage, and source role using the exact values from
   deep-dive section 6.
2. Add immutable `ExternalModelDiagnostic` with code, severity, stage, source, optional context,
   message, optional fix hint, occurrence count, and non-serialized local exception detail.
3. Add one explicit selection-result, inspection-diagnostic, stage, and exception-type mapping
   table. Map expected C1-C4
   `InvalidDataException` and `NotSupportedException` failures without inspecting message text;
   map unexpected exceptions to `EXT999 InternalFailure`.
4. Add deterministic sorting and duplicate collapse by code, source, context, and message.
5. Add immutable `PreviewEligibility` with `CanRender`, blocking codes, and missing capabilities.
   Warnings alone must not block rendering.
6. Add a versioned `ExternalModelReport` envelope for source identities and hashes, catalog,
   diagnostics, and eligibility. Leave pairing, options, corrections, and render metadata for
   their owning later cards rather than inventing those types here.
7. Configure JSON serialization so reports omit parser values, exception details, temporary paths,
   stack traces, and game installation paths.
8. Add tests that lock every stage mapping, sort key, collapse count, warning eligibility,
   schema version, round trip, and forbidden-field omission.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelDiagnostic.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelDiagnosticMapper.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/PreviewEligibility.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReport.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReportJson.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelContractTests.cs` (new)

## Acceptance criteria

- [ ] Diagnostics are immutable and use the documented code families and stages.
- [ ] Code selection never parses exception messages.
- [ ] Unexpected exceptions produce `EXT999`, remain available to local logs, and fail eligibility.
- [ ] Sorting and duplicate collapse are deterministic and preserve an occurrence count.
- [ ] Warnings remain visible without blocking an otherwise eligible preview.
- [ ] Report JSON is versioned and contains no machine-specific or exception-detail fields.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelContractTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not duplicate C1-C4 validation or infer codes from localized text.
- Do not serialize stack traces, exception objects, source root paths, or game installation paths.
- Do not make warnings block preview without a separate missing capability.
- Do not add UI formatting or TestHarness process execution.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C5-02` is done, change `WI-C5-04` to `Ready`.
- Append a result line to `.exchange`.
