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

Health changes are detected by polling `BodyModule.Health` each iteration (there's no engine
"health changed" event), the same pattern used for move completion. Objects already in the scene
when the recorder is constructed (map/scenario starting units) have their health seeded at
construction time so they don't produce a spurious first "change" entry. Objects with no
`BodyModule`, or no `Owner` (scenery/props), are skipped entirely.

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
