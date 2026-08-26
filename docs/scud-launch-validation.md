# SCUD launcher launch sequence: validation brief

Handoff document for a vision-capable agent validating the SCUD launcher's launch animation
on branch `minor-visuals`. Companion context: `docs/visual-validation-brief.md` (general
known-good artifacts and rendering gotchas), `docs/engagement-capture-findings.md` (capture
framework).

## 1. What is being validated

The GLA SCUD Launcher (`GLAVehicleSCUDLauncher`) attacking a civilian car (`CarAsian1`) at 90
world units, captured headlessly one PNG per logic frame (30 fps). Current capture:
`/mnt/pit2/Network Storage/temp/vg_attack_GLAVehicleSCUDLauncher.gif` (67 frames, 170 ms per
frame; also frames under `/tmp/opensage_engagement/` from the latest run, if still present).

Expected sequence, from the game's own data:

1. The launcher's erector arm (bone `TURRETEL`) raises the stowed missile from horizontal to
   vertical over about 45 frames (TurretPitchRate 60 deg/sec, FirePitch 90 deg).
2. The missile launches from the arm's tip bone (`WEAPONA01`), initially upward.
3. It lofts, cruises near PreferredHeight 120, then dives onto the target
   (DistanceToTravelBeforeTurning 200, DistanceToTargetBeforeDiving 200).
4. The stowed missile mesh on the launcher hides during flight (WeaponHideShowBone MISSILE)
   and the warhead detonates on the car.

Reference: original-game SCUD launcher footage exists publicly (search "Generals SCUD
launcher"); the arm raises well before launch and the missile lifts vertically off the arm.

## 2. Reported problem (current state, commit afdc46b5)

User review of the current GIF:

- About 80 percent of the animation appears to be "the missile bay on top of the truck
  closing".
- The missile then fires directly from the front of the truck into the target.

## 3. Verified facts (logic traces, not guesses)

- Turret pitch DOES converge: pitch rises 0 to 1.57 rad at ~0.035 rad per frame from frame 15
  to 45. The fire gate holds the shot until aimed.
- The arm DOES move on screen: pixel-diff of the launcher region shows changed pixels rising
  about 120 px above the launcher between frames 10 and 44. (Whether the motion reads as
  "raising" or "closing" needs a vision judgment; the axis or sign applied in
  `W3dModelDraw.UpdateTurretAim` (`Quaternion.CreateFromYawPitchRoll(0, -TurretPitch, 0)`,
  src/OpenSage.Game/Logic/Object/Draw/W3dModelDraw.cs) is the suspect if it looks wrong.)
- Bone hierarchy is correct: `WEAPONA01` child of `TURRETEL`, child of `TURRET`, child of
  `CHASSIS`. `MISSILE` is also a child of `TURRETEL`.
- DEFECT (proven): the logic-side launch transform never sees the elevation.
  `Drawable.GetWeaponLaunchBoneTransform` returns the bone's `RelativeBoneTransforms` entry,
  and a frame-by-frame trace shows it frozen at `<577.9, 600.0, 30.6>` with forward `+X`
  while pitch rises to 90 deg. The missile therefore spawns at the unelevated bone: low, at
  the truck's front, pointing horizontally at the target. This exactly matches "fires
  directly from the front of the truck".
- The flight itself is correct after spawn: 21 frames, homing, dive, kill
  (logic-verified; warhead delivers, car destroyed).
- The stowed-missile hide sets the right dictionary values (verified by instrumentation);
  visual magnitude at this camera distance is only about 22 px, so judge it in the GIF.

## 4. Root-cause hypothesis for the launch bone (for the fixing agent, after visual review)

`W3dModelDraw.Update` sets `TURRETEL.AnimatedOffset.Rotation` during the render pass, and
`ModelInstance.Update` recomputes `RelativeBoneTransforms` from `ModelBoneInstance.Matrix`
(which includes `AnimatedOffset`). The logic-time query in `GetWeaponLaunchBoneTransform`
(src/OpenSage.Game/Client/Drawable.cs:305) evidently reads transforms that were never
recomputed from the animated offset, or recomputed before the offset was applied. Check:

1. Who calls `ModelInstance.Update` and in what order relative to
   `W3dModelDraw.Update` within a frame (`Scene3D.Update` vs `Drawable.Update` inside
   `BuildRenderList`).
2. Whether `ModelBoneInstance.IsDirty` propagation marks parents' children when only the
   parent's `AnimatedOffset` changes.
3. The simplest robust fix may be composing the launch transform from the yaw and pitch bone
   offsets directly at query time, independent of render timing.

Secondary question for the vision agent: does the arm motion itself look like a correct
vertical raise (arm pivot at the rear, missile tipping up), or does it rotate the wrong way
or about the wrong axis (which would read as a lid closing over the bay)? Compare against
reference footage.

Timing note: 45 frames of elevation before a 21-frame flight is data-consistent
(TurretPitchRate 60 deg/sec at 30 fps for 90 deg). The "80 percent is the animation" feeling
is partly this proportion, partly the launch defect hiding the payoff. If the original game
elevates during its approach or pre-attack concurrently, that is a fidelity follow-up, not a
defect in the mechanism.

## 5. Reproducing and analyzing

Reproduce the capture (config in `src/OpenSage.Tools.TestHarness/TestHarness.json` under
`EngagementCapture`; `--no-build` does NOT copy an edited config, copy it into
`src/OpenSage.Tools.TestHarness/bin/Debug/net10.0/` yourself):

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/OpenSage.Tools.TestHarness -- --headless \
--built-in-scenario EngagementCapture
python3 scripts/annotate_engagement_gif.py /tmp/opensage_engagement out.gif --duration 170
```

The manifest (`/tmp/opensage_engagement/manifest.json`) has per-frame screen positions,
health, and projectile sightings; damage frames and projectile lifespans come straight from
it. Share results by writing into `/mnt/pit2/Network Storage/temp/` (quote the path; spaces)
and report per-frame verdicts.

Scratch-test pattern for logic-side ground truth (spawn units, step, trace): see git history
`ScudBonesScratchTests` / `ScudTraceScratchTests` (deleted after use; do not commit).

## 6. Known rendering red herrings

- Per-frame terrain flicker and the fog band at camera heights 900-1500 are pre-existing
  (see `docs/visual-validation-brief.md` section 4); ignore them here.
- The annotator draws an attacker ring and target ring with labels; the IMPACT flash overlay
  is annotation, not engine FX.
- The shell-tracer streak (yellow-orange) follows cannon shells; the SCUD missile has an
  exhaust trail instead, so no tracer.
- Capture exit code 5 ("no projectile observed") is expected for instant-hit weapons, not a
  failure of the scenario.
