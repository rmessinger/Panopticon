# Task Card `WI-C2-03`: Bind named animation targets

| Field | Value |
|---|---|
| Card ID | `WI-C2-03` |
| Roadmap phase | [Track C · C2](../../track-c-external-assets.md): Skeleton and animation retargeting |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C2-02` |
| Diagrams | [C2 architecture diagrams](../../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams) |

## Goal

Resolve named animation targets against a runtime hierarchy with exact case-insensitive matching
and contextual failures.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c2-skeleton-animation-retargeting.md` sections 3-5.
- Runtime animation target and binding types from `WI-C2-02`.
- `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs`.
- `src/OpenSage.Game/Graphics/ModelBone.cs`.
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`.

## Steps

1. Build a non-empty target-bone name index with `StringComparer.OrdinalIgnoreCase` only when a
   bound animation contains named targets.
2. Reject duplicate target names under that comparer before resolving channels.
3. Resolve every named channel to exactly one runtime index while retaining channel order.
4. Throw `InvalidDataException` for missing or ambiguous names and include animation, source bone,
   and target hierarchy context.
5. Add tests for reordered hierarchies, case differences, missing names, duplicate names, and
   unnamed unrelated target bones.

## Files likely to change

- `src/OpenSage.Game/Graphics/Animation/ModelAnimation.cs`
- `src/OpenSage.Game.Tests/Graphics/Animation/ModelAnimationTests.cs`

## Acceptance criteria

- [ ] Differently ordered hierarchies bind channels to the intended bone indices.
- [ ] Matching is exact apart from case.
- [ ] Missing and duplicate named targets fail before playback with asset context.
- [ ] Bones without channels require no mapping.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ModelAnimationTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not normalize names or add alias profiles.
- Do not insert identity channels or bind a missing name to index zero.
- Do not perform per-frame lookup or bind-pose adjustment.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done, change `WI-C2-04` to `Ready`.
- Append a result line to `.exchange`.
