# Task Card `WI-C2-04`: Cache bound animations

| Field | Value |
|---|---|
| Card ID | `WI-C2-04` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Blocked` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-C2-03` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Cache one immutable binding per hierarchy object and report out-of-range direct targets once at
binding time.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 4 and 5.
- Runtime animation binding types from `WI-C2-02..03`.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`.
- Existing `NLog` warning patterns in `src/OpenSage.Game/Content/Loaders/`.
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`.

## Steps

1. Cache bound results in
   `ConditionalWeakTable<ModelBoneHierarchy, BoundModelAnimation>`.
2. Key strictly by hierarchy object identity, not name, bone count, or content hash.
3. Omit out-of-range direct-index channels while preserving the order of retained channels.
4. Emit one warning per omitted channel during the first bind for that hierarchy, including
   animation and hierarchy context.
5. Add tests for same-hierarchy reuse, distinct-hierarchy results, direct-index omission, and
   warning count. Verify by inspection that the cache field is a `ConditionalWeakTable`, not a
   strong collection, and that `BoundModelAnimation` holds no hierarchy or bone reference.

## Files likely to change

- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`

## Acceptance criteria

- [ ] Repeated binding to the same hierarchy returns the same immutable result.
- [ ] A different hierarchy object receives a distinct result even when its name matches.
- [ ] Out-of-range direct targets are omitted and warned once instead of failing.
- [ ] Cache storage uses `ConditionalWeakTable` and no parallel strong hierarchy collection.
- [ ] Cached values contain only animation name, duration, and clips, with no reference to the
      hierarchy key or its bones.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelAnimationTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not cache by hierarchy name or structural hash.
- Do not suppress named-target failures.
- Do not add global or asset-store cache state.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C2-05` to `Ready`.
- Append a result line to `.exchange`.
