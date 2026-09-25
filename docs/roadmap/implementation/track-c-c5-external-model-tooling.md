# Track C · C5: External model tooling

> Audience: human overview above the divider; agent-context detail below it.

Implements the C5 row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c5-external-model-tooling.md](../deep-dives/c5-external-model-tooling.md), including its
[architecture diagrams](../deep-dives/c5-external-model-tooling.md#12-architecture-diagrams).

## Scope

Add a ReplaySketch Model tab backed by engine-owned inspection contracts and Core session state.
TestHarness remains the isolated graphics host and accepts a versioned request that produces a
deterministic PNG and machine-readable report. External source roots never join the game
installation filesystem, and preview corrections never rewrite imported data or source files.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C5-01`](../agent/cards/WI-C5-01-add-external-inspection-facade.md) | Public format-neutral catalog, animation descriptors, inspector facade, and explicit format selection. |
| 2 | [`WI-C5-02`](../agent/cards/WI-C5-02-mount-external-source-roots.md) | Traversal-safe read-only model and animation source mounts. |
| 3 | [`WI-C5-03`](../agent/cards/WI-C5-03-add-diagnostics-and-reports.md) | Immutable diagnostics, stable failure mapping, preview eligibility, and versioned reports. |
| 4 | [`WI-C5-04`](../agent/cards/WI-C5-04-implement-external-model-session.md) | Generation-safe session state, immutable snapshots, cancellation, and leases. |
| 5 | [`WI-C5-05`](../agent/cards/WI-C5-05-pair-external-animations.md) | Same-document and cross-document animation pairing, import options, and preview controls. |
| 6 | [`WI-C5-06`](../agent/cards/WI-C5-06-add-external-model-protocol.md) | Versioned JSON request protocol with separate roots and validated outputs. |
| 7 | [`WI-C5-07`](../agent/cards/WI-C5-07-host-external-model-requests.md) | TestHarness host mode through import, materialization, binding, and disposal. |
| 8 | [`WI-C5-08`](../agent/cards/WI-C5-08-render-external-model-scenes.md) | Deterministic scene rendering, sampling, atomic artifacts, and report-only mode. |
| 9 | [`WI-C5-09`](../agent/cards/WI-C5-09-launch-testharness-preview.md) | Awaitable cancellable preview process client and sibling-build executable locator. |
| 10 | [`WI-C5-10`](../agent/cards/WI-C5-10-add-external-model-viewmodel.md) | Injectable Core view model commands and state. |
| 11 | [`WI-C5-11`](../agent/cards/WI-C5-11-add-replaysketch-model-tab.md) | MAUI Model tab, pickers, diagnostics, preview, and fake-session screenshot mode. |
| 12 | [`WI-C5-12`](../agent/cards/WI-C5-12-verify-external-model-tooling.md) | Core, TestHarness, Model-tab, and Map-tab acceptance coverage. |

## Dependencies

Card 1 waits for `WI-C4-10`, which establishes the complete internal glTF importer composition
and exception contract. Card 2 follows the C4 resolver rules from `WI-C4-02`, but owns the
host-root wrapper used by C5. Cards 3, 4, and 6 also name `WI-C4-10` directly because they consume
that stable inspection and failure boundary without requiring graphics.

Card 5 waits for the session and C4 animation conversion. Cards 6 through 9 form one linear
protocol path: request contract, TestHarness host, deterministic rendering, then process client.
The host and render cards name `WI-C4-12`; the process client inherits it transitively through
`WI-C5-08`. The render card also waits for the reusable runtime-model capture helper from
`WI-C4a-10`.

Cards 9 through 11 continue independently after the render protocol: process client, Core view
model, then MAUI presentation and platform pickers. Card 12 waits for every earlier C5 card and
`WI-C4-12`; it is the C5 completion point. These dependencies do not point from C4 or C4a back
into C5, so they introduce no cycle.

---

## Agent context

- Public inspection boundary: `Panopticon.Simulation` exposes engine-owned catalogs and
  `IExternalAssetInspector`, not the internal C1 `IModelImporter` or `IAnimationImporter`.
- C4 state: every path under `Content/Importing/Gltf/` named by these cards is planned by C4 and
  does not exist until its prerequisite C4 card completes.
- Source isolation: model and optional animation documents use separate read-only roots. Keys are
  normalized virtual paths; absolute host paths appear only in the process request.
- Diagnostics: codes are stable values. Exception messages are not parsed to choose codes.
- Session ownership: source filesystems, cancellation sources, child processes, and preview
  artifacts have explicit leases and generation checks.
- Option boundary: `UnitsPerMeter` and feature gates rerun import. Axis, camera, lighting,
  transforms, morph samples, and animation time affect preview only.
- Protocol owner: request and report contracts live in ReplaySketch Core, which both MAUI and
  TestHarness already reference.
- Graphics owner: TestHarness constructs the game host and external `AssetLoadContext`, then uses
  C1 materializers and C2 binding. The ReplaySketch Core external-model path never creates a
  graphics device.
- Process boundary: the preview client is awaitable and cancellable. It does not reuse
  `IProcessLauncher`, whose contract is fire-and-forget.
- UI boundary: Model-tab command logic stays in Core. MAUI owns file pickers, bindings, image
  display, and deterministic screenshot fixture wiring.
- Acceptance assets: link repository-owned C4 fixtures into the ReplaySketch test output and
  resolve them from `AppContext.BaseDirectory`. Do not add downloaded or third-party model data.
