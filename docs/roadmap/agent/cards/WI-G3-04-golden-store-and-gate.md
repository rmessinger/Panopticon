# Task Card `WI-G3-04`: Golden store, comparer and regression gate

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G3-04` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-G3-03` |
| Diagrams | [G3 architecture](../../deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams) |

## Goal

Turn the `VisualRegression` capture pass into a gate: compare each native capture against a
committed golden per preset with explicit tolerances, pin goldens to a reference host, and update
goldens only through an opt-in mode. This completes the G3 milestone.

## Context to read (and only this)

- `docs/roadmap/deep-dives/g3-visual-regression-harness.md` §3.5 and §3.6, and the architecture
  diagrams.
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`: the capture pass from `WI-G3-03`.
- `src/Panopticon.Tools.TestHarness/PbrMaterialProofScenario.cs:62,230-233`: the existing
  committed-proof-PNG and byte-identity pattern.
- `src/Panopticon.Simulation/Graphics/FrameCapturer.cs:71-122`: `ToImage`, `CapturedFrame`.

## Steps

1. Add `GoldenTolerance`, `GoldenComparison` and a static `GoldenFrameComparer.Compare` over two
   equal-size RGBA images: count pixels whose largest channel delta exceeds `MaxChannelDelta`,
   record the largest delta and the metric deltas, and fail on `MaxDifferingPixelFraction` or
   `MaxMetricDelta`. Defaults 2, 0.001, 0.02; per-scene overrides from the scene set.
2. Add unit tests on synthetic pairs: identical passes, a perturbed 8x8 block fails, a
   sub-threshold delta passes, mismatched sizes fail.
3. Read goldens from `visual-regression/<preset>/<scene>.png` and
   `visual-regression/golden-manifest.json` (backend, device name, resolution, metric scores).
   Exit 1 on a missing golden, 2 on a regression, 6 when the running backend or device differs
   from the manifest (captures and scores still written).
4. Add `TESTHARNESS_VR_UPDATE_GOLDENS=1`: write native captures and a new manifest into the store
   instead of comparing. Never write goldens on a failing compare.
5. Capture the initial goldens on the reference host. Complete the deep dive's licensing and
   storage review before committing images; record the approved store and asset provenance.
6. Record the store location, invocation and update procedure in
   `docs/roadmap/implementation/track-g-g3-visual-regression.md`.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/` (comparer types, `VisualRegressionRunner.cs`)
- `test/Panopticon.Simulation.Tests/` (comparer tests)
- `visual-regression/` (goldens and manifest)
- `docs/roadmap/implementation/track-g-g3-visual-regression.md`

## Acceptance criteria

- [x] An unmodified run passes with exit 0 under both presets (verified twice: after the
      initial update run and again after the perturbation revert; 12/12 goldens Passed).
- [x] A deliberate perturbation (`RenderPipeline.ClearColor` 105 -> 110 in a scratch build)
      fails the affected scenes with exit 2 (terrain-grazing failed both presets at differing
      fraction 0.255; the sky region exposes the clear color while covered scenes stayed green,
      so the gate discriminates); reverting restored exit 0.
- [x] Goldens change only through `TESTHARNESS_VR_UPDATE_GOLDENS=1` (compare mode never writes
      into the store; the manifest records a SHA-256 per golden and the gate fails with
      `StoreMismatch` when the store drifts from the committed manifest).
- [x] Comparer unit tests run under `Category!=Interactive` without a GPU or install (6 tests
      over synthetic pairs).
- [x] Standard validation passes (build 0 errors; 2087 + 355 + 53 tests passed, 7 skipped;
      format and brace checks clean).

## Completion notes

- Goldens are NOT committed: the licensing and storage review (step 5) followed the repo
  precedent that renders of operator install content stay out of Git (see `.gitignore`
  `ee2-*-proof/`). The store lives outside the repo at
  `~/.panopticon/visual-regression-goldens` (override: `TESTHARNESS_VR_GOLDEN_STORE`); only
  `visual-regression/golden-manifest.json` is committed, pinning backend, device, host,
  resolution, per-golden SHA-256, scores and tolerances. Card step 3's `visual-regression/`
  image layout is therefore realized as store-root layout; this is the deep dive's
  reviewed-store fallback, recorded in the implementation plan.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Do not change rendering to make a comparison pass.
- A golden mismatch follows the repository determinism-failure rule: preserve artifacts, report
  the failing scene and likely cause, and wait for a decision before regenerating.
- Do not add Git LFS or a viewer.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G3 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G3 item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
