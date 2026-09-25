# Visual validation brief: Panopticon minor-visuals

Context document for a vision-capable model validating recent rendering and simulation work.
Read together with `docs/screenshots.md` (capture recipes) and `docs/maps.md` (map data).
Facts are current as of commit `1d5fbe5f` on branch `minor-visuals` (PR #28).

## 1. Repository state

- Repo: Panopticon (C# port of the SAGE engine, C&C Generals). Branch `minor-visuals`.
- Merged earlier: headless logic host (A1), tracer rounds, P6b test fixes.
- On this branch: validation-ground overlay tooltip, screenshot infrastructure, minimal HUD
  (minimap-only default) with pixel-snapped minimap outline, Missile Defender flight fix,
  camera framing contract test, capture documentation.
- Gates at HEAD: build 0 warnings, 918 tests passed (7 skipped Interactive), format clean,
  validation-ground headless run exit 0 (30 measured, 0 regressed, 1 known gap).
- One recovered git stash exists (`stash@{0}`, "RECOVERED"): unrelated roadmap-doc WIP from
  the `docs/p3-p6-delta-fixes` branch. Do not drop it.

## 2. The headline change to validate: missile flight

Bug: `MissileAIUpdate` handled ignition FX and hit checks but never moved the missile. The
THUST locomotor was never driven, so gravity was unopposed: the missile appeared at the
muzzle and fell straight down through the ground.

Fix (commit `c88af49d`): on ignition, apply the `InitialVelocity` impulse along the muzzle
direction, then each frame steer the SET_NORMAL (THRUST) locomotor toward queued target
points (pre-turn point, then a point tracking the live target while `TryToFollowTarget`),
until `FuelLifetime` expires. Waypoints are consumed when reached or passed.

Latent engine bugs fixed in the same commit:
- `Matrix4x4Utility.RotateZ(s, c)` wrote row 2's result into `M32` instead of `M22`
  (non-orthonormal rotations, orientation matrices failing Decompose).
- `Drawable.GetWeaponLaunchBoneTransform` returned render-computed `AbsoluteBoneTransforms`
  (all-zero before the first render); now composes root-relative bone transform with the
  object transform.
- `ModelInstance` seeds bind-pose bone transforms at construction.
- `CreateTransformMatrix` direction assert uses a length tolerance instead of exact equality.

Logic-level proof: `MissileAIUpdateFlightTests` (Alpine Assault, Missile Defender vs
BattleMaster 120 units apart) asserts the missile closes to within 30 units of the target and
the warhead damages it. It passes. This is the strongest current evidence the fix works.

## 3. Expected visuals for the engagement

Scene: Alpine Assault, two spawned units on flat ground near world (600,600). USA Missile
Defender (red selection bracket when selected) fires at a China BattleMaster tank roughly
40-60 units away. Expected per shot cycle (~1s at 30 logic fps):

1. Muzzle flash at the defender (FiringA model condition, `FX_BuggyMissileIgnition`).
2. Missile model (`ExMsslTm`, a small rocket) launching roughly horizontally, arcing slightly
   up then over toward the tank; flight lasts ~6 logic frames.
3. Impact: detonation FX (`WeaponFX_RocketBuggyMissileDetonation`) and tank health bar drop
   (~40 HP per hit from 50000 in test setups).
4. Reload pause, then repeat.

Reference artifacts in the shared viewer directory (`/mnt/pit2/Network Storage/temp/`,
served at https://files.reid-corp.com/viewer/):
- `vg_missile_defender.gif`: current capture (86 frames, camera height 115, 986x477 crop),
  produced by the `EngagementCapture` scenario plus `scripts/annotate_engagement_gif.py`. The
  units stand 120 units apart on a left-to-right axis, so the missile crosses the frame
  horizontally; it is ringed in 54 of the 86 frames. Both units are recognizable at model scale
  and labelled ATTACKER / TARGET from translated display names. The target's health drains 300
  to 0 and it is destroyed on camera. Findings and composition rules:
  `docs/system-investigations/engagement-capture-findings.md`.
- `vg_minimalhud_frame.png`: formation-scale frame with visible units across three faction
  clusters (red USA, green China, blue GLA), minimal HUD with outlined minimap bottom-left.

## 4. Known rendering facts (do not misreport as regressions)

| Artifact | Status | Where seen |
|---|---|---|
| Per-frame terrain flicker | Open rendering bug, terrain pass re-renders with slight noise | All headless captures |
| Fog gray `(105,105,105)` instead of terrain | Open bug for camera heights ~900-1500 | Framing outside that band avoids it |
| Camera look-at clamped to map centre | By design when visible ground exceeds the map; set height before `LookAt` | `TacticalViewConstraintTests` documents the contract |
| Missile/FX sub-pixel at far zoom | Expected at validation-ground heights (1600+); close-ups (height ~90-180) fix scale | GIF recipes in `docs/screenshots.md` |
| Shared global constants buffer across passes | Fixed on this branch; was rendering the forward pass with the shadow pass's light-space matrix on Vulkan | `GlobalShaderResourceData.AcquireSlot` / `BeginFrame` |
| Minimap bottom-left, black outline ring | Intentional minimal-HUD design | `vg_minimalhud_frame.png` |
| Projectile streams | Implemented: `ProjectileStreamManager` draws the data's textured ribbons (Dragon Tank flame, Toxin Truck spray, hazard cleanup) per live stream projectile; the hack "beam" weapons (Black Lotus, Hacker) reuse stream data but fire no projectile object and still show nothing | `src/Panopticon.Simulation/Graphics/ProjectileStreams/ProjectileStreamManager.cs` |
| Tank shell tracers | Engine addition (the game data deliberately uses NoTracer FireFX for cannons): a tracer streak follows each shell, scaled from the data's own tank-tracer values (Length 20, Width 0.5) | `ProjectileNugget.CreateShellTracer`, `TracerManager.CreateTracking` |
| Animating cloud shadows | Normal | All daytime captures |

## 5. Capture environment facts

- Headless offscreen Vulkan rendering through the TestHarness (`--headless`); no display
  needed. Captures write PNGs to `/tmp/panopticon_captures/`.
- Env hooks: `TESTHARNESS_VG_CAPTURE=1` (per-frame captures), `TESTHARNESS_VG_OVERLAY_SHOT=1`,
  `TESTHARNESS_VG_HOVER_OBJECT=<definition>`, `TESTHARNESS_VG_HOVER=x,y`.
- Engagement captures use the committed `EngagementCapture` built-in scenario
  (`dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario
  EngagementCapture`), configured under `EngagementCapture` in `TestHarness.json`. It writes
  PNGs plus a `manifest.json` of per-frame screen projections; `scripts/annotate_engagement_gif.py`
  renders the annotated GIF. Full recipe in `docs/screenshots.md`.
- Camera framing order that works: `InitHeightForMap()` → `SetAngleAndPitchToDefault()` →
  `SnapHeightAboveGround(h)` → `LookAt(pos)`. At height ~180 a tank renders ~120px, an
  infantryman ~40px.
- Spawn units via the `ValidationGroundHost.CreateAt` pattern: sample terrain height, then
  `UpdateColliders()` immediately (colliders build at the origin and would frustum-cull).

## 6. Open validation questions

A vision-capable model reviewing new captures should answer:

1. Does the missile render as a visible projectile in flight between the two units at close
   framing? Confirmed on this branch: the engagement manifest records a projectile
   (`MissileDefenderMissile`) in 88 of 150 frames, and it is visible in the annotated GIF.
2. Are muzzle flash and detonation FX visible per shot cycle at close framing? Confirmed in
   pixel analysis of the current GIF: launch bloom at the attacker frames 5-12, impact FX at
   the target peaking on the damage frame (19), smoke persisting after. A launch-time impact
   bug (damage FX at the muzzle) was found by this capture and fixed; damage now lands on the
   missile's arrival frame (see `docs/system-investigations/engagement-capture-findings.md`).
3. Does the missile trajectory arc (thrust + gravity) rather than drop or fly straight?
4. Does the tank show damage reactions (health bar drain is expected; death FX if HP reaches
   zero)?
5. Any new artifacts at close framing not listed in section 4?

## 7. Session log summary

- Minimap outline ring: fixed and refactored to `DrawingContext2D.DrawPixelSnappedRectangle`,
  pixel-identical to the approved golden capture.
- Missile flight: fixed, logic-verified, and now visually confirmed (see section 3).
- Global shader constants: each render pass now gets its own constants buffer and resource
  set per frame. Previously all passes shared one buffer, so on Vulkan the shadow pass's
  light-space matrix won and every capture rendered a map-wide view whatever the camera did.
  This, not framing, was why earlier close-ups showed unidentifiable specks.
- Camera investigation: concluded no engine bug; ordering contract documented and tested.
- Map inventory: `docs/maps.md` (55 maps; note grid cells are 10 world units).
- The earlier "unit meshes never render headlessly" and "meshes collapse at close zoom"
  diagnoses were wrong and are corrected in `docs/screenshots.md`; the real cause was camera
  constraint clamping putting subjects off-screen.
