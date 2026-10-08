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

## Audit (`WI-G8-01`, complete)

Produced by `WI-G8-01` against `638e40901` (G7-02 tip). Read-only; every claim below was
re-verified against the code at that revision. The deep dive's premise holds: the four-cascade
path and the normal offset already exist, and G8-02 reduces to the `SlopeScaledBias` member, its
tier value, and the receiver-side slope term.

### Settings-to-shader trace

| Member | Default | Plumbing endpoint | Runtime change without restart |
|---|---|---|---|
| `ShadowMapCascades` | `OneCascade` | `ShadowFrustumCalculator.CalculateShadowData` switch (`ShadowFrustumCalculator.cs:44-69`), `ShadowData` layer count, `ShadowConstantsPS.NumSplits` | Yes: `ShadowMapRenderer.RenderShadowMap` reallocation check (`ShadowMapRenderer.cs:48-56`) |
| `ShadowsType` | `Soft` | `ShadowConstantsPS.ShadowsType` -> `Shadows.h` `SampleShadowMapOptimizedPCF` filter-size switch (`Shadows.h:82-91`), frustum AABB kernel growth (`ShadowFrustumCalculator.cs:148`) | Yes: constants buffer rewritten every frame (`ShadowMapRenderer.cs:93-94`) |
| `ShadowMapSize` | `1024` (setter rounds to next power of two, `ShadowSettings.cs:24-36`) | `ShadowData` texture extent, `ShadowConstantsPS` offsets/scales, `GetShadowPosOffset` texel size | Yes: same reallocation check |
| `ShadowCascades2SplitDepth` | `0.333` | `ShadowFrustumCalculator.cs:54` (two-cascade split) | Yes |
| `ShadowCascades4SplitDepths` | `(0.06, 0.20, 0.50)` | `ShadowFrustumCalculator.cs:61-64` | Yes |
| `ShadowDistance` | `1200` | Camera far-plane clamp in `ShadowFrustumCalculator.cs:36-39`, `ShadowConstantsPS.ShadowDistance` -> receiver fade at `Shadows.h:303` | Yes |
| `StabilizeShadowCascades` | `true` | `ShadowFrustumCalculator.cs:109-126, 168-190` (sphere fit, up-vector, texel snapping) | Yes |
| `VisualizeCascades` | `false` | `ShadowConstantsPS.VisualizeCascades` -> `SampleShadowCascade` cascade colours (`Shadows.h:213-224`) | Yes |
| `UpdateShadowMatrices` | `true` | Early-out guard `ShadowFrustumCalculator.cs:28-31` | Yes |
| `VisualizeShadowFrustums` | `false` | `ShadowMapRenderer.DrawDebugOverlay` (`ShadowMapRenderer.cs:105-139`) | Yes |
| `Bias` | `0.002` | `ShadowConstantsPS.Bias` -> `lightDepth -= bias` in `SampleShadowMapOptimizedPCF` (`Shadows.h:61-63`) | Yes |
| `NormalOffset` | `0.4` | `ShadowConstantsPS.OffsetScale` -> `GetShadowPosOffset` (`Shadows.h:235-244`), applied in `ShadowVisibility` (`Shadows.h:269-272`) | Yes |

`ApplyTier` already exists and is wired (`ShadowSettings.cs:75-90`; applied at `Scene3D`
construction `Scene3D.cs:298` and on preset switch `Game.cs:1068`
`ApplyGraphicsSettingsIfChanged`). It owns cascades and map size only; `WI-G8-02` adds
`SlopeScaledBias` to the tier-owned set.

### Answers to the card's questions

- Normal offset is applied today, not just carried. `ShadowVisibility` adds
  `GetShadowPosOffset(nDotL, normal)` (which is `texelSize * OffsetScale * saturate(1 - nDotL) * normal`,
  already slope-scaled in world space) to the sample position before projection
  (`Shadows.h:269-273`). The phase row's "constant bias only" premise is incomplete: both a
  constant depth bias and a normal-based world-space offset are live. G8 adds the slope-scaled
  depth-bias term and leaves the normal offset untouched.
- Allocation is fully dynamic. `ShadowData` is a `Texture2D` array with one `D32_Float_S8_UInt`
  layer and one framebuffer per cascade (`ShadowData.cs:49-65`); `RenderShadowMap` compares live
  size, near/far planes and cascade count against the current instance every frame and
  reallocates on mismatch (`ShadowMapRenderer.cs:48-56`). `GlobalShaderResourceData` rebinding on
  shadow-map instance change is already exercised by this path.
- Cascade-count branching: `ShadowFrustumCalculator` switches over all three cascade values
  (`:44-69`); `ShadowConstantsPS.SetArrayData` guards slots beyond the live count with defaults
  (`GlobalShaderResources.cs:185-196`); the shader picks cascades by looping
  `NumSplits - 1` (`Shadows.h:260-266`) and blends across split edges. No code path hard-assumes
  a single cascade. The only fixed-four assumptions are cosmetic or capacity-only: `NUM_CASCADES
  4` array sizes in `Shadows.h`, the fixed `float[4] _cascadeSplits` scratch
  (`ShadowFrustumCalculator.cs:12`), and the four-entry debug `CascadeColors`
  (`ShadowMapRenderer.cs:97-103`).
- Slope-scaled bias insertion point: the fragment stage, in `ShadowVisibility`
  (`Shadows.h:246`). It already receives `nDotL` and the world normal from every receiver; the
  factor threads through `SampleShadowCascade` into `SampleShadowMapOptimizedPCF`, which already
  computes `shadowMapSizeInv`. `shadowPosDX`/`shadowPosDY` derivatives exist there but stay
  unused; the deep dive deliberately scopes them out.
- `ShadowConstantsPS` is 240 bytes (`StructLayout.Sequential`; `mat4` + splits `vec4` + 4 offsets
  + 4 scales + two floats + two `uint` + `ShadowDistance` + two `int`-sized members + trailing
  `_padding` float). `SlopeScaledBias` replaces `_padding` (`GlobalShaderResources.cs:143-145`,
  `Shadows.h:24`) with no size or offset change.
- Receiver shaders passing `nDotL` + normal into `ShadowVisibility` (verified by grep and read):
  `FixedFunction.frag`, `Terrain.frag`, `Road.frag`, `Water.frag`. `Water.frag` derives its
  normal from the bump texture, so its `nDotL` is not geometric; `MAX_SLOPE_TANGENT` bounds the
  slope term there. `NormalMapped.frag` and `PbrMetallicRoughness.frag` do not sample shadows at
  all (no `Shadows.h` include), so they need no change.
- Rasterizer-side bias is unavailable: the vendored `RasterizerStateDescription`
  (`src/Veldrid/RasterizerStateDescription.cs`) has no `DepthBias` or `SlopeScaleDepthBias`
  fields (verified by grep), and `MeshDepthShaderResources` sets only
  `DepthStencilStateDescription.DepthOnlyLessEqual` (`MeshDepthShaderResources.cs:27`). The slope
  term must be receiver-side, as the deep dive designs.
- Baseline crops from the `Legacy` `shadow-casters` VR capture (1024x768, single 1024 cascade,
  `Bias = 0.002`) were written to
  `https://files.reid-corp.com/viewer/panopticon-g8-legacy-baseline.png` (three 256x256 crops:
  lit ground, shadow edge, contact region). G8-02's tuning compares against this state.

## Tuning record (`WI-G8-02`, to complete)

| Constant | Start | Range | Chosen |
|---|---|---|---|
| `SlopeScaledBias` (High tier) | 2 | 0.5 to 4 | |
| `MAX_SLOPE_TANGENT` | 4 | | |

## Risks and platform notes

- Veldrid 4.9 has no rasterizer depth bias, so the slope term is receiver-side only.
- Four `D32_Float_S8_UInt` layers at 2048x2048 cost up to 128 MiB on drivers storing 8 bytes per
  texel; accepted for `Enhanced`.
- `Legacy` must stay byte-identical: `SlopeScaledBias = 0` must reduce to the existing bias, so no
  tier branch is added in the shader.
- Blended parts are excluded from the shadow bucket. Alpha-tested parts can enter it, but
  `MeshDepth.frag` does not preserve their cutout shape. Caster alpha testing is out of scope.
