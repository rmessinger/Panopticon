# Engagement capture: findings

Written for the next agent working on visual validation. It records why the earlier
`vg_missile_defender.gif` was unusable, what the real cause turned out to be, and what the
committed framework now does. Companion documents: `docs/screenshots.md` (recipes),
`docs/visual-validation-brief.md` (the branch context).

## What was wrong

The earlier capture showed a map-wide view with both units as specks and annotation rings on
empty terrain. Two separate problems were conflated:

1. A renderer bug, which was the reason nothing looked close up.
2. No committed framework. The capture came from a scratch `GameFact` test that was deleted
   after use, so nothing was reproducible and the annotations were positions guessed after the
   fact rather than positions the engine reported.

## Root cause: one global constants buffer shared by every render pass

`GlobalShaderResourceData` held a single uniform buffer and a single resource set. Both the
shadow pass and the forward pass wrote their own view-projection matrix into it during one
command list. On Vulkan the second `UpdateBuffer` was not visible to draws already recorded, so
the forward pass drew with the shadow pass's light-space orthographic matrix. The result is a
map-wide view no matter what the camera does.

Symptoms this produces, all of which were previously misdiagnosed as camera or framing faults:

- Camera height changes have no visible effect.
- `Scene3D.Camera.ViewProjection` is correct when logged at render time, yet the image does not
  match it.
- Units render "as specks at close zoom".

Diagnostic that proved it: log both `UpdateGlobalConstantBuffers` calls per frame and compare
`m11`. The shadow pass wrote roughly `0.0000` and the forward pass `2.2094`, and the first value
was the one visible on screen. Temporarily making the shadow pass write
`scene.Camera.ViewProjection` produced a correct close-up immediately.

Fix: each pass acquires its own constants buffer and resource set from a per-frame pool
(`AcquireSlot`), reset by `BeginFrame()` called from `RenderPipeline.Execute`. If a
"camera is ignored" symptom reappears, check `BeginFrame` is still called once per frame.

Note: this was also the first time the shadow pass rendered with its own matrix. Shadow quality
was not separately verified.

## The framework

`EngagementCapture` (`src/OpenSage.Tools.TestHarness/EngagementCaptureRunner.cs`) is a headless
built-in scenario. It spawns an attacker and a target, fits the camera to them, commands the
weapon, captures one PNG per logic frame, and writes `manifest.json` with per-frame screen
projections, marker radii, health, and projectile positions.

`scripts/annotate_engagement_gif.py` draws the overlay from that manifest, so labels cannot
drift from what the engine actually rendered.

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
dotnet run --project src/OpenSage.Tools.TestHarness -- \
  --headless --built-in-scenario EngagementCapture

python3 scripts/annotate_engagement_gif.py /tmp/opensage_engagement out.gif
```

The runner exits non-zero on a missing definition (1), a camera that cannot be fitted or an
attacker with no weapon (4), or no projectile ever observed (5). A silently empty capture is not
possible.

## Things that took a while to learn

Camera framing order. `SnapHeightAboveGround(h)` must run before `LookAt(pos)`. The tactical
view's map constraint is recomputed per height, so aiming first clamps the look-at point toward
the map centre and the subjects end up off-screen and frustum-culled.

Camera fit. On-screen size is roughly inversely proportional to height, so
`height *= measuredSpan / desiredSpan` converges in a few iterations.

Spawning. Sample `Terrain.GetGroundHeight`, set the transform, then call `UpdateColliders()`
immediately. Colliders otherwise stay at the origin and the object is culled.

Commanding fire. Set the weapon target directly
(`attacker.CurrentWeapon.SetTarget(new WeaponTarget(scene.GameObjects, target.Id))`). An
`AttackObject` order performs a move step that suppresses fire and drifts the framing that was
just fitted.

Ownership. The target belongs to the civilian player so it cannot return fire. Players are
`[neutral, civilian, USA, China, GLA]` for three `PlayerSetting` entries.

Labels. `ObjectDefinition.DisplayName` is a localization key such as `OBJECT:MissileTeam`; run it
through `.Translate()`. `Player.Side` returns raw faction names such as `FactionCivilian` and is
misleading in a label, so the annotator uses ATTACKER, TARGET and MISSILE roles instead.

## Composition: what makes the units recognizable

The unit itself is small relative to its own firing FX. `FX_BuggyMissileIgnition`, which this
weapon uses, grows to roughly 39 world units, about eight times the Missile Defender's own size,
so orientation decides whether the shooter is readable or lost in the bloom.

`AxisAngleDegrees` rotates the attacker-to-target axis around the midpoint. Measured results at
120-unit separation:

| Angle | Result |
|---|---|
| 0 | Horizontal flight, shooter washed out inside the launch bloom |
| 30 | Diagonal flight, shooter still washed out |
| 90 | Vertical flight, shooter clear in the foreground |
| 180 | Horizontal flight, shooter clear in silhouette, missile clear in mid-air |

180 is the default: the pair reads left to right with the target on the left, the backblast vents
away behind the shooter, and the missile crosses open ground.

`HideHud` (default true) closes every GUI window before capturing, otherwise the minimap corner
intrudes on the crop.

Separation 120 with `DesiredSpanFraction` 0.70 settles the camera at height 115 and puts a
projectile in 54 of 86 frames. The earlier 60-unit separation gave only 36 of 110.

The code defaults describe the original missile-defender engagement
(`AmericaInfantryMissileDefender` vs `ChinaTankBattleMaster`, separation 120, 150 frames).
Other units need per-unit tuning:

- Artillery has long pre-attack plus slow, lofted flight: Nuke Launcher and SCUD want 220+
  frames. The SCUD additionally needs `Separation` above its `MinimumAttackRange` of 200
  (300 works) and a wider fit (`DesiredSpanFraction` 0.52); its full configuration lives in
  `docs/scud-launch-transfer.md`.
- Short-range flame weapons (Dragon Tank, Angry Mob) need separation 60 or less.
- Instant-hit weapons (machine guns, Ranger, Pathfinder) produce no logic projectile: the
  runner exits 5 with the manifest still written; the capture shows tracers and muzzle FX.
- Armor matchups matter: the Pathfinder's sniper rifle does near-zero damage to a vehicle
  target (TruckArmor); pick a target the weapon can actually hurt.

The camera is fitted to the two actors only, so a tall trajectory (SCUD arc) leaves the
viewport mid-flight; fitting to projectile positions needs a probe pass (open item).

## Annotator details worth keeping

The GIF uses one shared palette; per-frame palettes make static scenery shimmer. The palette is
derived from a montage sampled across the whole sequence. Deriving it from frame 0 alone
quantizes mid-sequence colors, the projectile marker and the launch and impact FX, into gray
blotches. Forcing role colors into a reduced palette instead produces harsh banding in the smoke.

The crop is grown to a minimum fraction of the viewport so labels fit, the font is fitted to the
longest label and capped, labels are placed on whichever side of the marker has room, and
overlapping boxes are nudged apart.

The annotator reserves exact annotation colors in the shared palette so markers and impact
flashes stay saturated, and flashes `IMPACT -N HP` for three frames on every damage frame.
The target's HP lives in its label. Projectiles are not annotated: the engine draws them
(mesh, tracer streak, or stream ribbon), and the overlay crosshair only obscured the flight.
They still size the GIF crop so the whole trajectory stays in frame.

Capture a few seconds past the last expected impact (default 192 frames) so the final hit's
FX and smoke conclude before the sequence ends.

## Projectile visuals: streams, shell tracers, impact FX

The engagement capture surfaced that projectile-weapon visuals had three holes, all fixed on
this branch:

1. The weapon's `ProjectileDetonationFX` only executed for ground (position) hits; object
   hits played nothing unless the victim's armor carried its own DamageFX. The missile
   defender's detonation now plays on the car (2300-4600 bright pixels per impact).
2. `W3dProjectileStreamDraw` was an unimplemented stub, so the flame/toxin/cleanup ribbons
   never rendered. `ProjectileStreamManager` now draws them: one camera-facing textured
   ribbon per live stream projectile, parameters (Texture, Width, TileFactor, ScrollRate,
   MaxSegments) from the stream object definition's module data, additive-blended. The hack
   "beam" weapons reuse stream data but fire no projectile, so they still show nothing.
3. Tank cannons use NoTracer FireFX in the original data (the shell mesh was the only flight
   visual). Engines wanting a streak get one: `ProjectileNugget.CreateShellTracer` attaches a
   tracer that follows the shell, scaled from the data's own tank-tracer values
   (`WeaponFX_GenericTankGun`: Length 20, Width 0.5) against the shell's speed and radius.
   Weapons with a stream or an exhaust trail are excluded.

Render-bucket gotcha that cost an hour: `RenderBucket.AddObject` skips objects whose
`MaterialPass.ForwardPass` is null, so a manager whose material depends on runtime data (the
first ribbon's texture) must defer `AddObject` until that material exists.

## Verifying a capture before sharing

Check the manifest, not just the pictures. Target `Health` should fall across the sequence and a
non-trivial number of frames should list a projectile. The runner prints both.

## Follow-up bug found by this capture: impact effect at launch

The first capture with this framework showed the target's damage FX playing the moment the
missile was fired. Cause: `FiringWeaponState` executed every weapon nugget at fire time,
including the `DamageNugget`s of legacy-converted Generals weapons, so the warhead's damage
(and the victim's hit FX) landed at launch while the missile was still at the muzzle. The
projectile's warhead then fired the same nuggets again at detonation (double damage).

Fix: a weapon whose template contains a `ProjectileNugget` skips its `DamageNugget`s at fire
time (`WeaponTemplate.DeliversDamageViaProjectile`); the projectile delivers them at
detonation. `ProjectileDamageTimingTests` pins it: damage lands on the frame the missile
reaches the target, never the launch frame.

Removing the launch-damage crutch exposed two projectile types that never delivered at all:

- `DragonTankFlameProjectile` (`DetonateOnNoFuel = true`): fuel exhaustion killed the missile
  without firing its warhead. Fuel-out now delivers the warhead where the missile dies.
- `NukeCannonShell` (`BezierProjectileBehavior`): movement multiplied the per-frame `Speed`
  by frame duration again, a 30x slowdown, so the shell crawled and never reached anything.
  The collision sweep had the same units bug; it now uses actual per-frame travel (kinematic
  step for Bezier projectiles, physics velocity for MissileAIUpdate missiles).

The validation-ground baseline was re-captured after these changes: projectile weapons gained
their real flight frames (e.g. Missile Defender 4 to 47, Nuke Launcher 2 to 216, Dragon 6 to
18), and the Angry Mob got faster (17 to 2) because its molotovs no longer crawl.
