# SCUD launcher launch sequence: validation brief

Handoff document for a vision-capable agent validating the SCUD launcher's launch animation
on branch `minor-visuals`. Companion context: `docs/visual-validation-brief.md` (general
known-good artifacts and rendering gotchas), `docs/engagement-capture-findings.md` (capture
framework).

## 1. What is being validated

The GLA SCUD Launcher (`GLAVehicleSCUDLauncher`) attacking a civilian car (`CarAsian1`) at 300
world units, captured headlessly one PNG per logic frame (30 fps). Current capture:
`/mnt/pit2/Network Storage/temp/scud_launch.gif`, with the raw frames and manifest under
`/tmp/opensage_engagement/` from the latest run.

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

## 2. Reported problem (as of commit afdc46b5, now fixed)

User review of the GIF captured at that commit:

- About 80 percent of the animation appeared to be "the missile bay on top of the truck
  closing".
- The missile then fired directly from the front of the truck into the target.

## 3. Root cause

`W3dModelDraw.UpdateTurretAim` applied the turret pitch with
`Quaternion.CreateFromYawPitchRoll(0, -TurretPitch, 0)`.

`CreateFromYawPitchRoll(yaw, pitch, roll)` maps yaw to Y, pitch to X, roll to Z. W3D model
space is X forward, Y lateral, Z up. Passing the turret pitch as the second argument
therefore rotated `TURRETEL` about its own long axis, a roll, which on screen reads as the
erector rolling in place like a lid closing over the bay. The rotation left `+X` unchanged,
so the child launch bone `WEAPONA01` stayed at deck height pointing at the target and the
missile spawned horizontally out of the truck's front.

The fix is a rotation about the lateral axis:

```csharp
_turretPitchBone.AnimatedOffset.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -GameObject.TurretPitch);
```

At 90 degrees this maps `+X` to `+Z`, so the arm and the launch bone both point straight up.

The adjacent yaw line, `Quaternion.CreateFromYawPitchRoll(0, 0, TurretYaw)`, is correct: its
third parameter is rotation about Z, the model's up axis.

## 4. Corrections to the original analysis

The earlier hypothesis (stale `RelativeBoneTransforms`, `ModelInstance.Update` ordering,
`IsDirty` propagation) was a red herring. Verified by instrumenting the real render path:

- `ModelBoneInstance.IsDirty` includes `AnimatedOffset.IsDirty`, and `ModelInstance.Update`
  recomputes every `RelativeBoneTransforms` entry when any bone is dirty.
- `W3dModelDraw.Update` calls `UpdateTurretAim()` before the condition state's `Update`,
  which is what calls `Model.Update`.
- Children of `TURRETEL` demonstrably inherit the offset once the axis is right: the launch
  bone's world forward went from `<-1, 0, 0>` to `<-0.035, 0, 0.999>`.

The trace that showed a frozen launch bone was misleading because `drawModule.Update` runs
only from `Drawable.BuildRenderList`, that is, during the render pass. A logic-only scratch
test that calls `game.Update([])` without rendering never advances turret animation at all.
Instrument the headless capture scenario instead of writing logic-only scratch tests.

## 4a. Second defect: the stowed missile never rendered

The erector rose empty. `W3dModelDraw.UpdateBoneVisibilities` tested
`Drawable.HiddenSubObjects.ContainsKey(name)`, hiding the bone whenever a key was present
regardless of its value. `UpdateProjectileBoneFeedback` writes `MISSILE = false` every frame
while a round is loaded, so the stowed missile was hidden permanently from the first
condition-state update onward. `ShownSubObjects` had the same key-presence bug.

Both dictionaries record a state, not an intent, so the fix reads the value. The lookup also
matches the bone name as well as the sub-object name, matching `ModelInstance.IsHidden`.

## 4b. Third defect: no launch arc at 90 units

`SCUDLauncherGunExplosive` and `SCUDLauncherGunToxin` have `AttackRange = 350` and
`MinimumAttackRange = 200`. At the original 90-unit separation the missile spawns already
inside `DistanceToTargetBeforeDiving` (200), so `MissileAIUpdate` enters its terminal dive on
the first frame, discards the `DistanceToTravelBeforeTurning` climb point, and flies straight
at the target. Capture the SCUD at 300 units, which is a legal firing range, to see the
lofted arc.

## 4c. Verified result

Capture after the fixes (119 frames, SCUD vs `CarAsian1` at 300 units,
`DesiredSpanFraction` 0.8): the stowed missile is visible on the deck, the erector raises it
to vertical over frames 0 to 44, it launches at frame 45, climbs to an apex around frame 90,
dives, and destroys the car (300 to 0 HP). Artifact:
`/mnt/pit2/Network Storage/temp/scud_launch.gif`.

## 5. Reproducing and analyzing

Reproduce the capture with this `EngagementCapture` block in
`src/OpenSage.Tools.TestHarness/TestHarness.json` (`--no-build` does NOT copy an edited
config, copy it into `src/OpenSage.Tools.TestHarness/bin/Debug/net10.0/` yourself):

```json
"EngagementCapture": {
  "AttackerDefinition": "GLAVehicleScudLauncher",
  "TargetDefinition": "CarAsian1",
  "Separation": 300,
  "DesiredSpanFraction": 0.8,
  "Frames": 220
}
```

( ):

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
