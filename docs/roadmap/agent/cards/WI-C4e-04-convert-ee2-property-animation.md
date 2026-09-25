# Task Card `WI-C4e-04`: Convert EE2 property animation

| Field | Value |
|---|---|
| Card ID | `WI-C4e-04` |
| Roadmap phase | [Track C · C4e](../../track-c-external-assets.md): EE2 property animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4e-01`, `WI-C4e-02`, `WI-C4e-03`, `WI-C4c-04` |
| Diagrams | [C4e architecture diagrams](../../deep-dives/c4e-ee2-property-animation.md#11-architecture-diagrams) |

## Goal

Teach `NifAnimationImporter` to convert EE2 property controllers into C1 property channels while preserving the C4c transform importer behavior.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4e-ee2-property-animation.md` sections 2, 5, 6, 8, and 9.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 4, 6, and 7.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationImporter.cs` from `WI-C4c-04`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationConverter.cs` from `WI-C4c-04`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifPropertyTargetMap.cs` from `WI-C4e-02`.
- `test/Panopticon.Simulation.Tests/Content/Importing/NifAnimationImporterTests.cs`.

## Steps

1. Extend the C4c NIF animation converter with a property-controller dispatch path.
2. Validate controller flags, timing, data block type, key type, texture slot, shader-map byte, material-color enum, visibility byte values, and alpha ranges per the deep dive.
3. Resolve each property target through the C4e target map. Emit one channel per resolved sub-object or material slot.
4. Emit `Visibility` step channels for `NiVisController` and `NiBoolData` or `NiVisData`.
5. Emit UV float channels for `NiTextureTransformController` and embedded `NiUVController` when the supported slot and operation rules hold.
6. Emit material color `Vector3` channels and material alpha float channels.
7. Add installed-asset tests for the Market, Egyptian EIFV, Stealth Fighter Bomber, ResourceFish, embedded wall connector, embedded buoy, and `bld_farm_yam.kf` failure cases.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationImporter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationConverter.cs`
- `src/Panopticon.Simulation/Content/Importing/Nif/NifPropertyAnimationConverter.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/NifAnimationImporterTests.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifPropertyAnimationTests.cs` (new)

## Acceptance criteria

- [ ] Each proof clip emits the channel families and target counts listed in the deep dive.
- [ ] `bld_farm_yam.kf` decodes its material-color blocks but fails because morph animation is unsupported.
- [ ] Unsupported texture slots, shader maps, flags, missing targets, and alpha ranges fail with asset context.
- [ ] C4c rigid proof clips still import and bind unchanged.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifAnimationImporterTests|FullyQualifiedName~Ee2NifPropertyAnimationTests"
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifPropertyAnimationTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add renderer state in this card.
- Do not parse DDF state machines.
- Do not support `NiFlipController`, morph controllers, or NIF 10.0.1.0.
- Do not copy EE2 files into the repository.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done and `WI-C4e-05` is done, change `WI-C4e-06` to `Ready`.
- Append a result line to `.exchange`.
