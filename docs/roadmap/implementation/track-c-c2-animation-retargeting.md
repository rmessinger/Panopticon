# Track C · C2: Skeleton and animation retargeting

> Audience: human overview above the divider; agent-context detail below it.

Implements the C2 row of [Track C](../track-c-external-assets.md). Design source of truth:
[deep-dives/c2-skeleton-animation-retargeting.md](../deep-dives/c2-skeleton-animation-retargeting.md),
including its
[architecture diagrams](../deep-dives/c2-skeleton-animation-retargeting.md#8-architecture-diagrams).

## Scope

Represent direct-index and exact-name animation targets, bind them once to a runtime bone
hierarchy, cache immutable bound results by hierarchy identity, and route existing gameplay
animation activation through that boundary. Existing W3D direct-index playback remains unchanged.

## Card sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C2-01`](https://projects.reid-corp.com/work_packages/450) | Imported direct-index and bone-name target contract. |
| 2 | [`WI-C2-02`](https://projects.reid-corp.com/work_packages/451) | Runtime target/channel types and direct-index binding. |
| 3 | [`WI-C2-03`](https://projects.reid-corp.com/work_packages/452) | Exact-name binding and contextual failures. |
| 4 | [`WI-C2-04`](https://projects.reid-corp.com/work_packages/453) | Weak hierarchy-identity cache and one-time direct-index diagnostics. |
| 5 | [`WI-C2-05`](https://projects.reid-corp.com/work_packages/454) | Model-instance and gameplay activation integration. |
| 6 | [`WI-C2-06`](https://projects.reid-corp.com/work_packages/455) | C2 acceptance and W3D parity tests. |

The sequence starts after C1 completes. Card 6 is the completion point.

---

## Agent context

- Imported channel ownership:
  `src/Panopticon.Simulation/Content/Importing/ImportedAnimationChannel.cs`.
- Runtime animation ownership:
  `src/Panopticon.Simulation/Graphics/Animation/ModelAnimation.cs`.
- Binding output: immutable `BoundModelAnimation` containing the animation name, duration, and
  existing target-indexed `AnimationClip` values.
- Cache key: `ModelBoneHierarchy` object identity through
  `ConditionalWeakTable<ModelBoneHierarchy, BoundModelAnimation>`.
- Activation boundary: `ModelInstance.CreateAnimationInstance` binds to
  `Model.BoneHierarchy`; `W3dModelDraw` no longer constructs `AnimationInstance` directly.
- W3D compatibility: integer channel construction remains the direct-index path. Out-of-range
  direct targets are omitted with one warning during binding.
- Named matching: exact `OrdinalIgnoreCase`; missing or duplicate target names are invalid data.
- Runtime boundary: `AnimationInstance` receives only bound clips and performs no target lookup.
