# Track C · C1: Format-agnostic import abstraction

> Audience: human overview above the divider; agent-context detail below it.

Implements the C1 row of [Track C](../track-c-external-assets.md). Scope: define engine-internal
interchange types for meshes, skeletons, animations, and materials, plus an
`IModelImporter` / `IAnimationImporter` abstraction behind the existing
`OnDemandAssetLoadStrategy`, and refactor the W3D path to be one implementation of that
abstraction with no behavior change. No new external formats (glTF is C4). The seam is registered
in [seams.md](../seams.md) as the model/animation import seam.

## Audit-to-design sequence

| Step | Card | Deliverable |
|---|---|---|
| 1 | [`WI-C1-01`](../agent/cards/WI-C1-01-audit-model-import-path.md) | Current model import path audit (below). |
| 2 | [`WI-C1-02`](../agent/cards/WI-C1-02-audit-animation-import-path.md) | Current animation import path audit (below). |
| 3 | `WI-C1-03` | C1 deep dive, contract design, and grounded architecture diagrams from both audits. |
| 4 | later cards | Interchange types, importer interfaces, W3D refactored behind the seam, parity tests. |

Steps 1 and 2 are read-only; they ground the C1 architecture diagrams required before the first
source-edit card is marked `Ready`.

---

## Current model import path

Audited by `WI-C1-01`. Entry point: `AssetStore.Models.GetByName(name)` on a cache miss. All
citations are to the current tree; no source was modified.

### Stage trace

| Stage | Input → Output | Owning method | Citation | Format-specific | GPU alloc | Failure behavior |
|---|---|---|---|---|---|---|
| Cache lookup / on-demand dispatch | `string` → `Model` | `ScopedAssetCollection<T>.GetByName` | `src/OpenSage.Game/Content/ScopedAssetCollection.cs:61-103` | no (loader injected) | indirect | returns null if no loader or load returns null; miss is not negatively cached, so a failed name retries every call |
| Loader construction | none → `OnDemandModelLoader` | `OnDemandAssetLoadStrategy.CreateModelLoader` | `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs:32-37` | yes (w3d path resolver) | no | `None` strategy returns null loader, so data-only loads return null models |
| Path resolver selection | per game → `IPathResolver` | game `Definition` (e.g. `GeneralsDefinition.CreateAssetLoadStrategy`) | `src/OpenSage.Mods.Generals/GeneralsDefinition.cs:46`, `src/OpenSage.Game/Content/IPathResolver.cs:8-28` | yes (emits `art/w3d/<name>.w3d`; BFME2 variant adds a 2-letter subfolder) | no | not applicable |
| File resolution | `string` → `FileSystemEntry` | `OnDemandModelLoader.Load` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:25-32` | yes (resolver paths) | no | no entry → returns null silently |
| W3D parse | stream → `W3dFile` | `W3dFile.FromStream` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:40-44` | yes | no | parse exception propagates |
| Hierarchy selection: embedded | `W3dFile.Hierarchy` → `ModelBoneHierarchy` | `ModelBoneHierarchy(W3dHierarchyDef)` ctor | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:49-52`, `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs:23-44` | yes | no | pivot `ParentIdx` other than -1 or a valid prior index would throw or mislink |
| Hierarchy selection: referenced via HLod | `HLod.Header.HierarchyName` → `ModelBoneHierarchy` | recursive `AssetStore.ModelBoneHierarchies.GetByName` → `OnDemandModelBoneHierarchyLoader.Load` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:53-57`, `src/OpenSage.Game/Content/Loaders/OnDemandModelBoneHierarchyLoader.cs:16-39` | yes | no | hierarchy file missing → null-reference at `entry.Open()` (`OnDemandModelBoneHierarchyLoader.cs:31` has no null check); file present without a hierarchy chunk → `CreateDefault()` |
| Hierarchy selection: default | none → single root bone | `ModelBoneHierarchy.CreateDefault` | `src/OpenSage.Game/Graphics/ModelBoneHierarchy.cs:8-13`, `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:58-61` | convention only | no | not applicable |
| Sub-object dispatch: HLod | `HLod.Lods[0].SubObjects` → `ModelSubObject` list | `CreateModel` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:80-104` | yes | indirect | sub-object name absent from `RenderableObjectsByName` → silently skipped (`:84-87`); `BoneIndex` used unchecked into `Bones` (`:89`) |
| Sub-object dispatch: single-renderable fallback | first renderable → one `ModelSubObject` | `CreateModel` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:105-121` | yes | indirect | renderable count greater than 1 without HLod → `InvalidOperationException` (`:108-110`); bound to `Bones[0]` |
| Sub-object dispatch: empty | none → zero sub-objects | `CreateModel` | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:122-125` | yes | no | file with no renderable objects still yields a `Model` with an empty sub-object array, no diagnostic |
| Render-object conversion: `W3dMesh` | `W3dMesh` → `ModelMesh` | `ModelMesh(W3dMesh, AssetLoadContext)` ctor | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:141`, `src/OpenSage.Game/Graphics/ModelMesh.W3d.cs:19-99` | yes | yes | see responsibility table |
| Render-object conversion: `W3dBox` | `W3dBox` → `ModelBox` | `ModelBox(W3dBox, AssetLoadContext)` ctor | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:143`, `src/OpenSage.Game/Graphics/ModelBox.cs:21-28` | yes | no | bounds-only stub; rendering methods are TODO |
| Render-object conversion: other chunk | any other `W3dChunk` → n/a | `CreateSubObject` switch | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:139-146` | yes | no | `ArgumentOutOfRangeException` |
| Model construction | name + hierarchy + sub-objects → `Model` | `Model` ctor | `src/OpenSage.Game/Content/Loaders/OnDemandModelLoader.cs:127-130`, `src/OpenSage.Game/Graphics/Model.cs:14-38` | naming only | no | `HasSkinnedMeshes` scans sub-object meshes; instance type tag is `W3DContainer` |

Sub-object naming: `ModelSubObject` splits `FullName` at the first dot into `FullName` and `Name`
(`src/OpenSage.Game/Graphics/ModelSubObject.cs:23-27`), matching the W3D `container.mesh`
convention.

### ModelMesh.W3d.cs conversion responsibilities

| Responsibility | Owning method | Runtime output | GPU boundary |
|---|---|---|---|
| Material path selection (shader material vs fixed function) | ctor | `w3dShaderMaterial` null flag + `ShaderSet` choice | no |
| Shader-material path | ctor `:23-41`, `CreateModelMeshPartShaderMaterial` `:208-243` | one `ModelMeshPart`, UV0 texcoords | texcoords `DeviceBuffer` (`:227-229`); cached `Material` via `GetCachedMaterial` |
| Fixed-function vertex materials | `CreateMaterials` `:114-127` | `FixedFunctionShaderResources.VertexMaterial[]` | uploaded later inside cached material constants |
| Fixed-function shading config | `CreateShadingConfiguration` `:101-112` | `ShadingConfiguration` per `W3dShader` | no |
| Mesh-part splitting (per-triangle id expansion, run-length grouping) | `CreateModelMeshPartsFixedFunction` `:246-448` | `ModelMeshPart` list with `StartIndex`/`IndexCount` | no |
| Render state (cull, depth, blend) | `CreateModelMeshPart` `:504-554` | cached `Material` + `MaterialBlend` + shadow-depth `MaterialPass` | pipeline state objects created/cached by `ShaderResourceManager` |
| Texture lookup | `CreateTexture` `:129-147` | `Texture` via `AssetStore.Textures.GetByName` | nested on-demand GPU texture load |
| Geometry: positions/normals/tangents | `CreateVertices` `:149-190` | `MeshShaderResources.MeshVertex.Basic[]` | vertex `DeviceBuffer` at ctor `:87-89` |
| Skinning: influences | `CreateVertices` `:174-185` | `BoneIndex0/1`, `BoneWeight0/1` in the same vertex layout | same vertex buffer |
| Indices | `CreateIndices` `:192-206` | `ushort[]` | index `DeviceBuffer` at ctor `:91-93`; cast to `ushort` wraps silently above 65535 |
| Bounds | ctor `:75-81` | `_boundingBox` from header Min/Max, `_boundingSphere` from SphCenter/SphRadius | no |
| Flags | ctor `:83-85` | `Skinned`, `Hidden` (`W3dMeshFlags.Hidden`), `CameraOriented` (geometry-type mask) | no |
| House-color constants | ctor `:95-98` | shared cached `MeshConstantsBuffer` keyed on (`Skinned`, name starts with `HOUSECOLOR`) | shared `DeviceBuffer`, cached across meshes |

Runtime fields consumed by rendering (`src/OpenSage.Game/Graphics/ModelMesh.cs:17-55`):
`VertexBuffer`, `_indexBuffer` (both internal), `MeshConstantsBuffer`, `BoundingBox`,
`BoundingSphere`, `MeshParts`, `Skinned`, `Hidden`, `CameraOriented`. All are format-neutral
types; every W3D dependency is confined to construction.

CPU to GPU boundary: all GPU allocation happens inside `ModelMesh` construction (vertex, index,
texcoords buffers; cached materials/pipelines; nested textures). `Model`, `ModelBoneHierarchy`,
`ModelSubObject`, and `ModelBox` are CPU-only. The data-only strategy (`OnDemandAssetLoadStrategy.None`)
avoids all of it by supplying a null loader, not by a separate CPU path.

### W3D-specific assumptions crossing into runtime construction

- `Model` is named from `Path.GetFileNameWithoutExtension(w3dFile.FilePath)` and tagged
  `W3DContainer` (`Model.cs:20`, `OnDemandModelLoader.cs:128`).
- `ModelBoneHierarchy(W3dHierarchyDef)` ctor and `ModelMesh(W3dMesh, AssetLoadContext)` ctor take
  W3D chunk types directly; the tags are `W3DHierarchy` / `W3DMesh`.
- Bone linkage is by array index: HLod sub-object `BoneIndex` into `Bones`, pivot `ParentIdx` into
  prior pivots; no name-based lookup anywhere in the model path.
- Skinning is two-bone (`BoneIndex0/1`, `BoneWeight0/1`) with integer weights 0-100 normalized by
  `/100.0f`; the limit is baked into the vertex layout.
- Mesh naming conventions: sub-object `container.mesh` dot split; `HOUSECOLOR` prefix selects the
  mesh constants buffer.
- Shader selection strips `.fx` from `W3dShaderMaterial.Header.TypeName` to find the engine
  `ShaderSet` (`ModelMesh.W3d.cs:33-35`).
- Per-face material addressing: `VertexMaterialIds`, `ShaderIds`, and per-stage `TextureIds` arrays
  indexed per triangle; mixed materials on one triangle throw `NotSupportedException`
  (`ModelMesh.W3d.cs:381-384`); multi-frame textures throw `NotImplementedException` (`:141-144`).
- Double geometry: optional `Vertices2` / `Normals2` arrays fill `Position1` / `Normal1`, else zero.
- Index type is `ushort`.
- `W3dMeshFlags` (Hidden, TwoSided, GeometryTypeCameraOriented) map directly onto runtime flags.

### Already format-neutral runtime consumers

- `Model` public shape: `BoneHierarchy`, `SubObjects[]`, `HasSkinnedMeshes`, `CreateInstance`
  (`Model.cs`). No W3D types.
- `ModelBoneHierarchy` public shape: `Bones[]` of `ModelBone` (index, name, parent, translation,
  rotation); only the converting ctor is W3D-specific.
- `ModelSubObject` (`FullName`, `Name`, `Bone`, `ModelRenderObject`) and the `ModelRenderObject`
  abstract surface (`BoundingBox`, `BoundingSphere`, `Hidden`, `BuildRenderList`).
- `ModelMesh` runtime fields listed above; `BuildRenderList` consumes only bone transforms,
  visibilities, and mesh parts.
- The collection/cache machinery: `ScopedAssetCollection<T>`, `AssetStore` collections, lazy
  references. `IPathResolver` is a neutral interface; its shipped implementations are W3D-path
  specific.

### Ambiguous findings (3)

1. `ScopedAssetCollection.GetByName` has an unreachable second null check, so the
   "Failed to load asset" warning never fires and failure semantics (silent null) are only
   implicit (`ScopedAssetCollection.cs:79-90`). `ambiguous`: intended failure contract undocumented.
2. Referenced-hierarchy load has no missing-file guard: `OnDemandModelBoneHierarchyLoader.Load`
   dereferences a null `entry` (`OnDemandModelBoneHierarchyLoader.cs:31`). `ambiguous`: crash vs
   default hierarchy intent not determinable from code.
3. The empty-renderables branch builds a `Model` with zero sub-objects and no log
   (`OnDemandModelLoader.cs:122-125`). `ambiguous`: placeholder vs intended behavior.

## Current animation import path

Audited by `WI-C1-02`. Entry point: an INI `AnimationName` inside an `AnimationStateAnimation`
block. End point: per-bone `AnimatedOffset` transforms and visibility flags on
`ModelBoneInstance`. All citations are to the current tree; no source was modified.

### Stage trace

| Stage | Input → Output | Owning method | Citation | Format-specific | Failure behavior |
|---|---|---|---|---|---|
| INI animation reference | `AnimationName` token → `LazyAssetReference<W3DAnimation>` or null | `AnimationStateAnimation.ParseAnimationName` → `IniParser.ScanAnimationReference` | `src/OpenSage.Game/Logic/Object/AnimationState.cs:129-136`, `src/OpenSage.Game/Data/Ini/IniParser.cs:716-727` | no (plain string) | token `NONE` → null reference; otherwise lazy, failure deferred to lookup |
| Lazy on-demand lookup | key → `W3DAnimation` | `LazyAssetReference.Value` → `ScopedAssetCollection<W3DAnimation>.GetByName` | `src/OpenSage.Game/Content/AssetStore.cs:312`, `src/OpenSage.Game/Content/ScopedAssetCollection.cs:61-103` | no (injected loader) | miss not negatively cached; failed names retry every access |
| Key split and path resolution | `hierarchy.animation` key → `FileSystemEntry` | `OnDemandAnimationLoader.Load` | `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs:20-42` | yes (splits at first dot; resolves `splitName[1]` via the W3D path resolver) | key without a dot → null, no log; missing file → warn log + null |
| W3D parse and chunk extraction | stream → `W3DAnimation` | `W3dFile.FromStream`, `W3DAnimation.FromW3dFile` | `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs:44-50`, `src/OpenSage.Game/Graphics/Animation/W3DAnimation.cs:8-36` | yes | zero animation chunks → null with warn; more than one animation chunk → `NotSupportedException` |
| Name validation | asset name vs key | `OnDemandAnimationLoader.Load` | `src/OpenSage.Game/Content/Loaders/OnDemandAnimationLoader.cs:58-61` | yes (name is `HierarchyName + "." + Name`) | mismatch → warn only, asset still returned |
| Uncompressed conversion | `W3dAnimationChannel` / `W3dBitChannel` → `AnimationClip` | `W3DAnimation(W3dAnimation)` ctors | `src/OpenSage.Game/Graphics/Animation/W3DAnimation.W3dAnimation.cs:10-124` | yes | `UnknownBfme` channel type dropped; unsupported datum types and non-visibility bit channels → `NotImplementedException` |
| Compressed conversion: time-coded | `W3dTimeCodedAnimationChannel` → `AnimationClip` | `W3DAnimation(W3dCompressedAnimation)` | `src/OpenSage.Game/Graphics/Animation/W3DAnimation.W3dCompressedAnimation.cs:48-62` | yes | times from per-datum `TimeCode / FrameRate`; unsupported channel type via `ToAnimationClipType` → `ArgumentOutOfRangeException` |
| Compressed conversion: adaptive delta | `W3dAdaptiveDeltaAnimationChannel` → `AnimationClip` | same file | `src/OpenSage.Game/Graphics/Animation/W3DAnimation.W3dCompressedAnimation.cs:64-82` | yes | decoded with `W3dAdaptiveDeltaCodec.Decode`; per-frame times `i / FrameRate` |
| Compressed conversion: motion | `W3dMotionChannel` → `AnimationClip` | same file | `src/OpenSage.Game/Graphics/Animation/W3DAnimation.W3dCompressedAnimation.cs:84-97` | yes | keyframes from `GetKeyframesWithValues`; both data variants (time-coded, adaptive-delta) yield exactly `NumTimeCodes` entries |
| Gameplay animation-state selection | state → `AnimationInstance` | `W3dModelDraw.SetActiveAnimationState` | `src/OpenSage.Game/Logic/Object/Draw/W3dModelDraw.cs:210-240` | no | stops and clears prior instances; picks a random animation block; unresolved/null animation → skipped silently; mode, flags, and speed-factor range come from the INI block |
| Runtime playback | `AnimationInstance` → bone offsets | `AnimationInstance.Update` / `UpdateBoneTransforms` / `Evaluate` | `src/OpenSage.Game/Graphics/Animation/AnimationInstance.cs:134-333` | no | clips with `Bone >= _boneInstances.Length` skipped; translation lerps per axis, rotation slerps, visibility takes the previous keyframe's bool |
| Hierarchy application and skinning | bone offsets → world transforms + GPU skinning buffer | `ModelInstance.Update` | `src/OpenSage.Game/Graphics/ModelInstance.cs:167-234` | no | recomputes relative transforms from dirty `ModelBoneInstance.Matrix`; refreshes `BoneFrameVisibilities` unconditionally; skinned models upload `RelativeBoneTransforms` to the skinning `DeviceBuffer` |

`ModelInstance` construction allocates `ModelBoneInstance[]` in hierarchy `Bones` order and seeds
the bind pose before first render (`ModelInstance.cs:81-116`); `AnimationInstance` writes into that
same array, so clip bone indices, hierarchy bone indices, and instance bone indices are one shared
index space (`ModelInstance.cs:118-132` for the skinning buffer allocation).

### Compatibility contracts

- Identity: the INI key is `hierarchy.animation`. The loader splits at the first dot and resolves
  the remainder as a `.w3d` file name; the produced asset is named
  `HierarchyName + "." + Name` and compared to the INI key case-insensitively, warning only on
  mismatch (`W3DAnimation.W3dAnimation.cs:12`, `OnDemandAnimationLoader.cs:58-61`).
- Bone addressing: `AnimationClip.Bone` is the channel's `Pivot` value, an index into
  `ModelBoneHierarchy.Bones` (`AnimationClip.cs:6`, `W3dAnimationChannel.cs:18`). No name-based
  bone matching exists anywhere in the path; playback tolerates out-of-range indices by skipping.
- Clip types: `TranslationX`, `TranslationY`, `TranslationZ`, `Quaternion`, `Visibility`
  (`AnimationClipType.cs`). `KeyframeValue` is a union consumed according to clip type.
- Timing: frame `n` maps to `n / FrameRate` seconds; `Duration = NumFrames / FrameRate`
  (`W3DAnimation.W3dAnimation.cs:13`). Uncompressed channels place keyframes at
  `FirstFrame + i`; bit channels pad with `DefaultValue` keyframes at time zero and
  `LastFrame + 1` when the channel does not span the whole animation
  (`W3DAnimation.W3dAnimation.cs:84-121`); adaptive-delta assumes one key per frame.
- Modes and flags: `AnimationMode` (Once, Loop, OnceBackwards, LoopBackwards, Manual) and
  `AnimationFlags` (RandomStart uses the game's `IRandom`, StartFrameFirst/Last,
  AdjustHeightByConstructionPercent reads `GameObject.BuildProgress`); ping-pong is TODO and
  unhandled start-flag combinations fall through to time zero silently
  (`AnimationInstance.cs:87-111`).
- Visibility: three inputs AND-combine per frame into `BoneFrameVisibilities`: INI-driven
  `BoneVisibilities` (sub-object show/hide, `W3dModelDraw.cs:360-370`), parent-chain visibility,
  and animation-driven `ModelBoneInstance.Visible` (`ModelInstance.cs:203-216`).
- `ModelBoneInstance` count and order: fixed at `ModelInstance` construction from the hierarchy;
  animations must address that index space, which is the contract C2 retargeting consumes.

### Import-time vs runtime responsibilities

Import-time (W3D-specific today): key splitting, path resolution, `W3dFile` parsing, animation
chunk dispatch, channel decode (including the adaptive-delta codec), keyframe conversion, name
validation.

Runtime (importer-neutral today): the `AnimationClip` / `Keyframe` / `AnimationClipType` shape,
`AnimationInstance` playback (modes, flags, interpolation), `ModelInstance` bone transforms,
visibility, and skinning upload, and the gameplay selection path in `W3dModelDraw`, which works
against `LazyAssetReference<W3DAnimation>` and INI block fields only.

Format assumptions exposed by names: `W3DAnimation` (the asset type and the
`AssetStore.ModelAnimations` collection type), `OnDemandAnimationLoader`, and the
`LazyAssetReference<W3DAnimation>` fields in `AnimationStateAnimation`. Neutral without rename:
`AnimationClip`, `Keyframe`, `AnimationClipType`, `AnimationInstance`, `ModelBoneInstance`,
`ModelInstance`.

### Ambiguous findings (4)

1. A key with no dot returns null without any log, while a missing file logs a warning; the
   intended INI-level failure contract is unclear (`OnDemandAnimationLoader.cs:22-25`).
2. A file containing more than one animation chunk throws `NotSupportedException`; the
   single-animation-per-file assumption is implicit (`W3DAnimation.cs:21-24`).
3. Playback silently skips clips whose bone index exceeds the instance bone count; whether a
   hierarchy mismatch should be an error is not determinable from code
   (`AnimationInstance.cs:228-231`).
4. `ResetTimeStamps` ignores unimplemented start-flag combinations instead of throwing; which
   flags occur in real data is not determinable from the listed context
   (`AnimationInstance.cs:106-110`).

## Evidence required for C1 design

Checklist of decisions the C1 deep dive (`WI-C1-03`) must make from the audits above. Not resolved
here.

- [ ] Interchange ownership: which types own mesh/skeleton/animation/material data between import
      and runtime, and where the W3D ctors on `ModelBoneHierarchy`, `ModelMesh`, and `W3DAnimation`
      move.
- [ ] Importer selection: how `IModelImporter` / `IAnimationImporter` implementations are chosen
      and wired behind `OnDemandAssetLoadStrategy`, including path-resolver ownership.
- [ ] Model/animation contract split: what `Model`, `AnimationClip`, and the bone index space
      guarantee to importers, and what stays W3D-implementation detail (the compatibility contracts
      above are the input).
- [ ] CPU-to-GPU boundary: whether GPU allocation stays inside runtime construction
      (`ModelMesh` ctor, `ModelInstance` skinning buffer) or moves behind the seam.
- [ ] Error contract: null-return vs log vs throw per failure stage; today's behavior is
      inconsistent across the loaders (see the ambiguous findings in both audits).
- [ ] W3D parity tests: how the W3D refactor behind the seam proves no behavior change.
