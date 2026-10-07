# G7 Implementation: Texture Filtering

> Audience: agent. Card sequence and evidence for [G7](../deep-dives/g7-texture-filtering.md).

## Scope

Configurable world sampler and explicit terrain mip policy, not global sampler replacement.
Source of truth: [design and diagrams](../deep-dives/g7-texture-filtering.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G7-01 | Sampler consumer/cache table and terrain mip inventory | none |
| WI-G7-02 | Effective anisotropy/bias, invalidation and terrain builder | WI-G7-01, WI-G2-03, WI-G3-03 |

## Audit evidence to complete

Record maximum supported anisotropy, every captured sampler binding, terrain source dimensions,
layer ordering and actual resampling kernels. Select Enhanced's kernel against static and
camera-motion evidence; do not assume a sharper crop proves lower shimmer.

## Validation and risks

Default bias remains zero. Pure tests cover bounded finite bias and fallback. Rebind global and
radius-cursor resources on switching. Faithful mips retain existing bytes. G6's linear filtering
policy composes when active; G7 may land independently. Record aligned temporal-region thresholds.
