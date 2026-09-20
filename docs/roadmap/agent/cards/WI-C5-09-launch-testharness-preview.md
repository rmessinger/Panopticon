# Task Card `WI-C5-09`: Launch TestHarness preview

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-09` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C5-08` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add an awaitable cancellable TestHarness preview client with captured stderr, exit handling,
precise owned-process termination, and a reusable sibling-build executable locator.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 5 and 8.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IProcessLauncher.cs`: fire-and-forget contract
  that must not be reused.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/LauncherLocatorService.cs`: sibling build-output
  convention.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/RecordingProcessLauncher.cs`.
- `src/OpenSage.Tools.ReplaySketch.Maui/Services/SystemProcessLauncher.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelRequest.cs` (new, from
  `WI-C5-06`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelProtocolJson.cs` (new,
  from `WI-C5-06`).
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelReport.cs` (new, from
  `WI-C5-03`).

## Steps

1. Extract the project name, configuration preference, target-framework search, and platform
   executable naming from `LauncherLocatorService` into a reusable sibling-build locator.
2. Preserve `LauncherLocatorService.Locate()` behavior by delegating to the new locator and add a
   TestHarness locator for `OpenSage.Tools.TestHarness`.
3. Add `IExternalModelPreviewClient` with one asynchronous operation accepting a request,
   cancellation token, and caller-owned artifact lease.
4. Serialize the request into the lease directory, launch the located TestHarness executable with
   `--external-model-request`, disable shell execution, redirect standard error, and await exit.
5. Treat exit code zero plus a valid matching-generation report as success. Return structured
   failures for missing executable, start failure, nonzero exit, malformed or missing report,
   generation mismatch, and missing requested PNG.
6. On cancellation, terminate only the exact `Process` instance created for that request,
   including its child process tree where supported, then await process exit and stream-drain
   completion. Never use name-based process termination.
7. Bound captured stderr, include it in local failure detail and logs, and omit it from serialized
   reports.
8. Dispose process, registration, streams, and request file on every path without deleting the
   caller's artifact lease or preserved failure report.
9. Put process creation and termination behind an internal adapter. Test success, stderr, nonzero
   exit, cancellation, exact owned-instance termination, locator preference, generation mismatch,
   and cleanup with fakes, plus one direct TestHarness invalid-request smoke test.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Services/SiblingBuildExecutableLocator.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/LauncherLocatorService.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IExternalModelPreviewClient.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/IExternalModelProcess.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Core/Services/TestHarnessPreviewClient.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/TestHarnessPreviewClientTests.cs` (new)
- `src/OpenSage.Tools.ReplaySketch.Tests/SiblingBuildExecutableLocatorTests.cs` (new)

## Acceptance criteria

- [ ] The client is awaitable and cancellable and does not call `IProcessLauncher`.
- [ ] It launches the built TestHarness executable directly, not `dotnet run`.
- [ ] Cancellation terminates only the owned process and awaits its complete exit.
- [ ] Exit code, bounded stderr, report validity, generation, and requested PNG are checked.
- [ ] Existing launcher location behavior remains covered and unchanged.
- [ ] Failure reports remain available while transient request files and process resources are
      released.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~TestHarnessPreviewClientTests|FullyQualifiedName~SiblingBuildExecutableLocatorTests|FullyQualifiedName~ReplayExporterOpenCommandTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change `IProcessLauncher` into an async process API or route preview through it.
- Do not use `Process.Kill` on a discovered process, process name, or unrelated process ID.
- Do not fall back to `dotnet run`, a shell command string, or current-directory executable search.
- Do not serialize stderr, stack traces, or executable paths into `ExternalModelReport`.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C5-04` and `WI-C5-05` are done, change `WI-C5-10` to `Ready`.
- Append a result line to `.exchange`.
