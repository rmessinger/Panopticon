# SCUD launch sequence: transfer back

Status handoff for the agent that wrote `docs/scud-launch-validation.md`. That brief has been
rewritten in place to record the real root causes; this document is the summary of what
changed, what is verified, and what is still open.

## 1. Status

The reported defect is fixed. Four separate faults were involved, three of them engine bugs.

| # | Fault | Fix | Commit |
|---|---|---|---|
| 1 | Erector rolled about its own length instead of elevating, and the missile fired horizontally out of the truck's nose | `W3dModelDraw.UpdateTurretAim` now rotates the pitch bone about the lateral axis | `86ec012d` |
| 2 | Stowed missile never rendered; the erector raised an empty rail | `W3dModelDraw.UpdateBoneVisibilities` now reads the dictionary value instead of testing for key presence | `6a0bd967` |
| 3 | No launch arc; missile flew straight at the target | Not an engine bug: the capture ran at 90 units, inside the weapon's `MinimumAttackRange` of 200 | `6a0bd967` (doc only) |
| 4 | In-flight missile's pitch flipped roughly +/-45 degrees on alternating frames | `Matrix4x4Utility.CreateTransformMatrix` now builds the basis directly so row 1 is the requested direction | `8e91bb78` |

Current artifact: `/mnt/pit2/Network Storage/temp/scud_launch.gif` (163 frames), viewable at
https://files.reid-corp.com/viewer/.

## 2. Root causes, in short

Full evidence is in `docs/scud-launch-validation.md` sections 3, 4a, 4b, 4c.

1. `Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll)` maps pitch to X. W3D model space is
   X forward, Y lateral, Z up, so the turret pitch was applied as a roll.
2. `UpdateProjectileBoneFeedback` writes `HiddenSubObjects["MISSILE"] = false` every frame
   while a round is loaded. `UpdateBoneVisibilities` treated the key's presence as a hide
   command, so the missile was hidden permanently from the first condition-state update.
3. `SCUDLauncherGun*`: `AttackRange 350`, `MinimumAttackRange 200`,
   `DistanceToTargetBeforeDiving 200`. At 90 units the missile spawns already inside the dive
   radius, so `MissileAIUpdate` discards the `DistanceToTravelBeforeTurning` climb point on
   its first frame.
4. `CreateTransformMatrix` produced row 1 of `(cosy*cosp, siny, cosy*sinp)` instead of
   `(cosy*cosp, siny*cosp, sinp)`. The THRUST locomotor re-orients toward its velocity every
   frame through `TryToOrientInThisDirection3D`, so each frame read back a wrong forward
   vector and rotated from it.

## 3. Method notes for the next session

- Instrument the real render path. `drawModule.Update` runs only from
  `Drawable.BuildRenderList`, so a logic-only scratch test that calls `game.Update([])`
  without rendering never advances turret animation. That is what produced the original
  brief's misleading "frozen bone" trace.
- To find a mesh that does not appear, gate `ModelInstance.BuildRenderList` on an env var and
  render one sub-object at a time. That separated "not drawn" from "drawn degenerately" in
  one pass.
- Trace per frame, not just the first frame. Fault 2 looked correct on frames 0 and 1 and
  only flipped afterward.
- Read game INI straight out of `INI.big` with a short BIGF reader rather than guessing at
  data values. Extracted copies are under `/tmp/ini/`.

## 4. Reproducing

`EngagementCapture` block for `src/OpenSage.Tools.TestHarness/TestHarness.json`. `--no-build`
does not copy an edited config, copy it into
`src/OpenSage.Tools.TestHarness/bin/Debug/net10.0/` as well.

```json
"EngagementCapture": {
  "AttackerDefinition": "GLAVehicleScudLauncher",
  "TargetDefinition": "CarAsian1",
  "Separation": 300,
  "DesiredSpanFraction": 0.52,
  "Frames": 220
}
```

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/OpenSage.Tools.TestHarness -- --headless \
--built-in-scenario EngagementCapture
python3 scripts/annotate_engagement_gif.py /tmp/opensage_engagement out.gif
```

The committed `TestHarness.json` default is unchanged (`{ "Frames": 192 }`); the block above
is a local edit.

## 5. Gates

`dotnet build src`, `dotnet format src --verify-no-changes`, and
`dotnet test src --filter Category!=Interactive` all pass. The test run reports 7 failures,
all pre-existing on this branch and unrelated: `CurFileTests.CanReadAniCursors`,
`SteamLocatorTests.FindInstallations FindsGeneralsOnThisMachine`,
`BseFileTests.CanRoundtripBases`, `MapFileTests.CanRoundtripMaps`,
`ScbFileTests.CanRoundtripScbFiles`, `WndFileTests.CanReadWndFiles`,
`P2ScenarioMatrixTests.Matrix_AllCells_AIPlaysLegalGame`.

## 6. Open items

Nothing blocking, but these were noticed and not addressed.

- `MissileAIUpdate.SteerTowardTargetPoints` clears every queued target point when it enters
  the terminal dive. The original engine treats `DistanceToTargetBeforeDiving` as "ignore
  `PreferredHeight`", not "discard the climb waypoint". Behaviour only diverges when a
  missile spawns inside the dive radius, which the SCUD does below its minimum attack range.
- `MissileAIUpdate.Update` still carries a `// TODO: DistanceToTargetBeforeDiving` comment
  next to the dive handling in `SteerTowardTargetPoints`. Reconcile or delete it.
- Fault 4 affected every THRUST-appearance locomotor, not just the SCUD missile. Jets and
  other missiles were re-oriented through the same broken helper and are worth a visual pass.
- `EngagementCapture` frames the camera from actor positions only, so a tall trajectory
  leaves the viewport. Consider fitting the camera to projectile positions from a first pass.
- The capture defaults still describe a missile-defender engagement
  (`docs/engagement-capture-findings.md`). The SCUD configuration lives only in
  `docs/scud-launch-validation.md`.

## 7. Pathing

No pathing work was done in this session. Nothing above touches ground movement: fault 4 is
in a helper reached only from `Locomotor.TryToOrientInThisDirection3D`, which
`MoveTowardsPositionThrust` alone calls. Ground locomotors orient through separate code in
`Locomotor.cs`.
