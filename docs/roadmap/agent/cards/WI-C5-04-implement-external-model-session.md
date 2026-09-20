# Task Card `WI-C5-04`: Implement external model session

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-04` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4-10`, `WI-C5-02`, `WI-C5-03` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Implement the graphics-free external model session with immutable snapshots, generation-based
cancellation, source replacement, and explicit filesystem and artifact ownership.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 2, 5, and 12.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`: Core orchestration boundary.
- `src/OpenSage.Tools.ReplaySketch.Core/ScenarioMatrixRunner.cs`: immutable result precedent.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IMainThreadDispatcher.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/PlatformPaths.cs`: application cache root.
- `src/OpenSage.Game/Content/Importing/ExternalAssetSourceMount.cs` (new, from `WI-C5-02`).
- `src/OpenSage.Game/Content/Importing/IExternalAssetInspector.cs` (new, from `WI-C5-01`).
- `src/OpenSage.Game/Content/Importing/ExternalAssetInspection.cs` (new, from `WI-C5-01`).
- `src/OpenSage.Game/Content/Importing/ExternalFormatSelectionResult.cs` (new, from
  `WI-C5-01`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelDiagnostic.cs` (new, from
  `WI-C5-03`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/PreviewEligibility.cs` (new, from
  `WI-C5-03`).

## Steps

1. Add the `Empty`, `Inspecting`, `Ready`, `Rendering`, `Failed`, and `Disposed` session states.
2. Add immutable `ExternalModelSnapshot` containing generation, state, model source summary,
   catalog, diagnostics, eligibility, and latest preview artifact metadata. Card 5 adds animation
   pairing and option values after defining their types.
3. Add interfaces for source mount creation, inspection, preview artifact storage, and artifact
   leases so Core tests use fakes and the session deletes only its owned cache directory. Route
   production inspection through the public engine facade, not internal C1 importers.
4. Implement asynchronous model replacement. Cancel current work, increment the generation,
   dispose prior source and artifact leases, clear source-specific state, publish `Inspecting`,
   and inspect the new mount.
5. Discard any inspection or preview result whose generation no longer matches. Treat cancellation
   as control flow, not a diagnostic.
6. Publish complete immutable snapshots only. Keep warnings independent from operation state and
   allow `Ready` with warnings.
7. Make disposal idempotent, cancel and await owned work, release all leases, and reject later
   operations with `ObjectDisposedException`.
8. Add deterministic tests for successful inspection, warnings, replacement races, stale
   completion, cancellation, failed inspection, artifact replacement, and disposal order.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSessionState.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSnapshot.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IExternalAssetSourceMountFactory.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IExternalModelArtifactStore.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ExternalModelArtifactStore.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ExternalModelSession.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/ExternalModelSessionTests.cs` (new)

## Acceptance criteria

- [ ] Snapshot consumers never observe partially updated mutable state.
- [ ] Every source replacement increments one monotonic generation and disposes prior leases.
- [ ] Stale generation results cannot replace the current catalog, diagnostics, or artifact.
- [ ] Cancellation produces no import diagnostic.
- [ ] A ready session may contain warnings and remains render-eligible when no blocker exists.
- [ ] Session disposal releases filesystem, cancellation, task, and artifact ownership exactly once.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelSessionTests|FullyQualifiedName~ExternalModelContractTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not create a graphics device, process, MAUI control, or file picker.
- Do not expose mutable collections or live filesystem objects in snapshots.
- Do not delete paths outside the artifact store's one owned session directory.
- Do not retain an old source mount after publishing the replacement generation.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4-08` is done, change `WI-C5-05` to `Ready`.
- Append a result line to `.exchange`.
