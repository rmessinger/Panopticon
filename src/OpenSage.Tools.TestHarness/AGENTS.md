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
