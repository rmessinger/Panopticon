# Screenshot capture and sharing

> Environment-specific recipes for producing and sharing screenshots from agent shells on the
> Linux dev host. Amend this file whenever a new capability or limitation is found, so the
> working method never has to be re-derived.

## Sharing with the user

Copy the PNG into `/mnt/pit2/Network Storage/temp/` (the path contains spaces; quote it). That
directory is served read-only by the live viewer at `https://files.reid-corp.com/viewer/`, which
shows the newest image automatically: no refresh, upload, or login. Each iteration uses a unique
versioned filename and frame directory. Preserve existing entries as progress evidence; no
overwrite or removal without explicit operator approval. Historical recaptures include
`reconstructed` in their filenames and record the source commit.

Layout:
- Top level: meaningful demonstrations of progress only (proof stills, before/after comparisons,
  animation GIFs). Animated results ship as GIFs, not frame series.
- `frames/<topic>/`: granular per-frame captures (animation sample stills, frame sequences), e.g.
  `frames/sc2-anim/`.

```sh
cp /tmp/panopticon_captures/capture_0100.png "/mnt/pit2/Network Storage/temp/shot.png"
```

## Environment facts (Linux dev host)

| Item | Status |
|---|---|
| Real X server (`:0`) | Present, but agent shells cannot authenticate (`Xauthority` mismatch); windowed apps cannot use it |
| `Xvfb` / `xvfb-run` | Available; virtual displays work for X clients |
| GPU | AMD Radeon RX 7600 (RADV). Works for offscreen Vulkan (headless device creation). Cannot present under Xvfb (no DRI3) |
| OpenGL backend | Unusable: Veldrid.SPIRV's GLSL emitter crashes cross-compiling the `FixedFunction` shader (bundled native bug) |
| llvmpipe / lavapipe | Software Vulkan ICD at `/usr/share/vulkan/icd.d/lvp_icd.json`; renders but never presents to Xvfb within usable time |
| `ffmpeg` | Available; `x11grab` captures a virtual display to PNG |
| `xdotool` / ImageMagick `import` | Not installed; no key/mouse injection into Xvfb from agent shells |
| `PIL` (python3) | Available; used for pixel-level A/B verification of captures |

## EE2 particle attachment diagnostics

`Ee2ParticleProof` writes 17 frames to `ee2-particle-proof/` under the working directory,
including early muzzle samples at 0.033, 0.1, 0.2, and 0.3 seconds.
Both `CNC_GENERALS_PATH` and `EMPIRE_EARTH_II_PATH` must resolve installed games.
`EE2_PARTICLE_PROOF_HIDE_MODEL=1` omits the tank draw while retaining the same camera,
bone attachment, and particle seed. Run this diagnostic from a separate working directory
to preserve the composited proofs. Compare matching muzzle frames to isolate barrel occlusion.
`EE2_PARTICLE_PROOF_SIDE_VIEW=1` rotates the camera orbit 90 degrees for a side-angle reference.
The muzzle binding uses the tank's `Muzzle_FiringShell` DDF entry at scale `0.09`.
The tank uses `HeavyMounted1_13.ForceScale = 0.09`. The model and effect scales
are independent; materialized particle motion does not inherit the model scale again.

`EE2_PARTICLE_PROOF_MODEL` selects an additional model reference. Each profile writes
seven muzzle frames and a source/scale guide to `ee2-particle-proof/<profile>/`.
`EE2_PARTICLE_PROOF_SIDE_VIEW=1` also applies to these profiles.

| Profile | Model | ForceScale | Effect | EffectScale |
|---|---|---|---|---|
| `assault-tank` | `lhm12_assaulttank` | 0.09 | `fx_muzzle_tank` | 0.09 |
| `heavy-artillery` | `lha12_selfpropelled` | 0.09 | `fx_muzzle_heavyartillery_1` | 0.08 |
| `myrmidon-tank` | `lhm15_myrmidontank` | 0.07 | `fx_muzzle_tank` | 0.09 |

Sources: `HeavyMounted1_12`, `HeavyArtillery_12`, `HeavyMounted1_15` and their
`ChildNames` in `db/Units/`, with effects from `db/Visuals/visual_fx_weapon.ddf`.
All use `tag_muzzle_1` with no added barrel offset. The Myrmidon proof shows only
the first muzzle, not its second muzzle or secondary dust effect. Captures verify
import/render placement, not original-game attack timing or animation parity.

Tracked model references live under `ee2-particle-proof/<profile>/front/` and
`side/`. Selected profiles initialize only their muzzle effect. Repeatability
checks compare identical builds and initialization sequences, not earlier
captures that also initialized explosion and splash resources.

Capture timestamps are nonuniform. Reference GIF frame durations follow timestamp differences,
with a final-frame pause. Filename order alone does not imply a constant frame rate.

## Recipe: 3D scene screenshots (headless, reliable)

The generic GIF annotator supports `--health` for measured per-actor health panels outside the
rendered scene. `--labels` retains the static legend mode. Imported model bounds can exceed the
manifest's gameplay-derived `RadiusPixels`; increase `--padding` to retain complete models and
effects. The X1 duel uses `--health --labels --padding 260` and captures every logic frame.
Health panels are not proof of target-impact rendering.

The TestHarness renders offscreen with Vulkan and captures PNGs; no display is needed.

`MassBattleDeploymentScreenshotPath` captures the armies after deployment in either mode.
`MassBattleDeployment` runs reachability probes before spawning the armies. Windowed mode
keeps rendering after the report until the window closes; headless mode exits after the report.
`MassBattleDeploymentPreview: true` skips the probes and proof report, spawning the armies
immediately for windowed viewing or profiling. Screenshot capture remains available.

The G6 linear output pass originally inverted the composed framebuffer on top-left-origin
backends. `OutputColour.frag` samples by framebuffer coordinates instead of clip-space Y.
Orientation validation: `OutputColourOrientationTests` checks asymmetric texture rows and
columns at even and odd viewport sizes. Captures require direct visual inspection; agreement
with a golden does not establish orientation. Enhanced goldens were regenerated after the fix
(2026-10-09 manifest update); faithful goldens were byte-identical before and after.
PNG export requires no compensating flip for this defect.

Replay scenarios: set `"CaptureFrames": true` in `TestHarness.json` and run `--headless`; a
capture is written after every completed replay action.

Unit Validation Ground: set the `TESTHARNESS_VG_CAPTURE` environment variable; every logic step
renders and captures a frame (capped at 500 files) into `/tmp/panopticon_captures/capture_NNNN.png`.

```sh
rm -f /tmp/panopticon_captures/*
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
TESTHARNESS_VG_CAPTURE=1 \
dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- --headless
ls /tmp/panopticon_captures/
```

Exit code 3 from a VG run means "frame cap with no baseline", which is expected when the
scenario outlives its frame budget; captures remain valid.

Validation-ground map surveys must exclude submerged terrain using polygon `IsWater`/`IsRiver`
and the polygon's first-point Z. Generals water is represented by polygon triggers, not only
`StandingWaterAreas`. A flat heightmap region can be a lakebed. Dry placement must be checked
before treating a timing run or contact sheet as a valid map-switch proof.

## Recipe: windowed runs and the ImGui overlay (Xvfb + llvmpipe)

Status: does not work. llvmpipe renders (CPU fully busy) but no frame was ever presented to the
Xvfb X server in 10+ minutes; the captured screen stays at the window background. Documented so
the attempt is not repeated blindly:

```sh
Xvfb :99 -screen 0 1600x1000x24 &
DISPLAY=:99 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json dotnet run --project src/Panopticon.Tools.TestHarness
ffmpeg -y -f x11grab -video_size 1600x1000 -i :99 -frames:v 1 out.png   # captures only the window background
```

RADV cannot present under Xvfb either (no DRI3), the OpenGL backend dies in the Veldrid.SPIRV
GLSL bug, and the real X server rejects agent-shell auth. Use the offscreen recipe below instead.

## Recipe: overlay / tooltip screenshots (headless, reliable)

The validation-ground ImGui overlay can be rendered offscreen into the same framebuffer as the
scene, so tooltips and the status window are capturable without any display. This is the
documented way to screenshot overlay visuals.

The shared performance panel appears at the upper-left in overlay captures. Windowed scenarios
show it by default; `-`/`+` collapses/expands it independently of the validation-ground F1 window.
FPS, frame time, CPU, and GPU timings include capture overhead in offscreen captures, so these
images verify layout, not windowed performance. GPU frame time requires the Vulkan or Direct3D
11 backend and reports `n/a` on others. On macOS, Metal supports the
same offscreen recipe; `TMPDIR` selects the capture root and should use a unique directory per run.

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
TESTHARNESS_VG_OVERLAY_SHOT=1 \
TESTHARNESS_VG_HOVER_OBJECT=AmericaInfantryRanger \
dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- --headless
# frames with scene + tooltip land in /tmp/panopticon_captures/capture_NNNN.png
```

- `TESTHARNESS_VG_HOVER_OBJECT=<definition name>` pins the mouse on the first object with that
  definition each frame (follows camera motion). Small infantry can project to a point that
  misses their own collider (ray pick fails, no tooltip); if that happens, pick a larger unit.
- `TESTHARNESS_VG_HOVER=x,y` pins the mouse at a fixed screen position instead.
- F1-key visibility and status-window rendering are unchanged; the synthetic mouse only feeds
  the tooltip.
- Verify the result with a pixel scan: the tooltip is a dark translucent panel docked at the
  right edge above the bottom HUD, with a bright owner-colored text line (red for USA in the
  validation ground) and white text below; a crop and 3x upscale makes it reviewable.

## Recipe: annotated engagement GIFs (headless, reliable)

Captures one unit attacking another, with per-frame screen projections computed inside the
engine so overlays land exactly on the units. Two committed pieces:

- `EngagementCapture`, a TestHarness built-in scenario
  (`src/Panopticon.Tools.TestHarness/EngagementCaptureRunner.cs`). It spawns an attacker and a
  target, auto-fits the camera to them, commands the weapon directly, writes one PNG per logic
  frame, and writes `manifest.json` with per-frame screen positions, radii, health, and
  projectile positions.
- `scripts/annotate_engagement_gif.py`, which turns that directory into an annotated GIF.

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario EngagementCapture

python3 scripts/annotate_engagement_gif.py /tmp/panopticon_engagement \
  "/mnt/pit2/Network Storage/temp/engagement.gif"
```

Tuning lives under `EngagementCapture` in `TestHarness.json` (map, attacker/target definition
names, labels, separation, `TargetHp`, frame count, `DesiredSpanFraction`, output directory).

Two options matter for recognizability. `AxisAngleDegrees` rotates the attacker-to-target axis:
firing FX are far larger than the unit firing them, so the default 180 orients the pair left to
right with the backblast venting behind the attacker rather than over him. `HideHud` closes every
GUI window before capturing so the minimap does not intrude on the crop. Measured comparisons of
other angles are in `docs/system-investigations/engagement-capture-findings.md`.
The runner exits non-zero when a definition is missing, the camera cannot be fitted, the
attacker has no weapon, or no projectile is ever observed, so a silent empty capture is not
possible.
Labels come from `ObjectDefinition.DisplayName` run through `.Translate()`; the raw value is a
localization key. `Player.Side` returns raw faction names ("FactionCivilian") and is not
suitable for labels; the annotator uses ATTACKER/TARGET/MISSILE roles instead.

## Recipe: generic scenario capture (headless)

For scripted scenes beyond a two-unit engagement, `ScenarioCapture` runs a declarative capture
profile: actors, a fixed or auto-fitted camera, frame-scheduled `Attack`/`Construct`/
`ApplyImpulse` commands, and expectations (projectile observed, actor damaged/destroyed,
construction progressed/completed, minimum displacement). Checked-in example profiles:
`capture-construction.json` (a dozer builds a barracks, tracked from placement through
completion) and `capture-impulse.json` (a physics impulse slides a civilian car).

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless \
  --built-in-scenario ScenarioCapture --scenario capture-construction.json
python3 scripts/annotate_capture_gif.py /tmp/panopticon_capture_construction out.gif --labels
```

`--scenario` takes a file path or an embedded profile name; the profile schema and failure
behavior are documented in `src/Panopticon.Tools.TestHarness/AGENTS.md`. Output lands in
`$TMPDIR/panopticon_capture_<name>/` (PNG sequence plus a schema-2 manifest that extends the
engagement manifest with world positions, velocities, construction state, and command/
expectation results). Exit codes: 1 profile or setup error, 4 camera fit failure, 5 unmet
expectation. Playback defaults to real time (each PNG held for the logic frames it covers at
30 Hz, derived from the manifest); `--speed` scales it and `--frame-ms` overrides it. The
generic annotator crops and scales like the engagement one but draws no engagement-specific
overlays; `--labels` adds a static actor legend.

Projectile-orientation profiles can use a `ProjectileOrientation` expectation to require a
minimum local `+X` versus sampled-travel dot product and optional ascent or descent samples.
Pass `--projectile-orientation` to draw travel in white and local `+X` in cyan. Reference
profiles: `capture-projectile-orientation-shell.json`,
`capture-projectile-orientation-missile.json`, and
`capture-projectile-orientation-scud.json`.

Verify content before sharing: the manifest's target `Health` should fall across the sequence
and a non-trivial number of frames should list a projectile.

## Recipe: physics seam test scene (headless)

The `PhysicsStack` built-in scenario validates the object physics seam
(`docs/roadmap/plans/physics-seam.md`). Two phases: a pure-seam Bepu benchmark (100/1k/10k
stacked boxes, ms per `Timestep`, loose settle bounds, run-to-run stability probe), then a
visual stack of 40 cars falling and settling through the game on the Bepu backend,
post-validated against loose scatter bounds. Exit codes: 0 pass, 1 setup error, 5 unmet bound.

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario PhysicsStack
python3 scripts/annotate_capture_gif.py /tmp/panopticon_physics_stack \
  "/mnt/pit2/Network Storage/temp/physics_stack.gif" --labels
```

A capture profile selects the Bepu backend for a whole run via `"PhysicsBackend": "Bepu"`
(checked-in example: `capture-impulse-bepu.json`, the impulse car on Bepu for loose comparison
against the legacy `capture-impulse.json`; actor `"Z"` spawns height above ground for
falling/stacking scenes).

## Recipe: phase proof captures at PR setup (headless)

End-of-session rule: when a phase or card ships visually verifiable behavior, capture it from
the exact PR branch tip and put the artifacts in the viewer before opening or updating the PR.
Run from a detached worktree so the capture reflects the PR state, not the current checkout:

```sh
git worktree add /tmp/opencode/<phase-proof> <pr-branch-tip> --detach
cd /tmp/opencode/<phase-proof>
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario <PhaseProofScenario>
git worktree remove --force /tmp/opencode/<phase-proof>
```

Verified example (C4h, commit of PR #134): `Ee2ParticleProof` writes nine 512 px PNGs (three
effects at 0.0/0.5/1.0 s) plus `validation_guide.md` into `ee2-particle-proof/`. Deterministic
seed scope means reruns reproduce identical frames. Assemble one GIF per effect and share:

```sh
ffmpeg -y -framerate 1 -pattern_type glob -i 'ee2-particle-proof/fx_explosion_1-particle-*.png' \
  -vf "split[a][b];[a]palettegen[p];[b][p]paletteuse" out.gif
cp out.gif "/mnt/pit2/Network Storage/temp/c4h_<effect>.gif"
```

Verify before sharing: unique-color counts should grow across the 0000/0500/1000 frames, and
pixel diffs between consecutive frames should be non-trivial for evolving effects. Subtle
effects can be legitimate: `fx_muzzle_tank` diffs only 97-122 px because its puffs are small
next to the tank; the validation guide is the authority on expected magnitude.

## Camera framing contract for close-ups

Unit meshes render at any camera height once they are actually in view. The failure mode that
looks like "meshes draw as specks at close zoom" is the tactical camera's map constraint: when
the visible ground exceeds the map (far zoom), the constraint pins the look-at point to the
map centre, and `LookAt` calls are clamped against the constraint for the CURRENT height. So
aiming before changing the height lands the camera hundreds of units off target, off-screen
objects are frustum-culled, and nothing of the subject renders.

Working order (see `TacticalViewConstraintTests`):

```csharp
view.InitHeightForMap();
view.SetAngleAndPitchToDefault();
view.SnapHeightAboveGround(180f);   // height FIRST
view.LookAt(new Vector3(630, 600, 0)); // then aim
```

At height 180 a tank renders ~120px and an infantryman ~40px, large enough for unit-level GIFs
without upscaling. The terrain pass still has a separate hole: heights roughly 900-1500 render
fog gray instead of terrain (see `ValidationGroundCamera.MinimumSafeCameraHeight`); pick
framing heights outside that band.

A second cause of the same symptom was fixed in `GlobalShaderResourceData`: every render pass
shared one global constants buffer, so on Vulkan the forward pass drew with the shadow pass's
light-space matrix and the frame showed a map-wide view regardless of camera height. Each pass
now acquires its own buffer and resource set per frame (`AcquireSlot`, reset by `BeginFrame`
from `RenderPipeline.Execute`). If a "camera is ignored" symptom reappears, check that
`BeginFrame` is still being called once per frame.

## Recipe: ReplaySketch validation-ground editor (2D, no display)

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia --no-build -- \
  --screenshot "/mnt/pit2/Network Storage/temp/vg_avalonia.png"
```

Renders the editor scene via SkiaSharp, headless; works on Linux. The current Avalonia front-end's screenshot mode (`REPLAYSKETCH_SCREENSHOT_PATH`, see
`src/Panopticon.Tools.ReplaySketch.Avalonia/AGENTS.md`) runs on Linux under Xvfb with software
GL (verified recipe below). The former MAUI front-end was removed.

```sh
setsid nohup Xvfb :99 -screen 0 1600x1000x24 -nolisten tcp >/dev/null 2>&1 &
DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 \
REPLAYSKETCH_SCREENSHOT_PATH=/tmp/shot.png REPLAYSKETCH_SCREENSHOT_TAB=model \
  dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia --no-build
```

The Model tab (`REPLAYSKETCH_SCREENSHOT_TAB=model`) uses a deterministic fake session
(`ExternalModelScreenshotFixture`) and starts no TestHarness process, so its capture is
reproducible. Map-tab captures need `CNC_GENERALS_PATH` and a longer settle delay (8 s).

## Recipe: pixel-diff A/B verification

When a visual change must be verified without a human, capture two runs that differ only in the
change (for example an env-gated disable flag), then diff the same frame numbers:

```python
from PIL import Image
a = Image.open("tracer_on/capture_0100.png").convert("RGB")
b = Image.open("tracer_off/capture_0100.png").convert("RGB")
pa, pb = a.load(), b.load()
diff = [(x, y) for y in range(a.height) for x in range(a.width)
        if max(abs(pa[x,y][0]-pb[x,y][0]), abs(pa[x,y][1]-pb[x,y][1]), abs(pa[x,y][2]-pb[x,y][2])) > 30]
```

Runs are near-deterministic, so the diff isolates exactly the change's pixels. Brighter-in-enabled
counts confirm additive effects (tracers, muzzle flashes).

Caveat (measured on alpine assault): full-frame pixel identity does not hold between runs of the
same binary on maps with ambient effects; two same-binary runs differ by 300k-550k pixels per
frame (ambient particles, water shimmer). For behavior-level A/B verification compare manifest
trajectories instead (bit-identical when the simulation is unchanged); reserve pixel diffs for
controlled scenes without ambient effects.

## History

Past session transcripts are stored in `~/.local/share/opencode/opencode.db` (SQLite, `part`
table); mining them for `xvfb`/`ffmpeg`/`pit2` commands recovers prior solutions.
