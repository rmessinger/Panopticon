# Task Card `WI-G3-02`: Add sharpness and edge-aliasing image metrics with unit tests

| Field | Value |
|---|---|
| Card ID | `WI-G3-02` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Ready` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Not required: this card adds pure image-analysis functions over a pixel buffer and touches no render path, so the [diagram gate](../README.md#how-to-maintain-this-guide) on executable render work does not apply. |

## Goal

Provide the objective metrics the G3 phase row requires, as pure functions over a decoded image,
verified against synthetic inputs. Producing them independently of the harness lets the metrics be
correct before any capture plumbing exists.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/FrameCapturer.cs`: the `CapturedFrame` shape (`PixelData`, `Width`,
  `Height`, `Format`), which fixes the input the metrics must accept.
- `test/Panopticon.Simulation.Tests/`: the existing xUnit conventions and the `MockedGameTest` base, for test
  placement and naming only.
- `docs/coding-style.md`: naming and formatting rules.

## Steps

1. Add a static metrics type taking a pixel buffer plus width, height, and format, returning a
   sharpness score and an edge-aliasing score. Accept the `CapturedFrame` field shapes directly so
   no conversion layer is needed later.
2. Implement sharpness as a normalized gradient-energy measure over luminance, so it is comparable
   between two frames of the same dimensions.
3. Implement edge aliasing as a measure of intermediate-value coverage along detected edges: a hard
   two-value staircase scores high, a smoothly ramped edge scores low.
4. Normalize both scores to a fixed range and document the range in an XML doc comment on each
   method, since later cards compare them against thresholds.
5. Add unit tests over synthetic images built in the test itself, with no game or GPU dependency:
   a hard diagonal step edge, the same edge with a linear ramp across two pixels, a uniform field,
   and a blurred checkerboard versus a sharp one.
6. Assert the ordering relations rather than absolute constants: the ramped edge scores lower on
   aliasing than the step edge, the sharp checkerboard scores higher on sharpness than the blurred
   one, and the uniform field scores zero on both.

## Files likely to change

- `src/Panopticon.Simulation/Graphics/` (new metrics type)
- `test/Panopticon.Simulation.Tests/` (new metric tests)

## Acceptance criteria

- [ ] Both metrics accept the `CapturedFrame` field shapes without a conversion step.
- [ ] Tests cover the step edge, ramped edge, uniform field, and sharp versus blurred checkerboard.
- [ ] Tests assert ordering relations, not hardcoded score constants.
- [ ] No test requires a graphics device or a game installation.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add golden images, capture code, or a comparison runner. That is `WI-G3-03`.
- Do not change `FrameCapturer` or `FrameCaptureService`.
- Do not add a perceptual or learned metric. Both metrics must be simple, formula-defined, and
  explainable from the code.
- If a metric cannot be made resolution-independent, document the constraint rather than comparing
  across dimensions.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
