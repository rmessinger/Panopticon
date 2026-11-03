# Task Card `WI-G13-06`: Transitions, goldens, and cost

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-06` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `M` |
| Prerequisites | `WI-G13-05` |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Animate descent and ascent, then guard the view with a scripted scenario, goldens, a GIF, and a frame-time measurement.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) sections 3.5 and 4.
- `src/Panopticon.Tools.TestHarness/VisualRegressionRunner.cs`, `BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`; `docs/screenshots.md`.

## Steps

1. Add the 0.8 s eased descent from the tactical eye to the avatar eye and the reverse ascent to the restored snapshot re-targeted at the avatar position; input is ignored during transitions.
2. Add `BuiltInScenario.FirstPersonView`: drop at a scripted point on Alpine Assault, walk a scripted path past a structure and up a slope, exit. Report the avatar path and blocked steps.
3. Add `first-person` G3 captures (open ground facing a structure, horizon) for `faithful` and `enhanced`; record goldens; inject a near-plane and a fog regression and confirm failures.
4. Record B1 frame time in first person against the default tactical view in the plan.
5. Share a GIF of drop, walk, and exit under a versioned name.

## Files likely to change

- `src/Panopticon.Simulation/ViewModes/ViewTransition.cs` (new), `ViewModeController.cs`
- `src/Panopticon.Tools.TestHarness/FirstPersonViewRunner.cs` (new), `BuiltInScenario.cs`, `HarnessScenarioCatalog.cs`, `Program.cs`, `VisualRegressionRunner.cs`
- `docs/roadmap/implementation/track-g-g13-first-person-ground-view.md` (results)

## Acceptance criteria

- [ ] Goldens recorded; both injected regressions fail; existing goldens unchanged.
- [ ] Frame-time comparison recorded.
- [ ] GIF shared.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario FirstPersonView --headless
```

## Out of scope / guardrails

- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick G13-5 in the checklist and update the README progress row.
- Append a result line to `.exchange`.
