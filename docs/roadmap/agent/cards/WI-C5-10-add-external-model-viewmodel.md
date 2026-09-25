# Task Card `WI-C5-10`: Add external model view model

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-C5-10` |
| Roadmap phase | [Track C · C5](../../track-c-external-assets.md): Authoring and validation tooling |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C5-04`, `WI-C5-05`, `WI-C5-09` |
| Diagrams | [C5 architecture diagrams](../../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams) |

## Goal

Add an injectable ReplaySketch Core view model that exposes file-selection, diagnostics, options,
eligibility, reset, reload, and render commands without MAUI or graphics dependencies.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c5-external-model-tooling.md` sections 5, 7, and 9.
- `src/Panopticon.Tools.ReplaySketch.Maui/ViewModels/MainViewModel.cs`: current property-change and
  command behavior; do not move Model-tab logic into this MAUI type.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/IMainThreadDispatcher.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/SynchronousMainThreadDispatcher.cs`.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/ExternalModelSession.cs` (new, from `WI-C5-04`).
- `src/Panopticon.Tools.ReplaySketch.Core/Model/ExternalModels/ExternalModelSnapshot.cs` (new, from
  `WI-C5-04`).
- `src/Panopticon.Tools.ReplaySketch.Core/Services/IExternalModelPreviewClient.cs` (new, from
  `WI-C5-09`).

## Steps

1. Add an injectable file-selection interface with separate asynchronous model and animation
   operations returning a selected absolute file or cancellation.
2. Add a small awaitable command implementation in Core with execution state, cancellation,
   exception propagation to the view model, and `CanExecuteChanged`.
3. Add `ExternalModelViewModel` implementing `INotifyPropertyChanged` over one
   `ExternalModelSession`, file selector, main-thread dispatcher, and preview client.
4. Expose immutable bindable collections or projections for source summary, structural counts,
   animation descriptors, sorted diagnostics, selected diagnostic detail, import options, preview
   options, eligibility, current artifact, and operation status.
5. Add commands for choosing or reloading a model, choosing or clearing an animation source,
   selecting a descriptor, applying import options, applying preview options, resetting, rendering,
   and canceling current work.
6. Enable render only when `PreviewEligibility.CanRender` is true and neither inspection nor render
   is in flight. Keep warnings visible and nonblocking.
7. Marshal snapshot publication to the injected dispatcher, ignore stale generations, and keep
   command execution awaitable for tests.
8. Dispose subscriptions and the owned session exactly once.
9. Add tests with fake selector, inspector, preview client, dispatcher, and artifact store for
   command states, cancellation, warnings, options, diagnostics, stale results, render requests,
   and disposal.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Services/IExternalModelFileSelector.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/AsyncCommand.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/ViewModels/ExternalModelViewModel.cs` (new)
- `test/Panopticon.Tools.ReplaySketch.Tests/ExternalModelViewModelTests.cs` (new)

## Acceptance criteria

- [ ] The view model project has no MAUI, TestHarness, or graphics-device dependency.
- [ ] Every service that touches files, processes, clocks, artifacts, or dispatch is injectable.
- [ ] Render enablement follows eligibility and operation state; warnings alone do not disable it.
- [ ] Import option changes and preview-only changes call the distinct session paths.
- [ ] All commands are awaitable and deterministic under fake services.
- [ ] Disposal cancels work and removes session subscriptions exactly once.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ExternalModelViewModelTests|FullyQualifiedName~ExternalModelSessionTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not reference `Microsoft.Maui`, create controls, invoke platform pickers, or construct a
  graphics device.
- Do not add Model-tab state to `MainViewModel`.
- Do not use `async void` in Core commands.
- Do not hide warning diagnostics or enable rendering during an active render.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C5-11` to `Ready`.
- Append a result line to `.exchange`.
