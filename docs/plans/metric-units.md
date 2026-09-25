# Metric Units Plan

Status: proposal; not started; last updated 2026-08-27.

## What it is

Make meters the unit developers and tools see, without changing the engine's internal
simulation unit or breaking compatibility with original-game assets, replays, and save
games.

## Background

The engine's distance unit is the foot (see
`docs/system-investigations/physics-system.md` section 2 for the evidence:
`GameData.ini` "our distance units are roughly one foot"). That scale is baked into
every binary and data format Panopticon consumes:

| Surface | Where the unit is fixed |
|---|---|
| Map terrain | `.map` heightmap: 10-unit grid cells, vertex heights in world units |
| 3D models | W3D vertex coordinates authored in the same world units |
| Game data | every INI length (ranges, radii, PreferredHeight, geometry) and gravity |
| Replays | order positions serialized in world units; ReplaySketch plays original `.rep` files |
| Save games | persisted positions, velocities, and accelerations |

## Decision: presentation layer only

Two full-conversion variants exist, and both are rejected:

1. Numeric rescale (multiply every length by 0.3048 everywhere, including gravity and
   speeds). Physics is scale-covariant, so the simulation would be numerically
   identical, merely relabeled. Enormous diff, zero behavioral change, and every
   file boundary (maps, W3D, INI, replays, saves) now needs a scale factor applied
   on load and inverted on save.
2. Relabel (keep the numbers, declare them meters). Breaks every physically derived
   constant (gravity would be 64 m/s2), changes nothing visually, and corrupts
   interop with original content whose numbers really are feet.

Consequence: the internal unit stays the foot. "Converting to metric" means a
conversion surface at human-facing boundaries: tools, inspectors, validation
artifacts, and documentation.

## Design

### Single conversion source

`Panopticon.Mathematics.WorldUnits`:

```csharp
public static class WorldUnits
{
    public const float MetersPerFoot = 0.3048f;
    public static float ToMeters(float feet) => feet * MetersPerFoot;
    public static float ToFeet(float meters) => meters / MetersPerFoot;
}
```

No other file hardcodes 0.3048. Unit tests cover round-trip precision at map-scale
magnitudes (up to 100000).

### Documentation

One authoritative statement of the unit system in `docs/developer-guide.md` (new
"Units" section: distance = foot, time = 30 logic frames/sec, angles = degrees in
data, radians in code). `docs/maps.md` and the AGENTS.md environment notes link to it
instead of restating it; existing world-unit facts are annotated with meters where a
human reads them, not rewritten.

## Implementation steps

1. Add `WorldUnits` to `src/Panopticon.Mathematics` with round-trip tests.
2. Add the "Units" section to `docs/developer-guide.md`; cross-link from
   `docs/maps.md` and AGENTS.md.
3. ReplaySketch: map panel gains a scale bar and position tooltips in meters (logic
   untouched; UI-side conversion only). Verify with the screenshot-mode gate.
4. TestHarness: `EngagementCapture` manifest gains meters columns next to the
   world-unit ones (annotation script follows in a later step; keep both units so
   existing tooling that reads the manifest does not break).
5. ImGui inspectors (`PhysicsBehavior.DrawInspector`, locomotor inspector): display
   dual units for distances, speeds, and accelerations.
6. Validation artifacts: `scripts/annotate_engagement_gif.py` and future trace CSVs
   report meters alongside world units; update the deep-dive docs' measurement
   tables only when next touched, not retroactively.

Steps 1 and 2 deliver the core value; steps 3 through 6 are independent and can be
taken in any order.

## Optional step: unit declaration for authored content

If and when Panopticon authors new content (the WorldBuilder-requirements editor, a mod
pipeline), that pipeline works in meters internally and converts on export to the
foot-based formats, with an explicit unit declaration in the project file. Imported
original content is never rescaled.

## Rejected alternative: full internal conversion

For the record, a full conversion would touch: the map heightmap loader, the W3D
loader, all INI parse-time converters (`ParseVelocityToLogicFrames` and friends in
`src/Panopticon.Simulation/Data/Ini/IniParser.cs`), `GameData` gravity, replay order
serialization (breaking playback of original `.rep` files, ReplaySketch's core
function), save-game round trips, pathfinding cell sizes, camera constraints, and
every numeric assertion and validation baseline. Effort is measured in weeks, and the
outcome is worse interop for a relabeling. Do not revisit without a hard requirement
such as netplay co-hosting with a metric-native engine.

## Verification

- Standard gates: `dotnet build src`, `dotnet format src --verify-no-changes`,
  `dotnet test src --filter Category!=Interactive`, `scripts/check_brace_newline.py`.
- No changes under `src/Panopticon.Simulation` logic beyond adding the read-only helper:
  replay determinism and the ReplaySketch unit-timing baseline must remain
  byte-identical.
- ReplaySketch screenshot mode still renders (per the AGENTS.md validation rule).

## Out of scope

- Retroactively converting numbers in existing investigation docs.
- Converting the annotator's pixel-space geometry (screen, not world).
- Any change to asset loading or serialization.
