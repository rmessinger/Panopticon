# G2 Implementation: Graphics Settings

> Audience: agent. Card sequence and implementation evidence for [G2](../deep-dives/g2-graphics-settings-presets.md).

## Scope

Settings ownership and frame-boundary application. Later phases own their visual algorithms.
Source of truth: [design and diagrams](../deep-dives/g2-graphics-settings-presets.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G2-01 | Knob/default/owner/rebuild/capability table | none |
| WI-G2-02 | Immutable settings, presets and game ownership | WI-G2-01 |
| WI-G2-03 | Coherent application, resource invalidation and capture proof | WI-G2-02 |

## Audit evidence to complete

Record each knob's current value, construction site, consumers, cache key and invalidation
strategy here before G2-2. Establish a safe initial runtime-switch demonstration using the
existing cascade path; do not enable an unimplemented phase's setting.

## Validation and risks

Pure default/capability tests; repeat preset switches and resizes; post-G1 Faithful capture.
Resource sets retain sampler/texture references. Partial rebuilds are not safe. Requested and
effective settings must both appear in diagnostics. No simulation or experiment-seam changes.
