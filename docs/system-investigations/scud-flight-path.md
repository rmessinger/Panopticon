# SCUD flight path deep dive

How the flight path of a ballistic missile (the GLA SCUD Launcher's `SCUDMissile`) is
calculated. Sources: base Generals `INI.big` (`Data/INI/Object/FactionUnit.ini`,
`Data/INI/Locomotor.ini`, `Data/INI/GameData.ini`), the engine code, and a measured
300-unit engagement trace. Related: `docs/system-investigations/scud-launch-transfer.md` (launch sequence
defects), `docs/system-investigations/engagement-capture-findings.md` (capture tuning).

## 1. Summary

Despite the `KindOf = BALLISTIC_MISSILE` tag, the SCUD's path is not a ballistic
parabola. It is the emergent result of a physics simulation steered by three cooperating
systems:

1. `MissileAIUpdate` builds a two-point waypoint chain at ignition and picks the active
   goal each frame (Object definition `SCUDMissile`, `FactionUnit.ini`).
2. The THRUST locomotor `SCUDMissileLocomotor` converts the active goal into a motive
   force each frame (`Locomotor.MoveTowardsPositionThrust`).
3. `PhysicsBehavior` integrates gravity plus that force (Euler, per frame).

The only genuinely ballistic segment is the fall after fuel runs out. Contrast
`BezierProjectileBehavior` (Nuke Cannon shells): that one computes a prescribed
analytic arc where position is a function of travel fraction, no physics involved.

## 2. The data

Per-frame values assume the game's 30 logic frames per second.

| Setting | Value | Per frame |
|---|---|---|
| Speed | 200 dist/sec | 6.67 |
| MinSpeed | 100 dist/sec | 3.33 |
| Acceleration | 675 dist/sec2 | 0.75 |
| Braking | 0 | 0 |
| TurnRate | 540 deg/sec | 18 deg |
| MaxThrustAngle | 45 deg (absolute, not a rate) | 45 deg |
| PreferredHeight | 120 above ground | 120 |
| PreferredHeightDamping | 0.7 | 0.7 |
| CloseEnoughDist | 1.0 (code default, not set in INI) | 1.0 |
| InitialVelocity | 50 dist/sec | 1.67 |
| DistanceToTravelBeforeTurning | 200 | 200 |
| DistanceToTargetBeforeDiving | 200 | 200 |
| FuelLifetime | 5000 ms | 150 frames |
| IgnitionDelay | 0 | 0 |
| TryToFollowTarget | No | |
| Gravity (GameData.ini) | -64 dist/sec2 | -0.0711 |
| PhysicsBehavior Mass | 1, AerodynamicFriction unset (0) | |

## 3. Launch

`ProjectileNugget.CreateProjectile` spawns the missile at the weapon launch bone
(`Drawable.GetWeaponLaunchBoneTransform`): the bone's root-relative transform composed
with the object's model and world transforms. Models without a launch bone fall back to
the firer's own transform. The launcher's turret must already be aimed
(`PreAttackWeaponState` gates firing on `IsTurretAimed`), which for the SCUD includes
the erector's `FirePitch` of 90 degrees.

Caveat: the launch transform reads `ModelInstance.RelativeBoneTransforms`, which
include the turret aim offset only after a render pass has refreshed them
(`W3dModelDraw.UpdateTurretAim` runs from the render path). Headless logic-only tests
therefore launch along the bind pose (shallow), while rendered runs launch near
vertically. Both traces below and the shared SCUD GIFs reflect this distinction.

At ignition (after `IgnitionDelay`, zero for the SCUD) `MissileAIUpdate` does three
things:

1. Queues the climb waypoint: muzzle position plus 200 world units along the muzzle
   forward vector (`DistanceToTravelBeforeTurning`).
2. Queues the target's current position as the final waypoint.
3. Applies a one-shot ignition impulse of 50 dist/sec along the muzzle forward vector
   (`ApplyForce(mass * InitialVelocity)`).

`TryToFollowTarget = No` means the final waypoint is frozen: the SCUD is
fire-and-forget, and a moving target is only hit if it stays inside the terminal dive
cone.

## 4. Per-frame pipeline

Update order per frame (see `GameObject.Update`): Order0 `MissileAIUpdate`, Order1
`PhysicsBehavior`. Steering forces are therefore applied before the frame's
integration, and gravity is added after the thrust force within the same accumulator.

MissileAIUpdate (steer):

1. Dive check. Within `DistanceToTargetBeforeDiving` (200) of the live target, the
   missile sets the locomotor's `PreciseZPosition` flag (stop holding
   `PreferredHeight`) and, in the current implementation, also discards all queued
   waypoints and aims straight at the target. The original engine treats the setting as
   "ignore PreferredHeight" only; this divergence and its trigger condition are
   recorded in `docs/system-investigations/scud-launch-transfer.md` section 6.
2. Waypoint consumption. Intermediate waypoints are dropped when the missile is within
   `CloseEnoughDist` (1.0) or has overshot them (velocity dot to-waypoint negative).
   A missile covering ~6 units per frame would otherwise U-turn back toward a waypoint
   it crossed mid-step. The final waypoint is exempt so an overshoot still homes back.
3. Hand-off. The active waypoint goes to
   `Locomotor.LocoUpdateMoveTowardsPosition`, which dispatches on
   `Appearance = THRUST` to `MoveTowardsPositionThrust`.

Locomotor (thrust toward goal):

1. Speed envelope. Desired speed clamped to [MinSpeed, Speed]; acceleration capped at
   0.75 per frame (braking is 0).
2. Altitude hold. Unless `PreciseZPosition` is set, the goal Z is replaced by a damped
   step toward `PreferredHeight + ground height`: `goalZ = z + 0.7 * (120 + ground -
   z)`. The missile approaches cruise altitude asymptotically at 70 percent per frame.
3. Thrust direction. `CalcDirectionToApplyThrust` solves a simplified quadratic for the
   smallest positive time-to-goal given current velocity plus a gravity preload
   (gravity is added to velocity before the solve, so thrust actively compensates it),
   and returns the direction that would close the angle between velocity and goal.
4. Thrust authority. `TryToRotateVector3D` limits the thrust direction to at most
   `MaxThrustAngle` (45 degrees) from the current forward axis: a missile pointing up
   cannot instantly thrust sideways.
5. Nose orientation. The model is re-oriented toward the current velocity at up to 18
   degrees per frame (`TryToOrientInThisDirection3D`, the path through the formerly
   broken `CreateTransformMatrix`).
6. Force. `accelVec = thrustDir * maxAccel - velocity * damping` with
   `damping = clamp(maxAccel / maxSpeed, 0, 1)`, applied via `ApplyMotiveForce`.

PhysicsBehavior (integrate):

1. Gravity: -0.0711 per frame added to the Z acceleration. While a motive force is
   active, `ApplyForce` filters other forces to their lateral (XY) component, but Z is
   never filtered, so gravity always passes through.
2. Friction: none for the SCUD (AerodynamicFriction default 0, only Mass = 1 set).
3. Euler integration: velocity += acceleration, position += velocity, then the
   acceleration accumulator resets for the next frame.

## 5. Termination

Every frame in flight, `BezierProjectileBehavior.CheckForHit` sweeps the frame's travel
segment (`DistancePerFrame`) against objects and the ground. An object hit executes the
detonation FX and delivers the warhead damage (the firing weapon skips its own damage
nuggets for projectile-delivered warheads, see `WeaponTemplate.DeliversDamageViaProjectile`).
A ground hit detonates the same way. The launcher itself is skipped via the recorded
`LauncherObjectId`.

Fuel out at 150 frames (5000 ms): the SCUD sets no `DetonateOnNoFuel`, so steering
simply stops. Thrust no longer opposes gravity and the missile falls ballistically
until the ground sweep detonates it. Weapons that do set `DetonateOnNoFuel` (for
example the Dragon's flame) deliver the warhead in place at fuel-out instead.

## 6. Measured profile

A 300-unit engagement (SCUD Launcher versus Crusader, headless logic-only run, so the
launch follows the bind-pose muzzle angle rather than the rendered 90-degree erector).
One row per eight frames; planar distance is to the target:

| Frame | X | Z | VelZ | Planar | Phase |
|---|---|---|---|---|---|
| 48 | 582.8 | 31.5 | +0.7 | 317 | launch (post PreAttack aim) |
| 56 | 613.4 | 43.5 | +1.8 | 287 | climb |
| 64 | 654.0 | 60.6 | +2.3 | 246 | climb |
| 72 | 697.7 | 81.0 | +2.7 | 202 | entering dive radius |
| 80 | 742.5 | 95.3 | +0.1 | 157 | apex |
| 88 | 785.0 | 79.3 | -2.8 | 115 | dive |
| 96 | 830.2 | 55.5 | -3.1 | 70 | dive |
| 104 | 877.8 | 30.5 | -3.2 | 22 | terminal |

Total flight 60 frames. Horizontal speed averages 5.8 units per frame (~175 dist/sec,
approaching the 200 cap from below given the 0.75 acceleration). The apex at Z 95 never
reaches the PreferredHeight goal (~120 plus ground) because the missile enters the 200
dive radius first and the altitude hold releases. Terminal sink rate is about 3.1 per
frame (~94 dist/sec).

In rendered runs the same simulation launches near vertically (FirePitch 90), spends
its climb waypoint rising, arcs over under the 18-degree-per-frame turn limit plus the
45-degree thrust authority limit, then repeats this profile from a higher apex.

## 7. Why it flies this shape

Each parameter maps to a visible feature of the arc:

- InitialVelocity and FirePitch set how much altitude is bought before thrust takes
  over.
- TurnRate and MaxThrustAngle bound how sharply the missile can roll over from the
  climb into the transit; too little and it overshoots downrange before turning.
- PreferredHeight with damping 0.7 produces the flat-ish cruise segment between climb
  and dive.
- DistanceToTargetBeforeDiving fixes where the nose drops.
- Gravity is a constant headwind the thrust solver cancels while fuel lasts; it only
  shapes the path after fuel-out.

The engine computes no path; it computes a force. Any change to mass, gravity,
locomotor constants, or fuel changes the shape, which is why the path is validated by
tracing positions per frame rather than by comparing against a closed-form curve.
