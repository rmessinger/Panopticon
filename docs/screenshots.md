# Screenshot capture and sharing

> Environment-specific recipes for producing and sharing screenshots from agent shells on the
> Linux dev host. Amend this file whenever a new capability or limitation is found, so the
> working method never has to be re-derived.

## Sharing with the user

Copy the PNG into `/mnt/pit2/Network Storage/temp/` (the path contains spaces; quote it). That
directory is served read-only by the live viewer at `https://files.reid-corp.com/viewer/`, which
shows the newest image automatically: no refresh, upload, or login. Overwrite the same filename
for iteration snapshots so the viewer swaps instantly.

```sh
cp /tmp/opensage_captures/capture_0100.png "/mnt/pit2/Network Storage/temp/shot.png"
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

## Recipe: 3D scene screenshots (headless, reliable)

The TestHarness renders offscreen with Vulkan and captures PNGs; no display is needed.

Replay scenarios: set `"CaptureFrames": true` in `TestHarness.json` and run `--headless`; a
capture is written after every completed replay action.

Unit Validation Ground: set the `TESTHARNESS_VG_CAPTURE` environment variable; every logic step
renders and captures a frame (capped at 500 files) into `/tmp/opensage_captures/capture_NNNN.png`.

```sh
rm -f /tmp/opensage_captures/*
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
TESTHARNESS_VG_CAPTURE=1 \
dotnet run --project src/OpenSage.Tools.TestHarness --no-build -- --headless
ls /tmp/opensage_captures/
```

Exit code 3 from a VG run means "frame cap with no baseline", which is expected when the
scenario outlives its frame budget; captures remain valid.

## Recipe: windowed runs and the ImGui overlay (Xvfb + llvmpipe)

Status: does not work. llvmpipe renders (CPU fully busy) but no frame was ever presented to the
Xvfb X server in 10+ minutes; the captured screen stays at the window background. Documented so
the attempt is not repeated blindly:

```sh
Xvfb :99 -screen 0 1600x1000x24 &
DISPLAY=:99 VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json dotnet run --project src/OpenSage.Tools.TestHarness
ffmpeg -y -f x11grab -video_size 1600x1000 -i :99 -frames:v 1 out.png   # captures only the window background
```

RADV cannot present under Xvfb either (no DRI3), the OpenGL backend dies in the Veldrid.SPIRV
GLSL bug, and the real X server rejects agent-shell auth. Use the offscreen recipe below instead.

## Recipe: overlay / tooltip screenshots (headless, reliable)

The validation-ground ImGui overlay can be rendered offscreen into the same framebuffer as the
scene, so tooltips and the status window are capturable without any display. This is the
documented way to screenshot overlay visuals.

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
TESTHARNESS_VG_OVERLAY_SHOT=1 \
TESTHARNESS_VG_HOVER_OBJECT=AmericaInfantryRanger \
dotnet run --project src/OpenSage.Tools.TestHarness --no-build -- --headless
# frames with scene + tooltip land in /tmp/opensage_captures/capture_NNNN.png
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

## Recipe: ReplaySketch validation-ground editor (2D, no display)

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
dotnet run --project src/OpenSage.Tools.ReplaySketch.Avalonia --no-build -- \
  --screenshot "/mnt/pit2/Network Storage/temp/vg_avalonia.png"
```

Renders the editor scene via SkiaSharp, headless; works on Linux. The MAUI front-end's
screenshot mode (`REPLAYSKETCH_SCREENSHOT_PATH`, see `src/OpenSage.Tools.ReplaySketch.Maui/AGENTS.md`)
requires a MAUI platform runtime and does not run on Linux.

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

## History

Past session transcripts are stored in `~/.local/share/opencode/opencode.db` (SQLite, `part`
table); mining them for `xvfb`/`ffmpeg`/`pit2` commands recovers prior solutions.
