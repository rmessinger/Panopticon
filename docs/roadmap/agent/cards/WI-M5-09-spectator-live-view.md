# Task Card `WI-M5-09`: Spectator live view

| Field | Value |
|---|---|
| Card ID | `WI-M5-09` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-M5-04` |
| Diagrams | [M5 battle](../../deep-dives/mass-battle-1k.md#m5-battle) |

## Goal

Run the windowed battle as a spectator: the local player is the observer slot, not side A, the
whole battlefield is visible, and the operator only moves the camera.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 3: viewing model.
- `src/Panopticon.Simulation/Logic/PlayerManager.cs`: `OnNewGame` local player fallback to
  `Players[2]`.
- `src/Panopticon.Simulation/Logic/Map/SidesListUtility.cs`: the appended
  `ReplayObserver` player.
- `src/Panopticon.Simulation/Logic/PlayerSettings.cs`: `PlayerOwner`.
- `src/Panopticon.Tools.TestHarness/MassBattleCapacityProbeRunner.cs`: `FrameDeployment`
  camera setup and the windowed step.

## Steps

1. Add a spectator option to the game start path that makes the `ReplayObserver` player the
   local player, without changing the default fallback.
2. Reveal the map for the local observer (no shroud or fog), using the existing shroud or
   reveal mechanism.
3. In `MassBattleRunner` windowed mode, enable spectator, frame both zones with the
   `FrameDeployment` camera, and leave operator camera input active. No selection or order input
   reaches either army.
4. Tests on the mocked game: spectator start sets the observer as local player, both armies
   keep their AI, and default starts still pick `Players[2]`.
5. Capture a windowed screenshot per `docs/screenshots.md` showing both armies.

## Files likely to change

- `src/Panopticon.Simulation/Logic/PlayerManager.cs`
- `src/Panopticon.Simulation/Game.cs` (start option)
- `src/Panopticon.Tools.TestHarness/MassBattleRunner.cs`
- `test/Panopticon.Simulation.Tests/Logic/SpectatorStartTests.cs` (new)

## Acceptance criteria

- [ ] The windowed battle has an observer local player; both sides stay AI-controlled.
- [ ] Units of both sides are visible; the screenshot is shared under a versioned name.
- [ ] Spectator mode does not change the outcome or frame count for a seed versus headless.
- [ ] Standard validation passes (see [guide](README.md#standard-validation)).

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~SpectatorStart"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" EMPIRE_EARTH_II_PATH="<ee2 install>" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattle
dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No spectator UI (unit panels, score overlays) and no control-bar play.
- No frame rate work (`WI-M5-10`).

## On completion

- Set Status above to `Done`.
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
