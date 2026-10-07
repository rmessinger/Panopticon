# G8 Implementation: Shadow Quality Tier

> Implementation plan for [Track G · G8](../track-g-rendering-clarity.md). Source of truth:
> [deep-dives/g8-shadow-quality.md](../deep-dives/g8-shadow-quality.md),
> [Architecture diagrams §5](../deep-dives/g8-shadow-quality.md#5-architecture-diagrams).
>
> Audience: agent. Card sequence, audit deliverables, and tuning record for the shadow tier.

## Scope

Tier selection over the existing cascaded shadow map (four cascades at 2048x2048 under
`Enhanced`) plus a receiver-side slope-scaled bias term. The four-cascade path and the normal
offset already exist; G8 adds no cascade code and does not change the normal offset.

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| [`WI-G8-01`](../agent/cards/WI-G8-01-audit-shadow-settings-flow.md) | Settings-to-shader trace, four-cascade path confirmation, bias insertion point | none |
| [`WI-G8-02`](../agent/cards/WI-G8-02-four-cascade-tier-and-slope-scaled-bias.md) | `SlopeScaledBias`, `ApplyTier`, slope term in `Shadows.h`, captures under both presets | `WI-G8-01`, `WI-G2-03`, `WI-G3-04` |

## Audit deliverables (`WI-G8-01`, to complete)

- One row per `ShadowSettings` member: default, consumer (`ShadowFrustumCalculator`,
  `ShadowMapRenderer`, `ShadowConstantsPS.Set`, shader), and whether a runtime change takes effect
  without restart.
- Confirmation that `ShadowConstantsPS` is 240 bytes with the trailing padding float free.
- Confirmation that every receiver shader (`FixedFunction`, `Terrain`, `Road`, `Water`) passes
  `nDotL` and a normal to `ShadowVisibility`, noting `Water.frag`'s bump-derived normal.
- Baseline crops from a `Faithful` capture showing acne, cascade blockiness, and contact shadows.

## Tuning record (`WI-G8-02`, to complete)

| Constant | Start | Range | Chosen |
|---|---|---|---|
| `SlopeScaledBias` (High tier) | 2 | 0.5 to 4 | |
| `MAX_SLOPE_TANGENT` | 4 | | |

## Risks and platform notes

- Veldrid 4.9 has no rasterizer depth bias, so the slope term is receiver-side only.
- Four `D32_Float_S8_UInt` layers at 2048x2048 cost up to 128 MiB on drivers storing 8 bytes per
  texel; accepted for `Enhanced`.
- `Faithful` must stay byte-identical: `SlopeScaledBias = 0` must reduce to the existing bias, so no
  tier branch is added in the shader.
- Blended parts are excluded from the shadow bucket. Alpha-tested parts can enter it, but
  `MeshDepth.frag` does not preserve their cutout shape. Caster alpha testing is out of scope.
