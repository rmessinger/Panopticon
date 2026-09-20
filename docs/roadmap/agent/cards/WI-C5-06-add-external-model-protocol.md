# Task Card `WI-C5-06`: Add external model protocol

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-06` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-10`, `WI-C5-02`, `WI-C5-03`, `WI-C5-05` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Define and validate the versioned JSON request shared by ReplaySketch Core and TestHarness,
including distinct model and animation roots and caller-owned output paths.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 7, 8, and 10.
- `src/OpenSage.Tools.ReplaySketch.Core/OpenSage.Tools.ReplaySketch.Core.csproj`.
- `src/OpenSage.Tools.TestHarness/OpenSage.Tools.TestHarness.csproj`: existing Core reference.
- `src/OpenSage.Tools.TestHarness/Program.cs`: argument parsing precedent.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs`: JSON contract precedent.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelImportOptions.cs` (new,
  from `WI-C5-05`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelPreviewOptions.cs` (new,
  from `WI-C5-05`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReport.cs` (new, from
  `WI-C5-03`).

## Steps

1. Add immutable request values for schema version, generation, model source root and virtual key,
   optional animation source root and virtual key, format identifiers, import options, feature
   gates, preview corrections, animation sample time, camera, lighting, viewport, optional output
   image path, and required output report path.
2. Keep absolute host paths only in the request source-root and output fields. Keep virtual keys
   normalized and relative.
3. Add one serializer with explicit enum handling, stable property names, strict schema-version
   checks, and deterministic output.
4. Validate positive viewport dimensions, finite option values, supported format identifiers,
   existing absolute source roots, normalized virtual keys, and optional animation-field
   consistency.
5. Require output paths to be absolute files with existing parent directories. Require image and
   report outputs to share one canonical parent directory. Reject equality with either source
   file, directory targets, root-directory targets, and duplicate image and report paths.
6. Permit a missing output image only for report-only mode. Always require a report path.
7. Return structured `EXT` diagnostics for request failures without opening a game installation or
   source document.
8. Add round-trip, deterministic JSON, version mismatch, separate roots, report-only, and every
   output validation case to focused tests.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelRequest.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSourceRequest.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelViewport.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelProtocolJson.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelRequestValidator.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelProtocolTests.cs` (new)

## Acceptance criteria

- [ ] Request JSON carries distinct model and animation roots and normalized virtual keys.
- [ ] Unknown schema versions and format identifiers fail before filesystem or graphics work.
- [ ] Output validation prevents source overwrite, duplicate outputs, directory targets, and
      outputs split across different artifact directories.
- [ ] Report-only requests omit only the image output.
- [ ] Serialization is deterministic and round-trips every documented option.
- [ ] Validation failures use structured diagnostics and require no game installation.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelProtocolTests|FullyQualifiedName~ExternalModelContractTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not put parser objects, internal importer interfaces, open streams, or filesystem instances in
  the protocol.
- Do not add a persistent worker, network transport, or source-file mutation command.
- Do not resolve the game installation during request validation.
- Do not infer report-only mode from an invalid or unwritable image path.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-12` is done, change `WI-C5-07` to `Ready`.
- Append a result line to `.exchange`.
