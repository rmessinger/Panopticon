# Panopticon TestHarness — Agent Guide

## Purpose

`Panopticon.Tools.TestHarness` is a windowed desktop tool that runs a scripted game scenario
against a real SAGE game installation. It is the primary tool for **visual validation** of
the Panopticon renderer on any platform.

Unlike the headless tests in `Panopticon.Simulation.Tests`, the TestHarness opens a real game window
using SDL2 and renders frames to the display via the platform's native graphics API
(Metal on macOS, Direct3D 11 on Windows, Vulkan on Linux).

---

## Running the TestHarness

```bash
dotnet run --project src/Panopticon.Tools.TestHarness
```

Configuration is in `TestHarness.json` (copied to the output directory on build).
Set `GamePath` to your game installation if auto-detection fails.

`BuiltInScenario` in the config selects what runs; `--built-in-scenario <name>` overrides it
for a single run.

### `PbrMaterialProof` scenario

Headless capture of the complete C3 PBR feature matrix: synthetic static/skinned quads with
embedded textures render through `StandaloneModelCapture` into `pbr-material-proof/*.png`, plus a
`validation_guide.md` naming the expected check per capture. Asserts the emissive sRGB round trip
within one 8-bit step and byte-identical repeat captures (a discarded warm-up frame absorbs
first-execution engine-global uploads). Requires `CNC_GENERALS_PATH` as the rendering host only.

```bash
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario PbrMaterialProof
```

### `GltfRuntimeProof` scenario

Headless C4 runtime proof: imports the committed glTF fixtures (`--gltf-fixture-root`, default
the Game.Tests fixture matrix) through the internal glTF importers, materializes through C1-C3,
binds animations through C2, and captures the static PBR model, two rigid-node times, and three
skinned times (including one between STEP keys) into `gltf-runtime-proof/*.png` plus a
`validation_guide.md`. Captures apply presentation-only framing (uniform magnification 20x and a
yaw, because glTF front +Z maps to Panopticon +X and unrotated fixtures present vertical walls
edge-on to the arcball view) and discarded warm-up frames (plain, textured, and animated) absorb
first-execution instability; repeated runs are byte-identical. Requires `CNC_GENERALS_PATH` as
the rendering host only.

```bash
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario GltfRuntimeProof \
  --gltf-fixture-root "$PWD/test/Panopticon.Simulation.Tests/Content/Importing/Gltf/Fixtures"
```

### `EngagementCapture` scenario

Headless capture of one unit attacking another, used to produce annotated engagement GIFs.

```bash
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario EngagementCapture
```

Spawns the attacker and target, fits the camera to both, commands the weapon directly (an
`AttackObject` order would move the attacker and drift the framing), captures one PNG per
logic frame, and writes `manifest.json` with per-frame screen projections, radii, health, and
projectile positions. Knobs live under `EngagementCapture` in `TestHarness.json`; notably
`AxisAngleDegrees` (orients the pair so firing FX vent behind the attacker rather than over him)
and `HideHud` (closes GUI windows so the minimap stays out of the crop). Background and measured
angle comparisons: `docs/system-investigations/engagement-capture-findings.md`.

Exit codes: 1 unknown object definition, 4 camera could not be fitted or attacker has no
weapon, 5 no projectile ever observed. Render `scripts/annotate_engagement_gif.py <captureDir>
<out.gif>` to get the labelled GIF. Full recipe: `docs/screenshots.md`.

### `ScenarioCapture` scenario (generic)

Declarative capture of any small scripted scene: a profile JSON describes players, actors, a
fixed or auto-fitted camera, frame-scheduled commands, and expectations; the runner executes it
headlessly and writes a PNG sequence plus a schema-2 `manifest.json` (an additive superset of the
engagement manifest: world positions, velocities, construction state, command/expectation
results). `EngagementCapture` is now an adapter onto this runner.

```bash
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario ScenarioCapture --scenario capture-construction.json
```

`--scenario` takes a file path or an embedded profile name (checked-in examples:
`capture-construction.json`, `capture-impulse.json`, `capture-rebel-attack.json`,
`capture-scorpion-treads.json`, `capture-follow-move.json`, `capture-follow-projectile.json`,
`capture-scud-missile.json`, and the `capture-vehicle-*.json` set: tomahawk, rocket-buggy,
humvee-tow, scorpion-missile, toxin-truck, dragon-tank).
Commands: `Attack` (direct weapon
targeting, no movement order; optional `WeaponName` addresses a specific slot weapon by its
template name, e.g. a secondary TOW), `Construct` (real `Order.CreateBuildObject` dispatch; the created
building binds to the command's result ID and is tracked), `ApplyImpulse` (mass-independent
delta velocity via `PhysicsBehavior.ApplyForce`), `Move` (real pathfinded move order through
`AIUpdate.SetTargetPoint`; raises the MOVING condition flag on dispatch, clears on arrival),
`GrantUpgrade` (researches an upgrade for the actor's owner and refreshes its upgrade modules,
flipping weapon sets; schedule it a frame before an `Attack` that names an upgraded weapon).
Camera modes: `AutoFit` (one-time fit of subjects + framing points; the camera never moves),
`Explicit` (fixed `LookAt` + `Height`), `Follow` (fits like AutoFit, or honors
`InitialCameraHeight` when the follow subject is the lone subject, then pans every logic frame
so `FollowActorId`, defaulting to the first subject, stays at screen center; the fitted height
never changes, and a destroyed subject freezes the camera at its last position).
`FollowProjectileSourceActorId` (instead of `FollowActorId`) rides the newest live projectile
fired by that actor: framed on the shooter until launch, tracks the projectile at screen
center, freezes at the impact site between shots, hands off to the next shot.
`FollowFirstProjectileOnly` locks the FIRST projectile for its whole flight and freezes at its
death site instead of handing off (rapid-fire salvos). Projectiles are
linked to their launcher via `GameObject.CreatedByObjectID`, which `ProjectileNugget` sets.
Expectations: `ProjectileObserved`,
`ActorDamaged`, `ActorDestroyed`, `ConstructionProgressed`, `ConstructionCompleted`,
`MinDisplacement`; unmet expectations fail the run. Exit codes: 1 profile/setup error
(invalid references, unknown definitions, non-builder, no physics, insufficient funds),
4 camera could not frame the subjects or an attack source has no weapon, 5 expectation not met.
`scripts/annotate_capture_gif.py <captureDir> <out.gif>` renders a generic cropped GIF
(`--labels` adds a legend). `ProjectileOrientation` expectations validate sampled local `+X`
against travel, and `--projectile-orientation` draws both vectors in the GIF. Reference profiles:
`capture-projectile-orientation-shell.json`, `capture-projectile-orientation-missile.json`, and
`capture-projectile-orientation-scud.json`.
Camera fitting pauses logic and consumes zero logic ticks; warm-up
belongs in the profile's `SettleFrames`. A profile selects the physics backend for the whole run
with `"PhysicsBackend": "Bepu"` (example: `capture-impulse-bepu.json`), and actor `"Z"` spawns
at an absolute world height; omit it to place the actor on terrain. Actor
`PitchDegrees`/`RollDegrees` orient the
spawn (roll 180 = upside down; example: `capture-car-flipped.json`). `DiskFileSystem` resolves
loose override files case-insensitively, so patched models dropped into `<install>/Art/W3D/`
load regardless of query casing.

### `PhysicsStack` scenario (physics seam)

Headless validation of the object physics seam (`docs/roadmap/plans/physics-seam.md`), PS-1.
Two phases: a pure-seam Bepu benchmark (stacked boxes at 100/1k/10k bodies; ms per `Timestep`,
loose settle bounds, run-to-run stability probe compared by loose center-of-mass bounds), then
the embedded `capture-physics-stack.json` profile (40 cars stacked in columns fall and settle
through the game on the Bepu backend), post-validated against a loose scatter bound around the
stack center.

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario PhysicsStack
```

Exit codes: 0 pass, 1 setup/manifest error, 5 unmet settle or scatter bound. Visual frames and
manifest land in `/tmp/panopticon_physics_stack/`; `scripts/annotate_capture_gif.py` renders the
GIF. Reference timings (single-threaded, this repo's dev host): ~0.03/0.1/3.1 ms per `Timestep`
at 100/1k/10k bodies.

---

## What to observe

When the TestHarness window opens, you should see:

| Region | Expected appearance |
|---|---|
| **Main viewport** | 3-D rendered terrain (grass, dirt paths, roads) with the camera looking down at an RTS-style angle |
| **Bottom UI strip** | Control bar, minimap (lower-left), money counter ($9400–$10000 range), and unit portrait panels |
| **Units** | At least one Dozer (USA, yellow construction vehicle) visible in the first 30 seconds of play |
| **Construction** | A barracks building blueprint or structure appears after the first ~15 seconds |
| **Console output** | `[dispatched]` and `[completed]` lines appear as the scenario progresses |

### Common failure modes

| Symptom | Likely cause |
|---|---|
| Terrain visible but **all units invisible** | NormalMapped or FixedFunction Metal shader has wrong resource binding indices — see `ShaderCrossCompiler.FixMetalMslBindings` |
| **Black or dark-grey triangle** cutting across terrain | Terrain shader depth-buffer artefact from incorrect MSL `[[buffer(N)]]` assignments |
| **Entire screen grey** or wrong clear colour | `RenderPipeline.ClearColor` not normalised to 0–1 range |
| Window opens but **game freezes on first frame** | `Platform.Start()` / SDL2 initialisation issue; check console for exceptions |
| `[dispatched]` lines never appear | Replay export failed or the scenario frame offsets are wrong |

---

## Capturing frames for visual validation

Environment-specific capture and sharing recipes (headless PNG captures, windowed runs under Xvfb + llvmpipe, synthetic mouse hover for the ImGui overlay) live in [`docs/screenshots.md`](../../docs/screenshots.md); amend that file when a new capability or limitation is found.

The `FrameCapturer` utility in `Panopticon.Simulation` reads back any GPU texture to CPU memory.
You can add capture calls directly in `Program.cs` to dump frames to disk:

```csharp
// Capture the current rendered frame and save as PNG
var captured = FrameCapturer.Capture(
    game.GraphicsDevice,
    game.Panel.Framebuffer.ColorTargets[0].Target);

// Convert to PNG and write to a temp path
using var image = ToImage(captured); // see FrameCaptureSession.ToImage in the test project
var path = Path.Combine(Path.GetTempPath(), $"panopticon_frame_{frameNumber:D6}.png");
image.SaveAsPng(path);
Console.WriteLine($"[TestHarness] Frame saved: {path}");
```

For structured multi-frame capture with per-frame validation hints, use
`FrameCaptureSession` from `Panopticon.Simulation.Tests` as a reference implementation.
The session directory and a `validation_guide.md` are printed to stdout.

---

## Engine-action results file

Every TestHarness run (headless or windowed) writes a results text file recording every engine
`Order` processed during the run, for diffing two identically-configured runs for parity. It's
written unconditionally (no config flag) as a sibling of the exported replay, e.g.:

```
panopticon_scenario_<guid>.rep         -> panopticon_scenario_<guid>.results.txt
```

The path is printed to stdout at the end of the run (`Action results saved to: ...`). The file is
flushed periodically during the run (every 20 lines or 5 seconds, whichever first), not just at
exit, so a crash still leaves a usable partial file.

`ActionResultsWriter.WriteLine` also emits each line through NLog automatically, so every result
reaches both stdout and `panopticon.log` (see `Panopticon.Core/NLog.config`'s catch-all rule) in real
time as the scenario runs, not just in the dedicated results file at the end. This routing is
centralized in the writer itself — callers (like `EngineActionRecorder`) only need to call
`WriteLine`/`Write` once and get both destinations for free; no future recorder needs to
duplicate the NLog call.

### Format

A `#`-prefixed header block carries non-comparable run metadata (scenario, game, host, wall-clock
start time) — parity-diff tooling should skip `#` lines. The body is tab-separated:

```
logicFrame  state       orderType    player   details
```

`state` is `Dispatched` (single-shot, most order types), `Started`/`Completed` (two-part lifecycle:
Move, object-targeted Attack, Build, CreateUnit), or `Aborted` (a `Started` action whose unit/target
was destroyed before completing). `details` is a space-separated `key=value` list:

| Order type(s) | Lifecycle | Details |
|---|---|---|
| `MoveTo` | Started + Completed per unit | `unit`, `pos`, `dir`, `speed`, `target` (Started); `unit`, `pos` (Completed) |
| `AttackObject` / `ForceAttackObject` | Started + Completed per attacking unit | `unit`, `target`, `targetPos` (Started); `unit`, `target` (Completed, on target destroyed) |
| `ForceAttackGround` | Dispatched only (composite move+maybe-attack; no lifecycle tracking) | `unit`, `targetPos`, optional `target` |
| `BuildObject` | Started + Completed | `def`, `pos`, `angle` (Started); `def`, `pos`, `objectId` (Completed) |
| `CreateUnit` | Started + Completed | `def` (Started); `def`, `objectId`, `pos` (Completed) |
| Everything else | Dispatched only | `args=<raw Order.ToString() dump>` |

Move completion is detected by polling `AIUpdate.TargetPoints.Count == 0` once per main-loop
iteration (there's no engine "arrived" event). Build/CreateUnit completion is matched via a FIFO
queue per `(player, definitionName)` against `ConstructionCompleted`/`ObjectCreated`. Attack
completion is matched via `ObjectDestroyed` on the tracked target.

Known quirk: `CreateUnit` `Completed` entries report `pos=(0.00,0.00,0.00)` because
`ObjectCreated` fires before the unit is physically placed in the world; the `objectId` is still
reliable for parity comparisons.

### Object lifecycle and health

Alongside orders, the recorder also logs general object state changes for every player-owned
object (the `orderType` column holds a category string instead of an `OrderType` for these):

| Category | State | Trigger | Details |
|---|---|---|---|
| `ObjectLifecycle` | `Created` | `Game.ObjectCreated` | `objectId`, `def`, `pos`, `health` |
| `ObjectLifecycle` | `Destroyed` | `Game.ObjectDestroyed` | `objectId`, `def`, `pos` |
| `Health` | `Changed` | polled once per main-loop iteration | `objectId`, `def`, `previous`, `current`, `max` |
| `SupplyCycle` | `State` | polled: `SupplyAIUpdate.SupplyGatherState` changed | `unit`, `def`, `state`, `prev` |
| `SupplyCycle` | `ApproachStart` | polled: unit enters an `ApproachingSupply*` state | `unit`, `def`, `leg` (`Gather`/`Dump`), `goal`, `frame` |
| `SupplyCycle` | `ApproachArrived` | polled: unit leaves an `ApproachingSupply*` state | `unit`, `def`, `leg`, `travelFrames`, `overshoots`, `closest` (2D units), `maxSpeed` |

Health changes are detected by polling `BodyModule.Health` each iteration (there's no engine
"health changed" event), the same pattern used for move completion. Objects already in the scene
when the recorder is constructed (map/scenario starting units) have their health seeded at
construction time so they don't produce a spurious first "change" entry. Objects with no
`BodyModule`, or no `Owner` (scenery/props), are skipped entirely.

Objects under construction (`ObjectStatus.UnderConstruction`) are also excluded from health
logging: their health rises each tick with build progress (`GameObject.AdvanceConstruction`),
which is not a damage/heal event. Their baseline is still tracked silently so the transition to
full health on completion is not reported as a spurious change.

See `ActionResultsWriter.cs` (I/O layer) and `EngineActionRecorder.cs` (semantic layer) for the
implementation.

## How agents should validate visual output

1. **Run the TestHarness** (or the headless `DefaultScenario_CapturesFrames_ForVisualValidation`
   test) to produce PNG files and a `validation_guide.md`.
2. **Read `validation_guide.md`** — it lists each captured frame, its full path, and the
   specific visual properties to check.
3. **View each PNG** using your image-viewing capability.
4. **Report per frame**: ✅ Pass / ❌ Fail (describe anomaly) / ⚠️ Uncertain.
5. When reporting a failure, cite the likely subsystem:
   - Terrain artefacts → `ShaderCrossCompiler.cs` / `RenderPipeline.cs`
   - Missing units → `NormalMapped.hlsl` / `FixedFunction.hlsl` shader bindings
   - UI issues → `Gui/` subsystem or `Sprite.hlsl`

### Locating frame capture output

Headless test frames are saved under:

```
$TMPDIR/panopticon_frames/<test_name>_<timestamp>/
```

Look for the `[FrameCaptureSession] Session started:` line in test stdout to get the
exact path.

## Mass Battle Deployment (M0)

Deployment proof for the mass-battle map (`WI-M0-03`): a committed profile JSON describes the
map, two deployment zone rectangles, airfield anchors behind each zone, and a proxy roster; the
runner loads the map through a real skirmish start, moves one representative per distinct ground
locomotor class between the zone centroids with real pathfinded move orders, then spawns the full
roster per side (1000 units at twice footprint spacing, skipping impassable slots) plus airfields.
Zones and anchors are scenario data; the map itself is never edited or committed.

### Running it

```bash
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/Panopticon.Tools.TestHarness --no-build -- \
  --headless --built-in-scenario MassBattleDeployment
```

The default profile is `mass-battle-deepdivide-deployment.json` (shipped beside the
executable); `MassBattleDeploymentProfilePath` overrides it, `MassBattleDeploymentReportPath`
redirects the proof JSON, and `MassBattleDeploymentScreenshotPath` captures one PNG after the
armies spawn. A windowed run (no `--headless`) opens on the framed deployment and is the
operator-run load proof. Reachability probes run before the armies spawn; after the report is
written, windowed mode renders the deployment until the window closes. Headless mode exits
after the report.

`MassBattleDeploymentPreview: true` spawns the armies immediately without the reachability
probes or a proof report. This mode supports windowed profiling; the default is `false` for
full map validation.

Checks and exit codes: 0 all passed; 1 setup error (missing profile, block sum mismatch,
unknown definitions); 5 unmet acceptance check (capacity, airfield placement, zone separation
vs the longest roster weapon range, locomotor-class arrival, centroid grid path). Reports are
deterministic: same profile and map produce byte-identical JSON. The engine grid is
locomotor-agnostic today, so classes group locomotors by template `Surfaces` flags and the
fastest definition of each class is its representative; real per-locomotor pathing belongs to
the pathfinding track.

## Unit Validation Ground (P9)

A non-skirmish sandbox that stands up the whole unit roster side by side on a real map, orders
each unit to perform its primary action against a shared target cluster, and measures how long
that action takes in logic frames. Measurements are compared against a checked-in golden baseline.

### Running it

Set `Scenario: "UnitValidationGround"` in `TestHarness.json` (the `Scenario` field is the single
toggle; `ScenarioPath`/`--scenario` only overrides the map for the replay scenarios and is ignored
for the validation ground). Both modes share one scenario definition, spawn host, and timer.

Windowed (a human watches the roster act):

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness
```

A windowed run starts paused with the orders held, so the formation's starting conditions can be
inspected before anything acts (the camera pans/zooms while paused). Space, or the overlay button,
resumes: the settle stage runs and every unit is ordered to attack its target. The status window
is hidden by default; F1 toggles it. Hovering any object shows an ImGui tooltip with its name,
owner, HP, and position. Afterwards Space
toggles active pause; paused logic frames do not advance, so pausing never consumes a unit's frame
budget or perturbs the measurement. `ValidationGroundHoldOrders: false` restores immediate
auto-issue after the settle stage.

Headless (CI regression gate, exits non-zero on regression or timeout):

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless
```

Exit codes: `0` pass, `2` baseline regression or timeout when a baseline is configured,
`3` timeout with no baseline configured.

### Config knobs

All optional with defaults, in `TestHarness.json`:

| Knob | Purpose |
|---|---|
| `ValidationGroundScenarioPath` | Validation-ground scenario JSON (layout + placement overrides) from the ReplaySketch editor; when set, loaded verbatim and `ValidationGroundMapPath`/`SettleFrames`/`Seed` are ignored |
| `ValidationGroundMapPath` | Map to run on (default: Dark Mountain) |
| `ValidationGroundSettleFrames` | Frames to settle after spawn before issuing orders |
| `ValidationGroundBaselinePath` | Path to the golden baseline JSON (empty skips comparison) |
| `ValidationGroundRefreshBaseline` | `true` overwrites the baseline from the run instead of comparing |
| `ValidationGroundReportPath` | Where to write the JSON report artifact |
| `ValidationGroundHoldOrders` | `true` (default) starts a windowed run paused with orders held; Space or the overlay button resumes (settle + attack orders), then toggles active pause. Ignored by headless runs |
| `ExitAfterTargetsDestroyed` | `true` (default) exits once all targets are destroyed or the frame cap hits; `false` keeps the game running until the window is closed (windowed only; headless always exits) |

### Refreshing the baseline

The baseline lives at
`src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json`. The checked-in
values are a live capture against a real install, taken with fixed-timestep deterministic stepping.
Re-capture after an engine change that could affect weapon timings by running headless once with
`ValidationGroundRefreshBaseline: true` and `ValidationGroundBaselinePath` pointed at the file, then
commit the result. Never widen a tolerance to make a failing unit pass; a genuine regression is the
signal this phase exists to produce. Before committing, run headless twice and confirm the two JSON
reports are byte-identical (the determinism check).

The capture relies on a few non-obvious mechanics, all wired in `Program.RunValidationGround` and
`ValidationGroundHost`: fixed-timestep logic stepping (`Game.EnableFixedLogicTimestep`) so frame
counts are reproducible; skirmish AI disabled on the roster players (per the "no AI" design); the
victory system made inert (`NeverDefeatedVictoryConditionRule` plus
`Configuration.SkipStartingBuildings`) so no command centers are needed and the sandbox's own
ending criteria (all units resolved, frame cap, window close) govern; one target per attacker,
placed in weapon range and standardized to a low HP
(`ValidationGroundScenario.TargetHp`), so each unit's target-destruction completion is its own and
feasible for the whole roster; and direct weapon targeting for attackers (the `AttackObject` order's
move step suppresses fire for several units, so in-range attackers are commanded directly to isolate
the weapon delay). A unit that destroys its target has its weapon cleared so it stops firing.
Units the sandbox cannot measure (the TroopCrawler transport) are listed in
`PrimaryActionCatalog.UnmeasurableAttackReasons` and recorded as gaps. SpecialPower units (Medic,
Black Lotus, Hacker, Hijacker) act on per-unit ability subjects (damaged friendly, neutral
building, enemy vehicle) and complete on each power's observable effect (heal, ownership transfer,
disabled state), implemented by P6c; point-blank and armor-mismatched attackers use per-unit
placement/target overrides (`UnitOption`).

### Live capture and accuracy verification

Full guide: see "Validation ground baseline — capture & verification" below. The short version:
the timer counts logic frames (Generals runs at 30 Hz, so `MsPerLogicFrame` ≈ 33.33 ms), and every
action's measured duration has a direct anchor in the game's own INI data (weapon delays,
locomotor speed, build time, supply delays, special-power reload). Verify each golden value against
its INI anchor; set tolerances around the anchor to cover only approach/pathing jitter.

#### Validation ground baseline — capture & verification

##### Prerequisites

Do not assume these prerequisites are absent. The TestHarness and the Unit Validation Ground run in any environment that has a game install, a GPU, and a non-snap .NET; CI lacks them, but dev and agent environments often do not. Verify before skipping: the install path env var (e.g. `CNC_GENERALS_PATH`) resolves; `vulkaninfo --summary` lists a discrete GPU; `which dotnet` is not under `/snap`. When all three are present, windowed mode, `--headless`, and the validation ground all work here.

- A real C&C Generals install, located via `CNC_GENERALS_PATH` or `GamePath` in `TestHarness.json`.
- A graphics backend the host process can actually create:
  - Windowed mode works wherever the engine renders normally. It additionally needs an accessible
    display: a `tty`-type shell with no `DISPLAY` cannot authorize against the host's X server, so
    windowed (and frame capture) need a graphical session or a virtual framebuffer (`Xvfb`); the
    headless path needs no display. Tracked as a roadmap follow-up.
  - Headless mode needs an offscreen-capable backend. Vulkan requires `libdl`; snap-confined .NET
    SDKs cannot load it, so on Linux use a non-snap .NET install or confirm `libvulkan`/`libdl`
    resolve. On Windows prefer D3D11; on macOS use Metal.
- The roster resolves against the install: the `[GameFact]`
  `EveryCatalogUnit_ResolvesAgainstRealInstall` test must pass (it fails the run if any catalog
  definition name is invalid).

Known-good local environments, for reference when verifying the prerequisites above:

- Linux: install at `/home/rmessinger/dev/Command and Conquer Generals/` (`Generals.exe`; Linux FS
  is case-sensitive). Set `CNC_GENERALS_PATH` to that path.
- macOS: Steam install at `/Users/rmessinger/Library/Application Support/Steam/steamapps/common/Command and Conquer Generals`
  (`Generals.exe` and `.big` archives directly in that directory). Metal GPU, non-snap Homebrew
  dotnet. Set `CNC_GENERALS_PATH` to that path.

##### Capture workflow

1. Configure `TestHarness.json` for a refresh capture:

   ```jsonc
   {
     "Scenario": "UnitValidationGround",
     "Game": "CncGenerals",
     "Headless": true,
     "GamePath": "/path/to/Command and Conquer Generals",
     "ValidationGroundBaselinePath": "src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json",
     "ValidationGroundRefreshBaseline": true,
     "ValidationGroundReportPath": "/tmp/vg_report_run1.json"
   }
   ```

2. Run once: `dotnet run --project src/Panopticon.Tools.TestHarness --no-build`. Inspect the stdout
   table for gaps/timeouts before trusting any captured value. A unit reported `GAP` or `TIMEOUT`
   has no valid measurement; fix or escalate it (see "Reading gaps and timeouts") before capturing.

3. **Determinism check (mandatory before commit).** Re-run with `ValidationGroundRefreshBaseline:
   false` and `ValidationGroundReportPath` pointing at a second file, then diff the two JSON
   reports:

   ```sh
   diff /tmp/vg_report_run1.json /tmp/vg_report_run2.json
   ```

   They must be byte-identical. If they differ, the run is nondeterministic: find and fix the cause
   (unsettled spawn transients, RNG, target-assignment drift) rather than widening tolerances to
   absorb it.

4. Commit the refreshed baseline and one report JSON for the record.

##### Verifying accuracy — the timings must come from the game files

The timer counts logic frames, never wall-clock. Generals ticks at 30 Hz, so
`MsPerLogicFrame ≈ 33.33 ms` and INI millisecond durations are converted to frames at INI parse
time. Every measured dispatch→completion duration therefore has a direct, checkable anchor in the
installed game data. A golden value that is inconsistent with its INI anchor is wrong, regardless of
how reproducible the capture was.

| Action kind | INI anchor (read from the install) | What the measured frames should be |
|---|---|---|
| Attack | unit's primary `WeaponTemplate`: `PreAttackDelay.Min`, `CoolDownDelayBetweenShots.Min`, `AttackRange` | ≥ `PreAttackDelay.Min` (hard floor — a unit cannot reduce target health before its pre-attack delay); ≈ close-to-range frames + preAttack + first shot |
| Move | unit's `LocomotorTemplate.MaxSpeed` (world-units per frame) | `distance(layout) / MaxSpeed`; distance is deterministic from the `SpawnPlan` |
| Build | target `ObjectDefinition.BuildTime` (already in frames) | ≈ `BuildTime` + dozer approach |
| Gather | `SupplyAIUpdateModuleData.SupplyWarehouseActionDelay` × boxes + `SupplyCenterActionDelay` | sum + two travel legs |
| SpecialPower | unit's `SpecialPower.ReloadTime` (and power-specific effect delay) | ≥ the power's delay |

Worked example — a Ranger (USA infantry) attacking its assigned target:

- The formation is six interleaved parallel rows (units, targets, units, targets, ...); each unit's
  target sits `TargetApproachDistance` (90 world-units) ahead on an adjacent row, within weapon
  range. The formation geometry is configured (not in source) by the checked-in
  `src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-scenario.json`, loaded via
  `ValidationGroundScenario.Standard()`; `ValidationGroundBuilder` turns it into the spawn plan.
- Ranger's primary weapon `PreAttackDelay` and `AttackRange` come straight from
  `WeaponTemplate` in `Data/INI/Weapon/*.ini`.
- Attackers are commanded directly (`CurrentWeapon.SetTarget` on the in-range target), so the
  measured frames are weapon-dominated: ≈ `PreAttackDelay.Min` + shots-to-kill at the standardized
  `TargetHp`. There is no travel leg for attack.
- The committed golden must be at least `PreAttackDelay.Min`; the tolerance band covers only the
  first-shot jitter above that anchor.

Automated, data-grounded gate: the `[GameFact]` test
`BaselineGoldenFrames_AreConsistentWithWeaponIniData` loads the install, resolves each Attack
entry's primary `WeaponTemplate`, and fails if the golden frame count is below the weapon's
`PreAttackDelay.Min` (an impossible value) or missing a resolvable weapon. Run it after every
refresh; it is the proof the baseline is grounded in the game's own action-speed data.

Setting tolerances:

- Per entry, never global. A single global tolerance is simultaneously too loose for short actions
  and too tight for long ones.
- Combine a percentage of the INI-anchored golden with a minimum absolute frame floor (the
  `UnitTimingBaselineEntry.TolerancePercent` + `MinimumFrameFloor`), so a one-frame jitter on a
  20-frame action does not fail while a real slowdown on a 150-frame action does.
- Tune around the INI anchor, not the single capture: if the measured value drifts far from the
  anchor, investigate before accepting it.

### Reading gaps and timeouts

The stdout table has one row per roster unit. Verdicts:

- `OK` / `PASS` / `NEW`: completed within tolerance (NEW = unit not yet in the baseline).
- `REGRESSED`: completed but slower than the tolerance allows; headless exits non-zero.
- `TIMEOUT`: the action never completed within its frame budget; distinct from a regression.
- `GAP`: the unit/action could not be measured (unimplemented handler, unresolved definition, or
  spawn-versus-production misbehaviour). The reason is printed on the following line.

Gaps surfaced by a run escalate to P6b (unimplemented handlers) or P5 (spawn-versus-production
differences); record them in `.exchange`, do not fix them inside the validation ground.
