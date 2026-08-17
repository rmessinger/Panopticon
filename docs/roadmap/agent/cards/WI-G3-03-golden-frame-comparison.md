# Task Card `WI-G3-03`: Golden-frame comparison over the fixed scene set

| Field | Value |
|---|---|
| Card ID | `WI-G3-03` |
| Roadmap phase | [Track G · G3](../../track-g-rendering-clarity.md): Visual regression harness |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-G3-01`, `WI-G3-02`, `WI-G2-02`, plus a G3 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | Blocked on the G3 deep dive. Do not mark this card `Ready` until its Architecture Diagrams section exists and is not a placeholder. |

## Goal

Turn the existing capture path into a headless golden-frame regression run over a fixed scene set,
per preset, failing on regression and reporting the `WI-G3-02` metrics per frame. This is the guard
every later Track G phase validates against.

## Context to read (and only this)

- `docs/roadmap/implementation/track-g-g3-visual-regression.md`: the `WI-G3-01` gap list, which
  fixes what this card must add.
- The G3 deep dive and its architecture diagrams.
- `src/OpenSage.Game/Graphics/FrameCaptureService.cs`: the capture scheduling API to extend.
- The metrics type added by `WI-G3-02`.
- `src/OpenSage.Tools.TestHarness/AGENTS.md`: the existing headless frame-capture test and its
  output layout.

## Steps

1. Define the fixed scene set as data, not code: a small ordered list naming each scene, its camera,
   and the frame indices to capture. Keep it in one file so adding a scene is a data edit.
2. Add the comparison run as a test that, for each scene and each preset, captures the listed frames,
   compares each against its stored golden, and reports the metric scores.
3. Compare with a tolerance rather than byte equality, since driver and platform differences make
   exact match unachievable. Fail when the difference exceeds the tolerance the deep dive fixes.
4. Add an explicit golden-update mode, gated behind an environment variable, that writes the current
   captures as the new goldens. Never update goldens implicitly on failure.
5. Mark the run so it is excluded from the default non-interactive suite if it needs a graphics
   device or a game installation, following the existing category convention.
6. Record the golden store location and the update procedure in
   `docs/roadmap/implementation/track-g-g3-visual-regression.md`.

## Files likely to change

- `src/OpenSage.Game.Tests/` (the comparison run and the scene set data)
- `docs/roadmap/implementation/track-g-g3-visual-regression.md`

## Acceptance criteria

- [ ] The run captures every scene under both presets and compares against stored goldens.
- [ ] A deliberately perturbed frame fails the run; an unmodified run passes.
- [ ] The edge-aliasing metric distinguishes an aliased frame from an antialiased one on the same
      scene, satisfying the G3 milestone.
- [ ] Goldens are updated only through the explicit opt-in mode.
- [ ] The run is categorized so the default `Category!=Interactive` suite stays green without a
      game installation.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build a new tool or a viewer. Extend the existing capture path, per the
  [C5 Decision](../../open-questions.md).
- Do not change any rendering behaviour to make a comparison pass.
- Do not commit large golden image sets without confirming the storage approach the deep dive fixes.
- If the fixed scene set cannot be rendered headlessly, mark `Blocked` and record why.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the G3 phase Status in [`track-g-rendering-clarity.md`](../../track-g-rendering-clarity.md).
- Tick the G3 clause in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
