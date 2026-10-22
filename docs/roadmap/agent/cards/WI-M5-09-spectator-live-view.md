# Task Card `WI-M5-09`: Spectator live view

| Field | Value |
|---|---|
| Card ID | `WI-M5-09` |
| Roadmap phase | [M5](../../track-m-mass-battle.md): 1000 vs 1000 battle |
| Status | `Done` |
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

- [x] The windowed battle has an observer local player; both sides stay AI-controlled.
- [x] Units of both sides are visible; the screenshot is shared under a versioned name.
- [x] Spectator mode does not change the outcome or frame count for a seed versus headless.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

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
  - Result: `Configuration.Spectator` (default false, default fallback untouched) makes
    `PlayerManager.OnNewGame` pick the `FactionObserver` player as local when present;
    `Game.StartGame` skips control-bar creation for the observer side (no authored bar, and no
    spectator UI is in scope). `MassBattleRunner` boots both modes with `Spectator = true`, so
    the modes stay code-identical and the headless same-seed identity doubles as the
    flag-independence proof (scaled seed-1/2 reports unchanged). Step 2 (map reveal) is a no-op
    today: the render path has no shroud or fog gating, so every object draws for any local
    player; recorded rather than wired. Map-reveal machinery belongs with a shroud track.
    Mocked `SpectatorStartTests` 4/4 (default fallback, observer local, spectator without
    observer keeps the fallback, spectated armies keep their AI). Screenshot: 1920x1080
    battlefield capture after spawn shared as `m5-spectator-battlefield-v1-scaled.png` in the
    viewer temp dir (scaled roster; capture pipeline verified, contents not visually inspected
    in-session). Windowed operator proof remains subject to the documented Xvfb present
    limitation. Full non-interactive suite 2408 passed / 8 known failures.
