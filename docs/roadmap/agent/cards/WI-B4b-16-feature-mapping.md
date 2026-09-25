# Task Card `WI-B4b-16`: Declarative feature mapping

| Field | Value |
|---|---|
| Card ID | `WI-B4b-16` |
| Roadmap phase | [Track B · B4b](../../track-b-scale.md): B4b-6 vector semantics |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-B4b-12` and `WI-B4b-15` |
| Diagrams | [B4b §10.3](../../deep-dives/b4b-geospatial-map-generation.md#103-standalone-pipeline-and-dev-preview-types) |

## Goal

Apply manifest `FeatureMappingRule`s to clipped features, producing generated waypoints, object candidates, and region annotations.

## Context to read (and only this)

- `docs/roadmap/deep-dives/b4b-geospatial-map-generation.md` §5.3 (mapping table).

## Steps

1. Add `GeneratedFeature` (kind: Waypoint, Landmark, ObjectCandidate, Road, Rail, River, Water, Passability, Buildability, Annotation; game geometry; parameters; source feature id).
2. Add `FeatureMapper.Map(localFeatures, rules, terrain)`: first matching rule by layer and attribute predicates; object candidates require an object-definition name and orientation policy; unmatched features become Annotations.
3. Sample terrain Z for points from terrain chunks with bilinear interpolation.
4. Add `test/Panopticon.Geospatial.Tests/Features/FeatureMapperTests.cs`.

## Files likely to change

- `src/Panopticon.Geospatial/Features/FeatureMapper.cs`
- `src/Panopticon.Geospatial/Features/GeneratedFeature.cs`
- `test/Panopticon.Geospatial.Tests/Features/`

## Acceptance criteria

- [ ] A point mapped to Waypoint gets Z equal to the terrain height at that position.
- [ ] An ObjectCandidate rule without an object name fails with a `Generate` error.
- [ ] Unmatched features remain Annotations and generate no gameplay kinds.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~FeatureMapper"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No object placement into a map.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-B4b-17` and `WI-B4b-18` when their other prerequisites are `Done`.
