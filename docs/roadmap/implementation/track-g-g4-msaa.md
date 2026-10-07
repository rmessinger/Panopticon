# G4 Implementation: MSAA

> Audience: agent. Card sequence and evidence for [G4](../deep-dives/g4-msaa.md).

## Scope

Main 3D sample count and resolve; 2D draws on the single-sample resolved target.
Source of truth: [design and diagrams](../deep-dives/g4-msaa.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G4-01 | Pipeline/attachment/sample-count inventory | none |
| WI-G4-02 | Effective descriptions, targets, resolve and captures | WI-G4-01, WI-G2-03, WI-G3-03 |

## Audit evidence to complete

Record each main/auxiliary output, pipeline cache key, colour/depth count and sampler consumer.
Include model, terrain, road, water, particle, tracer, sprite and fade paths. Auxiliary outputs
must be classified separately rather than inheriting main MSAA unconditionally.

## Validation and risks

Capability tests; supported 1/2/4/8 captures; repeated switches/resizes. Resolve precedes 2D and
the final sampled blit. Faithful 1x is exact; 4x reduces aliasing in G3 silhouette regions.
Target reuse checks count and format as well as dimensions. G5 owns cutout coverage state.
