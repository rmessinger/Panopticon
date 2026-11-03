# Task Card `WI-G13-04`: Ground-level projection, culling, and HUD

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G13-04` |
| Roadmap phase | [Track G · G13](../../track-g-rendering-clarity.md): First-person ground view |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G13-02`, `WI-G13-03` |
| Diagrams | [G13 architecture](../../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams) |

## Goal

Make the first-person view render correctly at eye height: near and far planes, correct FOV, object culling, shadow cascades, and a hidden HUD with a crosshair and compass.

## Context to read (and only this)

- [G13 design](../../deep-dives/g13-first-person-ground-view.md) section 3.4.
- `src/Panopticon.Simulation/Graphics/Cameras/Camera.cs`, `Scene3D.cs:71-95, 421-465`, `Graphics/Rendering/Shadows/ShadowSettings.cs`, `Graphics/Rendering/RenderPipeline.cs`.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:148-155` (current HUD hiding).

## Steps

1. On entering `FirstPerson`, apply a `FirstPersonProjection` (near `0.25 * EyeHeight`, far `FirstPersonFarPlane`, horizontal FOV 75° converted with `2·atan(tan(h/2)/aspect)`); restore the tactical values on exit. Measure depth precision on a structure at 1000 units and record it.
2. Enable object frustum culling and four shadow cascades with a shorter distance while in `FirstPerson`; restore on exit.
3. Add `HudVisibility` on the game client: suppress WND/APT, `SelectionGui`, and debug overlay drawing and their input without popping windows. Use it in first person and switch the capture path to it.
4. Draw a crosshair and a compass strip in the 2D layer in first person.
5. Capture eye-height frames near a structure and toward the horizon.

## Files likely to change

- `src/Panopticon.Simulation/ViewModes/FirstPersonProjection.cs`, `HudVisibility.cs`, `FirstPersonOverlay.cs` (new), `ViewModeController.cs`
- `src/Panopticon.Simulation/Scene3D.cs`, `Graphics/Cameras/Camera.cs`, shadow settings
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`

## Acceptance criteria

- [ ] No near-plane clipping of terrain or a structure at one step from the eye.
- [ ] Tactical projection, culling, and shadow values restore exactly on exit; existing goldens unchanged.
- [ ] HUD hides and returns with windows intact; capture output is unchanged.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario VisualRegression --headless
```

## Out of scope / guardrails

- Sky and fog (`WI-G13-05`).
- Presentation only: no `GameObject`, order, or logic random. A determinism-enforcing test failure is a suspected deviation: stop, keep artifacts, report.

## On completion

- Set Status above to `Done` or `Blocked` with reason; set `WI-G13-05` to `Ready`.
- Append a result line to `.exchange`.
