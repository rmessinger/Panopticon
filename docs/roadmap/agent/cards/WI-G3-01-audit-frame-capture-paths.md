# Task Card `WI-G3-01`: Audit the frame capture paths and the golden-comparison gap

| Field | Value |
|---|---|
| Card ID | `WI-G3-01` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: G3 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

Record exactly what the existing capture machinery provides and what a golden-frame comparison still
needs, so G3 extends the current paths rather than adding a tool. Read-only: no code changes.

## Context to read (and only this)

- `src/OpenSage.Game/Graphics/FrameCapturer.cs`: `Capture(GraphicsDevice, Texture)` and the
  `CapturedFrame` result type.
- `src/OpenSage.Game/Graphics/FrameCaptureService.cs`: `ScheduleCapture`, `FlushPending`, `Capture`,
  `DeleteCaptures`, `ClearCaptureDirectory`, and the capture directory convention.
- `src/OpenSage.Tools.ReplaySketch.Maui/MainPage.xaml.cs`: the `REPLAYSKETCH_SCREENSHOT_PATH`
  screenshot path.
- `src/OpenSage.Tools.TestHarness/AGENTS.md`: the existing frame-validation workflow and the
  `validation_guide.md` convention.
- `src/OpenSage.Game/Graphics/Rendering/RenderPipeline.cs:160-168`: the final blit, which fixes what
  texture a capture can read.

## Steps

1. Record the two capture paths separately: the engine path through `FrameCapturer` and
   `FrameCaptureService`, and the ReplaySketch screenshot path. For each, record what it captures,
   what it writes, where, and whether it needs a window.
2. State explicitly whether the ReplaySketch screenshot path goes through `FrameCapturer` or through
   a different capture mechanism, since G3's phase row assumes it can extend both.
3. List what a golden comparison needs that neither path provides today: stable scene selection,
   explicit frame indices, a golden store location, a per-preset dimension, and a pass or fail
   exit code.
4. Record which existing headless test already captures frames, and whether its output layout can
   serve as the golden store without change.
5. Write the findings into a new `docs/roadmap/implementation/track-g-g3-visual-regression.md`
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g3-visual-regression.md` (new)

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

- Do not build a comparison harness or write metric code. That is `WI-G3-02` and `WI-G3-03`.
- Do not propose a new tool. The [C5 Decision](../../open-questions.md) rules one out.
- Do not capture or commit any golden images.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G3-02` and `WI-G3-03` already exist.
