# Task Card `WI-G6-03`: Clamp particle opacity at the presentation boundary

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-G6-03` |
| Roadmap phase | [G6 corrective follow-up](../../track-g-rendering-clarity.md#phase-resources) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [Particle presentation boundary](../../plans/macos-battle-artifacts.md#particle-presentation-boundary) |

## Goal

Prevent negative or over-range particle opacity from corrupting blended colors
under both rendering presets. Cover all particle templates, not only Aurora smoke.

## Context to read (and only this)

- [Evidence and reproduction contract](../../plans/macos-battle-artifacts.md).
- `src/Panopticon.Simulation/Assets/Shaders/Particle.frag`: texture/color/alpha output.
- `src/Panopticon.Simulation/Graphics/ParticleSystems/ParticleSystem.cs`: `UpdateParticle`, `UpdateVertexBuffer`.
- `src/Panopticon.Simulation/Graphics/Shaders/ParticleShaderResources.cs`: alpha and additive pipelines.
- `src/Panopticon.Rendering/Shaders/ShaderCrossCompiler.cs`: shader hash and cache invalidation.
- `test/Panopticon.Simulation.Tests/Graphics/ParticleSystems/ParticleSystemUtilityTests.cs`: existing test conventions.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs` and `src/Panopticon.Tools.ReplaySketch.Core/Model/Capture/CaptureProfile.cs`: capture profiles and assertions.

## Steps

1. Clamp final sampled opacity to `[0, 1]` in the shared particle fragment shader.
   Preserve RGB and existing blend factors, including additive effects authored
   with zero alpha. Do not modify simulation interpolation or random sampling.
2. Add a graphics-backed regression asserting that negative input opacity renders
   like zero, opacity above one renders like one, and an in-range value still blends.
   Use existing graphics-test fixtures; report unavailable graphics explicitly.
3. Add `capture-aurora-opacity.json` using the plan's reproduction contract. Capture
   the late smoke interval on Metal with the normal compiled shader cache and both
   `Legacy` and `Enhanced`. Inspect the full sequence and a windowed capacity probe.
4. Confirm the shader source change invalidates the normal cache through its SPIR-V
   hash. A hand-edited cache or installation override is not an implementation.

## Files likely to change

- `src/Panopticon.Simulation/Assets/Shaders/Particle.frag`
- `test/Panopticon.Simulation.Tests/Graphics/ParticleSystems/ParticleOpacityRenderingTests.cs` (new)
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-aurora-opacity.json` (new)
- Existing project content includes only if profile discovery requires them.

## Acceptance criteria

- [ ] GPU regression covers opacity below zero, above one, and inside the valid range.
- [ ] Late-life Aurora smoke has no black/neon corruption on Metal under either preset.
- [ ] Alpha-zero additive flames remain visible; smoke fades normally.
- [ ] Normal builds and shader-cache loading retain the fix without asset overrides.
- [ ] Particle lifetime, authored keyframes, RNG consumption, and serialized state are unchanged.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario ScenarioCapture --scenario capture-aurora-opacity.json
dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario MassBattleCapacityProbe
```

Set `CNC_GENERALS_PATH` to the real installation. Repeat captures with each graphics
preset through existing TestHarness settings. Preserve suspect determinism/golden
deviations and await the operator's determination; do not regenerate baselines.

## Out of scope / guardrails

- No per-template fixes, texture recoloring, blend-mode redesign, or jet lifecycle changes.
- Do not claim removal of unrelated black geometry from this particle-only change.

## On completion

- Set Status to `Done`, update the plan table and corrective checklist item.
- Append validation and capture locations to `.exchange`; retain the shipped G6 milestone.
