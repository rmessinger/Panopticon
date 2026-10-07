# G10 Implementation: Realistic Proportions

> Audience: agent. Card sequence and evidence for [G10](../deep-dives/g10-realistic-proportions.md).

## Scope

Reproducible asset reference data and explicit opt-in render-only aircraft scaling.
Source of truth: [design and diagrams](../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| [WI-G10-01](../agent/cards/WI-G10-01-audit-proportions-and-reference-scale.md) | Reference manifest, deviation table and measurement tests | none |
| [WI-G10-02](../agent/cards/WI-G10-02-presentation-scale-overrides.md) | Per-definition visual transform and separate culling bounds | WI-G10-01, WI-G2-03 |
| [WI-G10-03](../agent/cards/WI-G10-03-preset-wiring-and-golden-frames.md) | Explicit opt-in and proportion golden captures | WI-G10-02, WI-G3-04 |

## Audit evidence to complete

Record model/condition state, excluded FX meshes, coordinate axis, INI scale, asset hashes,
reference lengths/sources, calculation and tolerance. Re-derive rather than copy the track's
approximate scale claims. Inspect every bone consumer before selecting a render-only insertion point.

## Validation and risks

Use installation-resolved assets with patch/loose overrides. Pure arithmetic tests need no install;
asset-data tests require it and fail explicitly on changed assets. Never modify shared model/world
or absolute-bone matrices. Compare gameplay queries and scenario artifacts on/off; suspected
determinism deviations require preservation and user determination. Both presets default the flag off.
