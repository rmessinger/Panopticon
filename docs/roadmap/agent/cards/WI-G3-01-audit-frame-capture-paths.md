# Task Card `WI-G3-01`: Audit the frame capture paths and the golden-comparison gap

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G3-01` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G3 architecture](../../deep-dives/g3-visual-regression-harness.md#5-architecture-diagrams) |

## Goal

Record exactly what the existing capture machinery provides and what a golden-frame comparison still
needs, so G3 extends the current paths rather than adding a tool. Read-only: no code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/FrameCapturer.cs`: `Capture(GraphicsDevice, Texture)` and the
  `CapturedFrame` result type.
- `src/Panopticon.Simulation/Graphics/FrameCaptureService.cs`: `ScheduleCapture`, `FlushPending`, `Capture`,
  `DeleteCaptures`, `ClearCaptureDirectory`, and the capture directory convention.
- `src/Panopticon.Tools.ReplaySketch.Avalonia/`: the `REPLAYSKETCH_SCREENSHOT_PATH` screenshot path.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`: the engine-frame scenario path.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: the existing frame-validation workflow and the
  `validation_guide.md` convention.
- `src/Panopticon.Simulation/Graphics/Rendering/RenderPipeline.cs:160-168`: the final blit, which fixes what
  texture a capture can read.

## Steps

1. Record the two capture paths separately: the engine path through `FrameCapturer` and
   `FrameCaptureService`, and the ReplaySketch screenshot path. For each, record what it captures,
   what it writes, where, and whether it needs a window.
2. Confirm ReplaySketch screenshots use a different capture mechanism; G3 extends only engine
   capture through TestHarness. Record two-run repeatability and the measured reference host.
3. List what a golden comparison needs that neither path provides today: stable scene selection,
   explicit frame indices, a golden store location, a per-preset dimension, and a pass or fail
   exit code.
4. Record which existing headless test already captures frames, and whether its output layout can
   serve as the golden store without change.
5. Fill the audit section in `docs/roadmap/implementation/track-g-g3-visual-regression.md`
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g3-visual-regression.md`

## Acceptance criteria

- [ ] Both capture paths are described with their entry points and outputs.
- [ ] The question of whether ReplaySketch screenshots use `FrameCapturer` is answered definitively.
- [ ] The gap list names each missing capability with the component that would own it.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not implement metrics, capture or comparison. Those are `WI-G3-02` through `WI-G3-04`.
- Do not propose a new tool. The [C5 Decision](../../open-questions.md) rules one out.
- Do not capture or commit any golden images.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G3-02` through `WI-G3-04` already exist.
