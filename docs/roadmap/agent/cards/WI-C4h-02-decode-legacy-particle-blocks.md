# Task Card `WI-C4h-02`: Decode legacy particle blocks

| Field | Value |
|---|---|
| Card ID | `WI-C4h-02` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4h-01`, `WI-C4c-03` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Extend `Panopticon.FileFormats.Nif` so the three C4h proof particle effects parse to exact EOF and
unsupported particle families fail explicitly.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 4 and 10.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 3 and 4.
- `src/Panopticon.FileFormats.Nif/NifFile.cs:118-155` and `NifFile.cs:157-312`.
- `src/Panopticon.FileFormats.Nif/Blocks/NifGeometry.cs`, `NifGeometryData.cs`, and
  `NifTriShapeData.cs`.
- The C4c key-group types and time-controller blocks added by `WI-C4c-01` and `WI-C4c-03`.

## Steps

1. Add parser blocks for `NiParticles`, `NiParticlesData`, `NiParticleSystemController`,
   `NiParticleColorModifier`, `NiParticleGrowFade`, `NiGravity`, `NiColorData`, `NiFloatData`,
   `NiParticleBomb`, `NiBillboardNode`, `NiTriStrips`, `NiTriStripsData`, and `NiStencilProperty`.
2. Reuse the C4c key-group and time-controller helpers for adjacent alpha, material-color,
   keyframe, vector, and float data blocks.
3. Register the legacy particle block types in `NifFile.ReadBlock`.
4. Register observed `NiPSys*`, morph, UV, texture-transform, and bomb-force block names so they
   throw `NotSupportedException` with the NIF path and block index instead of a generic unsupported
   block message.
5. Add reference validation for particle data, controllers, modifier chains, color data, float
   data, emitter refs, and collider refs.
6. Add parser tests that build synthetic byte streams for one particle system, a grow-fade chain,
   a color modifier, `NiGravity`, `NiStencilProperty`, and a rejected `NiPSysData` block.
7. Extend the installed proof test to read the three proof effects through the full parser and
   assert exact EOF.
8. If done and `WI-C4h-03` has no other blocker, promote `WI-C4h-03` to `Ready`.

## Files likely to change

- `src/Panopticon.FileFormats.Nif/NifFile.cs`
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticles.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticlesData.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticleSystemController.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticleModifier.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticleColorModifier.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifParticleGrowFade.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifGravity.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifColorData.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifFloatData.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifTriStrips.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifTriStripsData.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifBillboardNode.cs` (new)
- `src/Panopticon.FileFormats.Nif/Blocks/NifStencilProperty.cs` (new)
- `test/Panopticon.Simulation.Tests/Data/Nif/NifParticleTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2ParticleEffectInventoryTests.cs`

## Acceptance criteria

- [ ] The three proof effects parse to exact EOF through `NifFile.Read`.
- [ ] Synthetic tests cover every new supported block and each explicit unsupported family.
- [ ] Bad references fail as `InvalidDataException` with owner block context.
- [ ] `NiPSys*`, bomb, morph, UV, and texture-transform features fail with named
      `NotSupportedException` messages.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifParticleTests"
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2ParticleEffectInventoryTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add particle interchange or runtime materialization.
- Do not approximate unsupported modifiers by silently dropping them.
- Do not add NIF 10.0.1.0 support.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4h-03` to `Ready`.
- Append a result line to `.exchange`.
