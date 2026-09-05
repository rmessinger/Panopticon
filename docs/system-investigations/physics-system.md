# General physics system deep dive

The shared per-object physics system (`PhysicsBehavior`): what exists, how it runs, what
it interacts with, and where the port has gaps. Companion to
`docs/system-investigations/scud-flight-path.md`, which covers the missile application
of this system in detail. Sources: engine code, base Generals INI data, and a measured
drop trace.

## 1. Summary

Physics in OpenSAGE is one optional behavior module per object, not a standalone
simulation pass. Nearly every movable object carries one: 221 `PhysicsBehavior` blocks
in `FactionUnit.ini`, 106 in `CivilianUnit.ini`, versus 2 in buildings. The module is a
translation-only Euler integrator: "rigid body" in name, but rotation is handled with
hand-set yaw/pitch/roll rates rather than torques (a todo inherited from the original
engine). It runs in update Order1, after AI/steering (Order0) and before trackers,
which is why steering forces set during Order0 are integrated in the same frame they
are applied.

The system is intentionally cheap: objects whose velocity and acceleration are zero,
that are on the ground, not overlapping anything, and have no active rotation or
motive force, sleep forever (`CalculateSleepTime`) until a force wakes them.

## 2. Data

Per-object fields (`Behavior = PhysicsBehavior`), with code defaults:

| Field | Default | Notes |
|---|---|---|
| Mass | 1.0 | only divisor for forces; Crusader uses 50 |
| AerodynamicFriction | 0 | airborne, proportional to velocity |
| ForwardFriction | 0.15 | grounded 2D, along heading |
| LateralFriction | 0.15 | grounded 2D, across heading |
| ZFriction | 0.8 | parsed but never applied (see gaps) |
| CenterOfMassOffset | 0 | steers pitch rate toward nose-up/down |
| AllowBouncing | false | ground bounce on impact |
| KillWhenRestingOnGround | false | used by drones |
| AllowCollideForce | true | written to a flag nothing reads |
| MinFallHeightForDamage | 40 | converted to a speed threshold; currently NaN (see gaps) |
| FallHeightDamageFactor | 1.0 | multiplies fall damage |
| PitchRollYawFactor | 2.0 | legacy correction for double-applied rates |
| GravityMult | 1.0 | per-object gravity scale |

Global constant: `GameData.ini` `Gravity = -64` dist/sec2 (the file also carries a
commented `;Gravity = -32` alternative), converted at parse time to -0.0711 per frame2
at 30 logic frames per second. `GroundStiffness` (default 0.5) scales bounce strength.
Zero Hour adds shock/stun fields (`ShockResistance`, `ShockMaxYaw/Pitch/Roll`,
`ShockStandingTime`, `ShockStunnedTime*`), unused by Generals data.

## 3. Per-frame pipeline

`PhysicsBehavior.Update`, in order:

1. Gravity is added to the Z acceleration (per-object `GravityMult` applied at
   construction).
2. Friction, one of two regimes. Grounded (or `ApplyFriction2DWhenAirborne`, set by
   locomotors like the SCUD's): lateral and forward 2D friction computed from the
   velocity split against the heading, scaled by mass, applied as force; forward
   friction is skipped while a motive force is active. Airborne: aerodynamic friction
   as `velocity * -aeroFriction` directly on acceleration, plus yaw/pitch/roll
   damping.
3. Integration: velocity += acceleration, position += velocity. A NaN position
   destroys the object. Units braking under locomotor control integrate only Z
   (projectiles cheat in Z too).
4. Optional rotation: yaw/roll/pitch rates (scaled by `PitchRollYawFactor`) are
   applied as matrix rotations; a nonzero `CenterOfMassOffset` scales the pitch rate
   toward vertical. Who sets the rates: locomotors (turning), death behaviors
   (`JetSlowDeathBehavior` spiral, transport exit toss), parachute contain.
5. Ground handling. Terrain height at the new position (plus carrier deck offset if
   any). If at or below ground: the excess Z velocity is removed (the slope-friendly
   detail: only the overshoot, so driving downhill keeps a small negative Vz), the
   position is clamped, and `AllowToFall` is cleared. If above ground while flagged
   `StickToGround` (set from the locomotor template) and not `AllowToFall`, the object
   snaps down to the ground: walking units hug terrain. Freefall-flagged objects get
   disabled with `DisabledType.Freefall` until they land.
6. Bounce: if `AllowBouncing` and the object crossed into the ground, a bounce force
   of `|Vz| * GroundStiffness * Mass` is queued and applied next frame; Generals
   returns after queuing, Zero Hour additionally tests the stunned unit for
   destruction and re-arms bouncing.
7. Landing effects: on the airborne-to-grounded transition the object receives a
   ground collision (`OnCollide(null, ...)`, relayed to the container, which is how
   parachute landing is detected) and, in the original engine, falling damage. See
   gaps: falling damage is currently unreachable. `KillWhenRestingOnGround` kills
   grounded, slow, airborne-type objects (drones shot down).
8. Sleep recomputation.

## 4. Forces

Two channels into the same acceleration accumulator:

- `ApplyForce(force)`: F = ma division, then added to the accumulator. While a motive
  force is active, the X/Y components are filtered to the lateral direction relative
  to the object's heading; Z is never filtered (gravity always passes).
- `ApplyMotiveForce(force)`: same, plus it marks the object as motive for 10 frames
  (LogicFramesPerSecond / 3). Motive status exempts the object from forward friction
  and filters other forces as above. Locomotors re-apply every frame, so the window
  mostly matters for anything else pushing the object meanwhile.

Current force sources: locomotive movement (`Locomotor.MoveTowardsPositionThrust` and
friends), the missile ignition impulse (`MissileAIUpdate`), and debris toss
(`ObjectCreationList`, `SendItFlying` disposition: a mass-scaled random impulse).
`Mass` adds `Contain.ContainedItemsMass` for transports, but no module implements
`IContainModule` yet, so contained mass is unwired (see gaps).

## 5. Collision

Broad phase: the partition cell manager collects candidate pairs from dirty objects,
checks exact colliders, then calls `OnCollide` on both objects.

`PhysicsBehavior.OnCollide` resolves, in order: projectiles handle their own collision
and return; ground collisions relay to the container (parachute detection); objects
ignore their own containers and cargo; two parachuting objects ignore each other;
explicit ignore lists apply; and the special case that infantry touching an unmanned
vehicle captures it as the new pilot. Then the crush system: `CanCrushOrSquish` by
crusher/crushable levels, with front/back/total crush-point geometry deciding which
end of the crushee folds, crush damage on first overlap, and overlap accounting
(`_currentOverlap`/`_previousOverlap`, one deep).

What is missing: the physical push-apart. The doc comment on `OnCollide` still
describes "apply proportional forces to push them apart", but no such code exists in
the port, and the `AllowCollideForce` flag that gated it is written and never read.
Consequences: colliding units do not exchange momentum or shove each other; physical
unit separation is entirely up to pathfinding and locomotion. Object collisions are
logical events (crush, projectile impacts, topple triggering via the other object's
velocity), not simulated contacts.

## 6. Measured behavior

A Crusader (Mass 50) spawned 300 world units above flat ground, headless run, one row
per eight frames:

| Frame | Z | VelZ | Airborne |
|---|---|---|---|
| -1 | 318.75 | 0.000 | yes |
| 23 | 299.12 | -1.636 | yes |
| 47 | 238.54 | -3.342 | yes |
| 71 | 136.99 | -5.049 | yes |
| 89 | 33.95 | -6.329 | yes |
| 92 | 18.75 | -2.329 | landed |
| 95+ | 18.75 | 0.000 | no |

Acceleration is exactly -0.0711 per frame2 (gravity, no drag: the Crusader sets no
aerodynamic friction). Impact at -6.47 per frame (~194 dist/sec), matching
sqrt(2 g h). The clamp removes only the overshoot (VelZ -2.33 on the landing frame,
zero the next), the tank does not bounce (no `AllowBouncing`), and it takes no fall
damage even though a direct `AttemptDamage` probe with `DamageType.Falling` for 100
works (480 to 380 HP). Explanation below.

## 7. Gaps and dead code

Ordered by gameplay impact:

1. Falling damage is unreachable. The default
   `MinFallSpeedForDamage = HeightToSpeed(40)` computes
   `sqrt(2 * Gravity * 40)` with `Gravity` = -0.0711, so the threshold is NaN, and
   `netSpeed > 0` is never true. No object in the current game can take falling
   damage (paradropped units, vehicles tossed by shockwaves are all affected). The
   original engine evidently used the gravity magnitude; the port needs an absolute
   value (and the same fix inside the `MinFallHeightForDamage` INI field). Verified
   by the trace in section 6 plus the direct-damage probe.
2. Object-object push-apart is not ported (section 5). Two colliding objects never
   physically separate each other.
3. `AllowToFall` is cleared on ground contact but never set anywhere. The intended
   purpose (release a StickToGround object mid-air, for paradrops and cargo unloads)
   has no caller, so `StickToGround` objects snap to terrain the moment physics sees
   them above it unless flagged `IsInFreefall`.
4. `ZFriction` is parsed and clamped but never applied anywhere.
5. `PhysicsBehaviorModuleData` parses `FirstHeight`, `SecondHeight`, and
   `OrientToFlightPath`, which belong to `DumbProjectileBehavior` (ported as
   `BezierProjectileBehavior`, where the live copies are). Accepted and ignored here.
6. `ContainedItemsMass`: `IContainModule` has zero implementations in the codebase,
   so `GameObject.Contain` is always null and transport cargo never adds mass.
7. Cosmetic: bounce sound is a documented no-op; Zero Hour shockwave stun fields are
   parsed but nothing in Generals data sets them (stun relief logic itself works when
   a stun is set programmatically).

## 8. Interactions

- Locomotors: `SetPhysicsOptions` pushes locomotor friction configuration into
  physics (extra 2D friction, `UltraAccurate` mode, airborne friction flag,
  `StickToGround`), then movement code applies motive forces every frame. See the
  SCUD deep dive for the thrust loop.
- Projectiles: `MissileAIUpdate`/`BezierProjectileBehavior` objects carry physics
  for flight; projectile collisions bypass `OnCollide` via
  `ProjectileHandleCollision`.
- Death behaviors: jets spiral via `JetSlowDeathBehavior` roll/pitch rates;
  `ToppleUpdate` reads the impacting object's velocity to topple props.
- Debris: `ObjectCreationList` creates physics-driven debris pieces with a random
  `SendItFlying` impulse.
- Parachutes: `ParachuteContain` sets pitch/roll rate limits; landing is detected
  through the ground-collision relay to the container.
