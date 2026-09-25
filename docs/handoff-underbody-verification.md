# Handoff: car underbody texture and flipped-car physics

Audience: verifying agent. Scope: everything changed in the "flipped cars are transparent and
sink into the ground" workstream, how to independently verify each piece, and known limitations.
All commands assume repo root `/home/rmessinger/dev/Panopticon` and
`CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals"`.

## Summary of changes

Two problem classes were fixed:

1. Flipped cars rendered hollow from below: civilian car W3D models have no underbody geometry
   and single-sided meshes.
2. Flipped cars sank into or hovered over uneven terrain: the Bepu adapter used independent
   body-lowest and terrain-highest corners instead of comparing terrain at each corner.

## Repository changes

| File | Change |
|---|---|
| `src/Panopticon.Tools.W3dPatcher/` (new project, in solution) | CLI + `W3dUnderbodyPatcher.Patch`: transforms the body bounds through its complete bind-pose bone chain, creates a horizontal model-space grey `UNDERBODY`, transforms it back to body-local space, and reuses the body mesh's HLod bone. Single-mesh no-HLod files get a synthesized hierarchy + HLod. |
| `src/Panopticon.FileFormats.W3d/W3dVertexMaterialInfo.cs` | Bug fix: chunk id written as 0x28 instead of 0x2D; written files failed to re-parse. |
| `src/Panopticon.FileFormats.W3d/W3dShader.cs` | Bug fix: struct's 16th byte was discarded; now preserved as `RawTrailingByte`. |
| `src/Panopticon.IO/DiskFileSystem.cs` | `GetFile` falls back to a segment-wise case-insensitive path walk, so loose override files load on case-sensitive filesystems regardless of query casing. |
| `src/Panopticon.Simulation/Logic/Object/Behaviors/PhysicsBehavior.cs` | Bepu adapter: body-center offset rotates with the object; box Z half-extent comes from `GeometryHeight/2`; ground correction uses the maximum paired `terrainZ - cornerZ` penetration. |
| `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs` + `ScenarioCaptureRunner.cs` | Actors support `PitchDegrees`/`RollDegrees` (roll 180 = upside down) and follow-camera captures. Actor `Z` is documented as absolute world Z. |
| `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-car-flipped.json` (new, embedded) | Isolation scene: one `CarSwatchEuro` spawned rolled 180 at absolute Z 65, with a camera that follows its downhill drift. |
| `test/Panopticon.Simulation.Tests/Data/W3d/W3dUnderbodyPatcherTests.cs` (new) | 6 tests, including model-space placement and winding checks for rotated-axis `CarAsian1` and identity-axis `CarSwatchEuro`. |
| `test/Panopticon.Simulation.Tests/Data/DiskFileSystemTests.cs` (new) | Case-insensitive `GetFile` resolution. |
| `src/Panopticon.Tools.TestHarness/AGENTS.md` | Documents pitch/roll spawn fields and loose-override loading. |

## Deployed artifacts (game install, not in repo)

Patched models live as loose overrides in
`/home/rmessinger/dev/Command and Conquer Generals/Art/W3D/` (exact archive-entry casing):
`CVAsnCar1.W3D`, `CVBug3.W3D`, `CVCmpCar.W3D`, `CVBMW2.W3D`, `CVSPTCAR01.W3D`, `CVSUV01.W3D`,
`CVSwatch01.W3D`. Delete a file to fall back to the archive version. Unpatched originals are in
`W3D.big` under `Art\W3D\`.

Rationale for the model set: `CarAsian1` (stack cars) plus the six car definitions statically
placed by the Alpine Assault map (`CarBug3`, `CarCompact`, `CarLuxury02`, `CarSport01`,
`CarSUV01`, `CarSwatchEuro`). The white/blue wagons are `CarSwatchEuro`/`CVSwatch01`.

## Verification procedures

### Unit and round-trip tests

```sh
CNC_GENERALS_PATH="..." dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~W3dUnderbodyPatcher"
CNC_GENERALS_PATH="..." dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~DiskFileSystemTests"
```

Expected: 6 + 1 passed.

### Isolation capture (single flipped car)

```sh
dotnet build src/Panopticon.Tools.TestHarness -c Release
CNC_GENERALS_PATH="..." dotnet run --project src/Panopticon.Tools.TestHarness -c Release --no-build -- \
  --headless --built-in-scenario ScenarioCapture --scenario capture-car-flipped.json
```

Output: `/tmp/panopticon_capture_car-flipped/` (70 PNGs + `manifest.json`).

Manifest checks (Python, JSON is plain here): the single actor's `WorldZ` starts at 65, falls,
bounces once, and settles. Settled origin Z must equal terrain height at the final
`WorldX/WorldY` plus the box height (9 for `CarSwatchEuro`), within ~0.5 for slope contact.
Terrain at spawn (540,600) is about 18.75; the car drifts downhill while bouncing, so sample
terrain at the final position (use `HeightMap.GetHeight` via a small probe, or the map editor).

The follow camera keeps the drifting car in frame. In the current corrected capture, its origin
moves from `(540, 600, 65)` to approximately `(539.7, 718.3, 26.0)` and its final speed is
approximately `0.0032` world units per frame.

Visual checks on `frame_0069.png` (settled and centered):
- Car rests on its roof: wheels point up, roofline contacts ground, contact shadow beneath.
- Belly (upward face) is a closed flat grey plane: crisp edges, uniform grey, zero
  terrain-green pixels inside the car silhouette.
- Critical false positive: the ground shadow decal is also grey. A decal lies on the terrain,
  has soft edges, and terrain texture shows around/through it; the real plane has hard polygon
  edges and fully occludes terrain. Earlier verification rounds were fooled by this; instruct
  any image-inspection agent to distinguish the two explicitly.
- The car drifts about 118 world units downhill during bouncing. The follow camera keeps its
  fixed height of 110 and pans to the final position.

### Full stack capture

```sh
CNC_GENERALS_PATH="..." dotnet run --project src/Panopticon.Tools.TestHarness -c Release --no-build -- \
  --headless --built-in-scenario PhysicsStack
python3 scripts/annotate_capture_gif.py /tmp/panopticon_physics_stack \
  "/mnt/pit2/Network Storage/temp/physics_stack_underbody.gif" --labels
```

Current console: benchmark timings (0.034/0.094/2.941 ms per Timestep at 100/1k/10k bodies),
run-to-run drift 0.00, "all 80 captured frames within the loose scatter bound", exit 0.

The profile uses an explicit camera centered at `(540, 600)` with height 350, so identical
positions no longer produce different apparent scales between runs. The corrected run ends
with actor origins at Z 18.75 through 30.89 and final maximum speed 0.039 per frame.

Visual pass over `frame_0079.png` (all regions, zoomed): exposed grey planes stay attached to
their cars and align with each car's floor rather than its side. No terrain is visible through
an exposed underside. Cars can settle on their sides or ends because their collision shapes
remain simple boxes.

Current shared GIF (live viewer at https://files.reid-corp.com/viewer/):
`physics_stack_underbody.gif` in `/mnt/pit2/Network Storage/temp/`.

### A/B against unpatched models

Move one loose file out of the install's `Art/W3D/`, re-run the relevant capture, restore the
file. With the `DiskFileSystem` fix, presence/absence of the loose file now demonstrably
switches the model (this A/B is how the case-sensitivity bug was found). Pixel-diff whole
frames only on scenes without ambient effects; on Alpine Assault, ambient noise makes
full-frame diffs uninformative (see `docs/screenshots.md`).

## Known limitations and follow-ups

- Damaged-state models (`*_D.W3D`, `*_d*.W3D`) are not patched. Cars in these captures never
  drop below full health (verified via manifest `Health`), but combat scenes swapping to
  damaged models will render hollow from below again. Patching them is a mechanical re-run of
  the tool per model.
- Byte-exact W3D round-trip against original files is impossible in general: originals carry
  garbage in the unused tail of fixed-length strings (reader trims at NUL, writer zero-pads).
  Patched files are valid and byte-stable across re-serialization cycles (test-enforced).
- The collision box is the INI `Geometry` box, which only approximates the visual model: the
  `CarAsian1` box height (7) vs model height (~9.7) leaves flipped cars resting ~1.3 units
  proud; `CarSwatchEuro` matches closely (9 vs ~8.7). Exact rest contact needs per-model
  collision shapes (roadmap Track F scope).
- Bepu terrain contacts and the SAGE-compatible corner clamp coexist. The clamp corrects only
  the largest paired corner penetration; it does not use independent corner extrema.
- `capture-car-flipped.json` is an embedded resource: rebuild the TestHarness after editing
  it (`--no-build` runs a stale profile).
- Capture actor `Z` is absolute world height, not height above ground.
- Camera `AutoFit` needs more than one subject point. The flipped-car profile uses `Follow`;
  the stack profile uses `Explicit` for stable framing.
- `PhysicsStackRunner.cs` has a pre-existing unused-variable warning (`fieldMin`), not from
  this workstream.
- Pre-existing test failures unrelated to this work (reproduce on clean HEAD):
  `CurFileTests.CanReadAniCursors`, `SteamLocatorTests.FindInstallationsFindsGeneralsOnThisMachine`,
  `BseFileTests.CanRoundtripBases`, `MapFileTests.CanRoundtripMaps`, `ScbFileTests.CanRoundtripScbFiles`,
  `WndFileTests.CanReadWndFiles`, `P2ScenarioMatrixTests.Matrix_AllCells_AIPlaysLegalGame`.

## Status of prior verification claims

Verification history matters because two earlier "verified fixed" claims were wrong: first the
loose overrides silently failed to load (case-sensitive filesystem, since fixed in
`DiskFileSystem`), then the flipped car was buried under the terrain so the visible "grey
belly" was the shadow decal. A later full-stack PASS was also invalid: `CarAsian1` uses local Y
as model up, while the patcher assumed local Z. The current captures postdate the
bind-pose-aware patcher, paired-corner terrain correction, follow camera, and fixed stack
camera. Reproduce them from the procedures above rather than relying on an earlier verdict.
