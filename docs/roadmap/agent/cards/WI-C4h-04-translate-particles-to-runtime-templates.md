# Task Card `WI-C4h-04`: Translate particles to runtime templates

| Field | Value |
|---|---|
| Card ID | `WI-C4h-04` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4h-03` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Materialize imported particle effects into existing Panopticon particle templates with deterministic
random seeding and no source-format branches in per-frame runtime code.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 6, 7, 9, and 10.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/FXParticleSystemTemplate.cs:11-172`.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystem.cs:232-573`.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystemUtility.cs`.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystemManager.cs:32-112`.
- Imported particle values from `WI-C4h-03`.

## Steps

1. Add `ImportedParticleEffectMaterializer` under the existing import materialization namespace.
2. Convert each `ImportedParticleSystem` into one `FXParticleSystemTemplate` using existing color,
   alpha, update, physics, draw, emission velocity, and emission volume modules.
3. Convert birth rate to a stable 30 Hz burst schedule and particle lifetime to frame ranges.
4. Map grow-fade and color-over-life curves to the existing color, alpha, size, and size-rate
   fields. Reject curves that cannot fit eight key slots.
5. Add a request-scoped seed API to `ParticleSystemUtility` and tests that prove repeated capture
   setup produces identical random samples.
6. Add materializer tests that compare templates for the three proof descriptors and assert no
   live `ParticleSystem` is created during materialization.
7. If done, promote `WI-C4h-05` to `Ready`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Loaders/ImportedParticleEffectMaterializer.cs` (new)
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystemUtility.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/ImportedParticleEffectMaterializerTests.cs` (new)
- `test/Panopticon.Simulation.Tests/Graphics/ParticleSystems/ParticleSystemUtilityTests.cs` (new)

## Acceptance criteria

- [ ] Materialization creates only existing `FXParticleSystemTemplate` values and attachment plans.
- [ ] The existing runtime update and render paths contain no NIF-specific conditions.
- [ ] Particle randomness can be reset per proof effect and repeats exactly.
- [ ] Unsupported emitter, modifier, key, or texture semantics fail before live system creation.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ImportedParticleEffectMaterializerTests|FullyQualifiedName~ParticleSystemUtilityTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not change INI particle parsing behavior for shipped SAGE games.
- Do not add GPU particle simulation or a second renderer.
- Do not loosen particle key-slot limits without a focused follow-up design.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4h-05` to `Ready`.
- Append a result line to `.exchange`.
