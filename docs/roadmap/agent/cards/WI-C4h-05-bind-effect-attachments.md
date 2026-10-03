# Task Card `WI-C4h-05`: Bind effect attachments

| Field | Value |
|---|---|
| Card ID | `WI-C4h-05` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C4h-04`, `WI-C4c-04` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Bind materialized particle effects to DDF world origins or C4c node bones so proof effects spawn
at the correct model or scenario location.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 2, 8, and 10.
- `docs/roadmap/deep-dives/c4c-ee2-rigid-animation.md` sections 5 and 6.
- `src/Panopticon.Simulation/FX/FXNuggets/ParticleSystemFXNugget.cs:9-94`.
- `src/Panopticon.Simulation/FX/FXNuggets/FXListAtBonePosFXNugget.cs`.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystemManager.cs:32-63`.
- `src/Panopticon.Simulation/Graphics/ModelInstance.cs` and `ModelBoneInstance.cs`.

## Steps

1. Add a small DDF-effect binding helper for the proof data shape: effect name, NIF file name,
   optional tag or bone name, local offset, world scale, follow mode, and one-shot mode.
2. Resolve proof DDF entries for `fx_explosion_1`, `fx_muzzle_tank`, and `fx_watersplash_1` to
   imported particle effect keys.
3. Resolve attachment names against the C4c runtime bone hierarchy with exact case-insensitive
   matching. Fail on missing or duplicate names.
4. For follow-bone effects, use `ParticleSystemManager.Create(template, getWorldMatrix)`. For
   one-shot world effects, use `Create(template, worldMatrix)`.
5. Add tests for root attachment, tag attachment, missing tag failure, duplicate tag failure, and
   fixed world spawn.
6. If done, promote `WI-C4h-06` to `Ready`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Nif/NifParticleEffectBinding.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/Nif/NifParticleEffectAttachmentResolver.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/NifParticleEffectAttachmentResolverTests.cs` (new)
- `src/Panopticon.Tools.TestHarness/Ee2ParticleProofScenario.cs` (new or updated when created)

## Acceptance criteria

- [ ] DDF proof entries resolve to the expected NIF particle effect asset keys.
- [ ] Follow-bone effects use the live model bone matrix and fixed effects use a fixed world matrix.
- [ ] Missing and duplicate attachment names fail before `ParticleSystemManager.Create`.
- [ ] Attachment scale and offset are applied once and are deterministic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NifParticleEffectAttachmentResolverTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement full EE2 DDF state machines.
- Do not change Generals FX behavior except through shared bug fixes required by the new tests.
- Do not add ReplaySketch UI in this card.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4h-06` to `Ready`.
- Append a result line to `.exchange`.
