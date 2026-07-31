# OpenSAGE TestHarness — Agent Guide

## Purpose

`OpenSage.Tools.TestHarness` is a windowed desktop tool that runs a scripted game scenario
against a real SAGE game installation. It is the primary tool for **visual validation** of
the OpenSAGE renderer on any platform.

Unlike the headless tests in `OpenSage.Game.Tests`, the TestHarness opens a real game window
using SDL2 and renders frames to the display via the platform's native graphics API
(Metal on macOS, Direct3D 11 on Windows, Vulkan on Linux).

---

## Running the TestHarness

```bash
dotnet run --project src/OpenSage.Tools.TestHarness
```

Configuration is in `TestHarness.json` (copied to the output directory on build).
Set `GamePath` to your game installation if auto-detection fails.

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

The `FrameCapturer` utility in `OpenSage.Game` reads back any GPU texture to CPU memory.
You can add capture calls directly in `Program.cs` to dump frames to disk:

```csharp
// Capture the current rendered frame and save as PNG
var captured = FrameCapturer.Capture(
    game.GraphicsDevice,
    game.Panel.Framebuffer.ColorTargets[0].Target);

// Convert to PNG and write to a temp path
using var image = ToImage(captured); // see FrameCaptureSession.ToImage in the test project
var path = Path.Combine(Path.GetTempPath(), $"opensage_frame_{frameNumber:D6}.png");
image.SaveAsPng(path);
Console.WriteLine($"[TestHarness] Frame saved: {path}");
```

For structured multi-frame capture with per-frame validation hints, use
`FrameCaptureSession` from `OpenSage.Game.Tests` as a reference implementation.
The session directory and a `validation_guide.md` are printed to stdout.

---

## Engine-action results file

Every TestHarness run (headless or windowed) writes a results text file recording every engine
`Order` processed during the run, for diffing two identically-configured runs for parity. It's
written unconditionally (no config flag) as a sibling of the exported replay, e.g.:

```
opensage_scenario_<guid>.rep         -> opensage_scenario_<guid>.results.txt
```

The path is printed to stdout at the end of the run (`Action results saved to: ...`). The file is
flushed periodically during the run (every 20 lines or 5 seconds, whichever first), not just at
exit, so a crash still leaves a usable partial file.

`ActionResultsWriter.WriteLine` also emits each line through NLog automatically, so every result
reaches both stdout and `opensage.log` (see `OpenSage.Core/NLog.config`'s catch-all rule) in real
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
$TMPDIR/opensage_frames/<test_name>_<timestamp>/
```

Look for the `[FrameCaptureSession] Session started:` line in test stdout to get the
exact path.

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
  dotnet run --project src/OpenSage.Tools.TestHarness
```

Headless (CI regression gate, exits non-zero on regression or timeout):

```sh
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
  dotnet run --project src/OpenSage.Tools.TestHarness -- --headless
```

Exit codes: `0` pass, `2` baseline regression or timeout when a baseline is configured,
`3` timeout with no baseline configured.

### Config knobs

All optional with defaults, in `TestHarness.json`:

| Knob | Purpose |
|---|---|
| `ValidationGroundMapPath` | Map to run on (default: Alpine Assault) |
| `ValidationGroundSettleFrames` | Frames to settle after spawn before issuing orders |
| `ValidationGroundBaselinePath` | Path to the golden baseline JSON (empty skips comparison) |
| `ValidationGroundRefreshBaseline` | `true` overwrites the baseline from the run instead of comparing |
| `ValidationGroundReportPath` | Where to write the JSON report artifact |

### Refreshing the baseline

The baseline lives at
`src/OpenSage.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json`. The checked-in
values are a live capture against a real install, taken with fixed-timestep deterministic stepping.
Re-capture after an engine change that could affect weapon timings by running headless once with
`ValidationGroundRefreshBaseline: true` and `ValidationGroundBaselinePath` pointed at the file, then
commit the result. Never widen a tolerance to make a failing unit pass; a genuine regression is the
signal this phase exists to produce. Before committing, run headless twice and confirm the two JSON
reports are byte-identical (the determinism check).

The capture relies on a few non-obvious mechanics, all wired in `Program.RunValidationGround` and
`ValidationGroundHost`: fixed-timestep logic stepping (`Game.EnableFixedLogicTimestep`) so frame
counts are reproducible; skirmish AI disabled on the roster players (per the "no AI" design); an
untouched CommandCenter per faction so the victory system does not defeat factions that start with no
buildings; one target per attacker, placed in weapon range and standardized to a low HP
(`ValidationGroundScenario.TargetHp`), so each unit's target-destruction completion is its own and
feasible for the whole roster; and direct weapon targeting for attackers (the `AttackObject` order's
move step suppresses fire for several units, so in-range attackers are commanded directly to isolate
the weapon delay). A unit that destroys its target has its weapon cleared so it stops firing.
Units the sandbox cannot measure (suicide, bomber, horde, transport, sniper that will not fire at the
structure target) are listed in `PrimaryActionCatalog.UnmeasurableAttackReasons` and recorded as gaps;
SpecialPower units have no implemented order path and are also gaps.

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
  - Windowed mode works wherever the engine renders normally.
  - Headless mode needs an offscreen-capable backend. Vulkan requires `libdl`; snap-confined .NET
    SDKs cannot load it, so on Linux use a non-snap .NET install or confirm `libvulkan`/`libdl`
    resolve. On Windows prefer D3D11; on macOS use Metal.
- The roster resolves against the install: the `[GameFact]`
  `EveryCatalogUnit_ResolvesAgainstRealInstall` test must pass (it fails the run if any catalog
  definition name is invalid).

##### Capture workflow

1. Configure `TestHarness.json` for a refresh capture:

   ```jsonc
   {
     "Scenario": "UnitValidationGround",
     "Game": "CncGenerals",
     "Headless": true,
     "GamePath": "/path/to/Command and Conquer Generals",
     "ValidationGroundBaselinePath": "src/OpenSage.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json",
     "ValidationGroundRefreshBaseline": true,
     "ValidationGroundReportPath": "/tmp/vg_report_run1.json"
   }
   ```

2. Run once: `dotnet run --project src/OpenSage.Tools.TestHarness --no-build`. Inspect the stdout
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

Worked example — a Ranger (USA infantry) attacking the target cluster:

- Spawn row sits 250 world-units from the cluster centre (`LayoutConfig.FactionRowDistance`).
- Ranger's primary weapon `PreAttackDelay` and `AttackRange` come straight from
  `WeaponTemplate` in `Data/INI/Weapon/*.ini`.
- Expected measured frames ≈ `(250 − AttackRange) / locomotorMaxSpeed + PreAttackDelay.Min`.
- The committed golden must be at least `PreAttackDelay.Min`; the tolerance band covers only the
  approach/pathing jitter above that anchor.

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
