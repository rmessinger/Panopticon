# Task Card `WI-C4h-03`: Add particle interchange

| Field | Value |
|---|---|
| Card ID | `WI-C4h-03` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4h-02` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Add C1-style CPU-only particle effect import values and a NIF converter that emits them for the
three proof effects without touching the live particle runtime.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 5, 6, and 10.
- `docs/roadmap/deep-dives/c1-import-abstraction.md` sections 1, 2, 4, and 6.
- `src/Panopticon.Simulation/Content/Importing/ImportedModel.cs`, `ImportedMaterial.cs`, and
  `ImportedAnimation.cs`.
- The NIF particle blocks added by `WI-C4h-02`.

## Steps

1. Add immutable import values for `ImportedParticleEffect`, `ImportedParticleSystem`,
   `ImportedParticleEmitter`, `ImportedParticleModifier`, and `ImportedParticleAttachment`.
2. Add validation in constructors: finite values, non-negative lifetimes, sorted key times,
   supported emitter kinds, non-empty texture names when a texture is required, and no duplicate
   system names in one effect.
3. Add `INifParticleEffectConverter` or an internal converter class under the NIF importing folder.
4. Convert each supported legacy particle system to the interchange values:
   - local transform and source-to-engine conversion;
   - lifetime, birth rate, speed ranges, initial color, initial size;
   - color-over-life and grow-fade modifiers;
   - static texture and material names.
5. Emit explicit unsupported markers or exceptions for gravity, bomb, flipbook, mesh emission,
   and controller features not yet materialized.
6. Add tests from synthetic NIF objects to interchange values, including validation failures for
   invalid ranges and unsupported features.
7. If done, promote `WI-C4h-04` to `Ready`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/ImportedParticleEffect.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedParticleSystem.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedParticleEmitter.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedParticleModifier.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/ImportedParticleAttachment.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Nif/NifParticleEffectConverter.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/NifParticleEffectConverterTests.cs` (new)

## Acceptance criteria

- [ ] Particle import values are CPU-only and contain no `AssetStore`, graphics device, or runtime
      particle objects.
- [ ] The converter emits deterministic descriptors for all supported fields in the three proof
      effects.
- [ ] Unsupported features fail or produce explicit diagnostics, never silent drops.
- [ ] Constructor validation rejects malformed imported values before materialization.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifParticleEffectConverterTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not create `FXParticleSystemTemplate` or live `ParticleSystem` instances.
- Do not parse DDF attachment tables in this card.
- Do not add a new particle runtime.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4h-04` to `Ready`.
- Append a result line to `.exchange`.
