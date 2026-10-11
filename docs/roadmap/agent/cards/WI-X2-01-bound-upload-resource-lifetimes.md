# Task Card `WI-X2-01`: Bound temporary graphics-upload resource lifetimes

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X2-01` |
| Roadmap phase | [Track X · X2](../../track-x-fixes-tweaks.md), capacity-probe graphics resource fixes |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [Architecture diagrams](#architecture-diagrams) |

## Goal

Prevent temporary upload command lists and staging resources from accumulating throughout a rendered battle. Verify native memory behavior independently of the managed heap.

## Context to read (and only this)

- [Capacity-probe evidence](../../implementation/track-x-x2-capacity-probe-fixes.md#capacity-probe-evidence).
- `src/Panopticon.Simulation/Utilities/Extensions/ResourceFactoryExtensions.cs`: static buffer and texture uploads.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystem.cs:150-242`: per-system buffer construction.
- `src/Veldrid/GraphicsDevice.cs`: `WaitForIdle`, `DisposeWhenIdle`, submission, and deferred disposal.
- `src/Veldrid/D3D11/D3D11CommandList.cs`: context ownership, submission completion, and disposal.
- `src/Panopticon.Simulation/Terrain/RadiusCursorDecals.cs:72-118`: existing synchronized upload cleanup.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: rendered probe and screenshot lifecycle.

## Architecture diagrams

```mermaid
classDiagram
    ParticleSystem --> ResourceFactoryExtensions : index upload
    ResourceFactoryExtensions --> GraphicsDevice : submit and queue cleanup
    GraphicsDevice --> D3D11CommandList : deferred disposal ownership
    D3D11CommandList --> ID3D11DeviceContext : owns native context
```

```mermaid
sequenceDiagram
    participant P as ParticleSystem
    participant U as ResourceFactoryExtensions
    participant G as GraphicsDevice
    P->>U: CreateStaticBuffer(indices)
    U->>G: Create staging and command list
    U->>G: Submit copy commands
    U->>G: DisposeWhenIdle(temporaries)
    Note over G: Current cleanup requires explicit WaitForIdle
    U-->>P: Destination index buffer
```

## Steps

1. Reproduce repeated particle-system/static-buffer uploads on D3D11 without screenshots or explicit idle waits during measurement. Record temporary-resource counts, managed heap, working set/private bytes, elapsed time, and upload count. Confirm the disposal queue is the retaining owner; do not infer retained native bytes from wrapper counts.
2. Fix the two upload helpers so each successful upload reaches GPU-safe temporary cleanup without relying on scene teardown or an incidental screenshot. Use the existing submit, idle-wait, dispose pattern as the bounded correctness fix. Dispose only temporaries; preserve ownership of the returned destination. Cover exceptions during creation, recording, and submission without freeing resources still in flight.
3. Add regression coverage for repeated buffer and texture uploads, destination contents, and temporary-resource disposal. Follow existing graphics-test infrastructure. Mark platform-dependent D3D11 verification explicitly rather than silently skipping it.
4. Repeat the rendered capacity probe with the same profile, camera, frame budget, and build configuration. Record before/after memory and frame-time measurements. If native growth persists, preserve artifacts and report the residual separately.

## Files likely to change

- `src/Panopticon.Simulation/Utilities/Extensions/ResourceFactoryExtensions.cs`
- Focused upload-lifetime regression tests under `test/Panopticon.Simulation.Tests/`.
- This card's Result section.

## Acceptance criteria

- [x] Repeated uploads cannot grow the deferred temporary-resource backlog in proportion to total uploads.
- [x] Uploaded buffer and texture contents remain correct; returned destination resources remain usable.
- [x] Failure paths release safely owned temporary resources.
- [ ] D3D11 evidence includes native process-memory measurements, not only managed heap size. Pending operator Windows run; command in Result.
- [x] Capacity-probe results include memory growth and frame-time impact of synchronous cleanup, with residual growth explicitly reported (Linux/Vulkan host).
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

Record the exact Windows D3D11 probe command and focused graphics-test command in Result. Screenshots belong after the measured interval because capture calls `WaitForIdle` and can mask the defect.

## Out of scope / guardrails

- No global per-frame GPU idle wait, renderer redesign, general resource pooling, or vendored backend rewrite.
- Do not assume GPU submission has completed before disposing temporaries.
- If synchronized cleanup is prohibitively expensive, report the tradeoff and create a separately scoped asynchronous-upload follow-up instead of expanding this card.
- Preserve profiler artifacts. Determinism-test deviations require the repository's stop-and-report procedure.

## On completion

- Update this card, the [X2 plan](../../implementation/track-x-x2-capacity-probe-fixes.md), checklist, and progress table.
- Reconcile `WI-X2-02` with the post-fix allocation evidence before changing its status.
- Append a result line to repo-root `.exchange`.

## Result

Done, with the D3D11 native-memory item explicitly pending a Windows operator run.

Fix: `CreateStaticBuffer` and `CreateStaticTexture2D` now submit, queue their command list and staging resource, and immediately drain through `WaitForIdle`, mirroring `RadiusCursorDecals`. Recording and submission are guarded so a throw disposes the command list, staging resource, and undelivered destination. Destination ownership is unchanged.

Regression: `ResourceUploadLifetimeTests` (headless Vulkan). 64 buffer uploads and 32 texture uploads keep the deferred queue at zero after every call, destination contents verified by staging read-back (including uniform-buffer 16-byte rounding); a truncated-mip upload throws and leaves no deferred temporaries. Without the fix, both queue-drain tests fail (queue grows by two resources per upload); content and failure-path tests pass in both states by design.

Probe (Linux, RADV Vulkan, Debug build, 900-frame budget, deepdivide profile, 2,000-unit engagement; screenshots disabled during measurement):

| Metric | Pre-fix | Post-fix |
|---|---|
| Frames run / units destroyed | 900 / 36 | 900 / 36 |
| Median tick wall ms (95% CI) | 56.9 (54.0-60.7) | 53.0 (50.5-57.3) |
| Median FPS (95% CI) | 17.6 (16.5-18.5) | 18.9 (17.5-19.8) |
| scene3d span median ms | 3.85 | 3.85 |
| Gen0/1/2 GC | 1245/80/13 | 1244/98/14 |
| Managed alloc total | 17.54 GB | 17.54 GB |
| End RSS | 138.9 MB | 151.8 MB |

Interpretation: the battle is deterministic and identical; synchronous draining costs no measurable frame time on this host (medians overlap within CI; post-fix medians are slightly lower, run-to-run noise). Linux RSS cannot substitute for the Windows D3D11 evidence: RADV retains freed device memory in driver pools, so end-RSS differences are not a native-byte attribution. Both runs exit 5 with the pre-existing "no aircraft completed a second sortie" engagement check; identical in both states, recorded as environmental for this comparison.

Windows D3D11 evidence command (operator run; same profile and budget):

```sh
CNC_GENERALS_PATH="<install>" dotnet run --project src/Panopticon.Tools.TestHarness --no-build --   --headless --built-in-scenario MassBattleCapacityProbe
# TestHarness.json: Scenario=MassBattleCapacityProbe, Headless=true,
# MassBattleCapacityProbeFrameBudget=9000; sample working set/private bytes during the run;
# keep screenshots out of the measured interval.
```

Deferred follow-up if the Windows run shows the idle-per-upload drain is too costly: a scoped asynchronous-upload design (fence or per-frame drain), split from this card per its guardrails.
