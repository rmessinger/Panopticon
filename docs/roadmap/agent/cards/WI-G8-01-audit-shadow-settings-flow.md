# Task Card `WI-G8-01`: Audit the shadow settings flow and bias application

> Audience: agent. Phase implementation task.

| Field | Value |
|---|---|
| Card ID | `WI-G8-01` |
| Roadmap phase | [Track G · G8](../../track-g-rendering-clarity.md): Shadow quality tier |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [G8 architecture](../../deep-dives/g8-shadow-quality.md#5-architecture-diagrams) |

## Goal

Trace every shadow setting from its default through to the shader that consumes it, and record what
already exists versus what G8 must add, so the cascade tier and bias change can be scoped precisely.
Read-only: no code changes.

## Context to read (and only this)

- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowSettings.cs:9-83`: the defaults, the
  power-of-two map size setter, the cascade split depths, `Bias`, and `NormalOffset`.
- `src/Panopticon.Simulation/Graphics/Rendering/Shadows/ShadowMapRenderer.cs`: how the settings reach the
  shader constants and how the cascades are built.
- `src/Panopticon.Simulation/Graphics/Shaders/GlobalShaderResources.cs`: the shadow constants structure and
  where `Bias` and `NormalOffset` are copied into it.
- `src/Panopticon.Simulation/Assets/Shaders/Shadows.h:55-70`: where the constant bias is subtracted from the
  light depth.

## Steps

1. Record each `ShadowSettings` member with its default, and whether it is currently plumbed all the
   way to a shader constant or stops earlier.
2. Record whether `NormalOffset` is actually applied in the shader today, or only carried in the
   constants. The phase row assumes only a constant bias is in use, so this needs confirming.
3. Record how the shadow map texture is allocated and whether its size and array layer count follow
   the settings dynamically or are fixed at construction.
4. Record what changes when the cascade count moves from one to four: which code paths already
   branch on cascade count and which assume a single cascade.
5. Record where a slope-scaled bias would have to be computed, naming the shader stage that has both
   the surface normal and the light direction available.
6. Fill the audit section in `docs/roadmap/implementation/track-g-g8-shadow-quality.md`,
   following the format of [`track-g-g1-high-dpi.md`](../../implementation/track-g-g1-high-dpi.md).

## Files likely to change

- `docs/roadmap/implementation/track-g-g8-shadow-quality.md`

## Acceptance criteria

- [ ] Every `ShadowSettings` member is recorded with its default and its plumbing endpoint.
- [ ] Whether `NormalOffset` is applied in the shader today is answered definitively.
- [ ] Code paths assuming a single cascade are listed.
- [ ] The shader stage where a slope-scaled bias can be computed is named.
- [ ] No `.cs` or shader file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change any shadow setting or shader. That is `WI-G8-02`.
- Do not evaluate alternative shadow techniques; G8 is a quality tier on the existing cascaded map.
- Do not measure performance.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - None; `WI-G8-02` already exists and depends on these findings.
