# Open Questions

> Part of the [Large-Scale Simulation Roadmap](README.md). This document is the decision log.
> Each entry is a settled Decision with rationale and links back to the affected phase. No
> questions remain open; new items join here and become Decisions once settled.
>
> Audience: human reference for roadmap decisions.

- C10d decompressor provenance. Decision (2026-10-30): implement managed Oodle1 and BitKnit2
  using the pinned permissive specifications and cross-check implementations recorded in
  [the provenance review](deep-dives/c10-aoe3de-granny-models.md#6-references-and-licensing).
  Exclude the proprietary Granny DLL and unlicensed sources. Retain reference attribution
  and any required notices; validate against independent decoders and the installed corpus.

- Internal length unit. Decision (2026-10-06): the engine's internal length unit becomes the meter in [H2b](deep-dives/h2b-metric-core.md), after the complete H2a group merges. Source formats keep their units; conversion happens once at each source-to-engine boundary and is inverted on legacy writes. Supersedes the presentation-only [metric-units plan](../plans/metric-units.md), which kept feet internally. Mixed EE2, glTF, and Generals content makes a single physical unit the cleaner runtime contract than per-importer meters-to-feet expansion.
- Multi-source unit importing. Decision (2026-10-04): H2a owns shared engine-native unit-content interchange, inheritance/dependency resolution, capability validation, runtime materialization, and scoped publication. It runs next before M2 without waiting for H1/H2/H3. Generals INI proves legacy coverage; a bounded real EE2 slice proves a second source. M2 expands the EE2 adapter, Track C supplies visuals, M3 owns balancing, and H4 serializes the shared representation. Unsupported required gameplay blocks import instead of silently substituting defaults. This avoids source-specific runtime paths. Design and cards: [H2a](deep-dives/h2a-unit-content-import.md).
- Determinism priority. Decision (revised twice): determinism is not a roadmap requirement or a
  gate for parallelism, distribution, physics, AI, replay, or experiment tooling. Runs may diverge
  across executions, platforms, builds, thread schedules, and shard layouts. Validation uses
  per-run invariants, authored bounds, aggregate distributions, and performance statistics. Seed
  controls remain an experiment input but do not imply reproducibility. Within a tick,
  data-parallel systems may run concurrently and write freely; conflicting writes are reconciled
  through explicit intent/command buffers and authority rules. Supersedes the scoped-determinism
  revision (one run identity reproduces its run): the identity, hash, and fixed-order constraints
  it imposed are dropped in favor of the storage-first recording below. Consequences:
  - [A2](track-a-foundations.md) is statistical validation: distributions and invariants, no
    same-seed agreement or state-hash parity.
  - [B3](track-b-scale.md) uses free intra-tick parallelism with intent buffers and merge rules;
    commit order is not constrained.
  - [B6](deep-dives/b6-distributed-simulation.md) validates handoff and boundary sync by
    invariants such as no unit duplication or loss and conservation.
  - Physics may use a multithreaded dispatcher without `Simulation.Deterministic`.
  - Re-simulated frames are plausible, not authoritative:
    [A1-6 §3](deep-dives/a1-6-operational-recording.md#3-reconstruction-fidelity).
- Operational recording. Decision (revised): disk use takes priority over scrub latency and
  over exactness between checkpoints. Large runs record in `Operational` mode: operational-level
  records per operational boundary plus full engine checkpoints at frame zero and every 6 domain
  campaign hours (648,000 frames by default). Granular frames are rebuilt for the whole world by
  re-simulating from the nearest checkpoint with logged inputs; rebuilt frames are labelled as
  such and may drift from the original run. The operational stream is the authoritative record.
  No run identity or state hashes are recorded. Derived checkpoints are cached in memory only.
  `Full` mode (A1-5 per-frame recording) stays the RTS default so existing baselines are
  unchanged. `GameLogic.Random` becomes the persisted `SageRandom`. Region-local reconstruction
  is out of scope. Design and size estimates: [A1-6](deep-dives/a1-6-operational-recording.md).
- ECS adoption depth. Decision: incremental data-oriented storage behind the module API
  (no full ECS rewrite). This follows from the Simulation-domain scope & mapping Decision below:
  every domain, RTS and non-RTS alike, is hosted by extending the `GameObject`/module system
  rather than by adding separate runtimes, so one incremental SoA-style storage layer behind the
  existing module API serves all domains. Hot module data migrates toward data-oriented layouts
  opportunistically, driven by [B1](track-b-scale.md) benchmarks, while the module API and
  composition model stay intact. Affects [B3](track-b-scale.md).
- Distributed model. Decision: spatial sharding. One world partitioned by region across
  nodes (chosen over lockstep peers, which caps scale at a single machine's capacity). Trades
  that ceiling for cross-shard handoff and boundary sync. See
  [B6 deep dive](deep-dives/b6-distributed-simulation.md). Affects
  [B6](track-b-scale.md) and informs [A2](track-a-foundations.md)/[B3](track-b-scale.md).
- glTF scope for C4. Decision: static meshes + skinned skeletal animation only. The first
  production external format ([C4](track-c-external-assets.md)) covers the minimum needed to get
  externally-authored units, with mesh, skeleton, and gameplay-driven animation, into the engine.
  Morph targets (blendshapes) and advanced glTF PBR material extensions are deferred to
  [C6](track-c-external-assets.md) and added only if an experiment needs them. This keeps the
  first importer small and proves the C1 abstraction end-to-end as early as possible.
- C4 glTF importer architecture. Decision: use strict SharpGLTF.Core parsing behind a
  glTF-private adapter, then expose only C1-C3 engine-owned values. External resources resolve
  through the selected model's virtual root. One basis maps `(x, y, z)` to `(z, x, y)` across
  geometry, nodes, inverse binds, animation, and bounds, and one meter maps to approximately
  3.28084 foot-based world units. Scene nodes remain runtime bones for rigid attachments. The
  first skinning subset supports one skin and two meaningful influences, with inverse bind
  matrices added to the neutral skeleton.
  External-tool sessions select this importer explicitly without replacing shipped game
  definitions' single-importer strategy. Affects
  [C4](track-c-external-assets.md).
- Empire Earth II proof scope. Decision: [C4a](track-c-external-assets.md) is an additive,
  experimental second-format proof after C1, not a replacement for C4's production glTF
  importer. It loads the static NIF 10.1 War Galley and Twin-Engine Bomber models directly from
  an operator-supplied `graphics.zip`, bakes the Bomber's highest-detail LOD, preserves
  fixed-function values and source texture names, and renders untextured diffuse materials. KF
  animation, skinning, `.texcache` decoding, player-color patches, DDF integration, and older NIF
  versions remain outside the proof. No EE2 asset is committed.
- EE2 rigid animation scope. Decision: [C4c](track-c-external-assets.md) imports only the
  DDF-bound NIF 10.1 `.kf` clips of the C4a proof models, using the four-block
  `NiControllerSequence` / `NiKeyframeController` subset. Unit-scale NIF nodes become runtime
  bones so rests match the C4a bake. Keys take the form `<model>#kf=<path>` because KF files
  carry no rest pose. XYZ Euler rotations use `Rz·Ry·Rx` with NifSkope's segment-normalized
  Hermite tangents and resample at 30 Hz, rejecting steps of 90 degrees or more. Constant
  accumulation-root channels apply as pose, and varying root motion is rejected. Scale keys must
  be identity. Skinned characters move to C4d, which starts with an influence inventory before
  choosing between widening runtime skinning and rejecting assets. See the
  [C4c deep dive](deep-dives/c4c-ee2-rigid-animation.md). Affects
  [C4c and C4d](track-c-external-assets.md).
- EE2 skin influence limit. Decision: [C4d](track-c-external-assets.md) widens C1, the shared
  runtime mesh vertex layout, and glTF import from two to four influences per vertex. Rejecting
  beyond two would reject 87 of 124 DDF-bound skinned EE2 unit models, and four matches
  Gamebryo's hardware partition width. Influences at or below 1e-4 are dropped, more than four
  meaningful influences are rejected, and weight sums further than 0.01 from 1 are rejected. No
  influence is truncated. See the [C4d deep dive](deep-dives/c4d-ee2-skinned-animation.md#4-influence-policy).
  Affects [C4, C4d, and C4f](track-c-external-assets.md).
- NIF rotation convention. Decision: NIF `Matrix33` values are column-vector matrices, so every
  node, shape, skin, and Euler-sample rotation is transposed once when it enters a row-vector
  `Matrix4x4`. Quaternion keys stay unchanged. Skin transforms, upright character geometry, and
  time-0 quaternion keys all confirm this reading, and the positional copy used by C4a and C4c
  inverted non-symmetric rotations. C4d lands the fix first and regenerates the C4a and C4c
  locked outputs. See the [C4d deep dive](deep-dives/c4d-ee2-skinned-animation.md#5-nif-rotation-convention).
  Affects [C4a, C4c, C4d, and C4e through C4i](track-c-external-assets.md).
- Material-property animation targeting. Decision: [C4e](track-c-external-assets.md) property
  animation targets runtime sub-objects or material slots, not bones or shared material names.
  Visibility reuses the existing `Visibility` channel, with sub-object targets added beside bone
  targets. Material color, alpha, and UV channels bind against a `Model` because material slots
  belong to mesh parts. Shared `Material` and `MaterialPass` resources remain immutable; sampled
  values live on `ModelInstance` or `ModelMeshPartInstance`. See the
  [C4e deep dive](deep-dives/c4e-ee2-property-animation.md). Affects
  [C4e](track-c-external-assets.md).
- EE2 scale animation. Decision: [C4f](track-c-external-assets.md) supports NIF 10.1 uniform
  scale as a scalar rest field plus a rest-relative scale channel, using `sampleScale /
  restScale`. Positive finite rest scale is a bone transform, and identity scale remains
  unchanged. Animated zero scale is valid when rest scale is positive. Singular rest scale is
  rejected for animated targets because `AnimatedOffset * rest` cannot reconstruct non-zero
  samples from a zero rest transform. See the
  [C4f deep dive](deep-dives/c4f-ee2-scale-animation.md). Affects
  [C4f](track-c-external-assets.md).
- EE2 morph runtime ownership. Decision: [C4g](track-c-external-assets.md) implements the shared
  C6 morph-target runtime under the request-scoped `morph-targets` gate instead of depending on
  C6 as a completed phase. The EE2 farm proof is the first concrete morph consumer, and C6
  remains the envelope for later glTF and shader work. NIF morph 0 is the absolute base and is
  validated, then omitted; morphs 1..N become C6 position deltas. KF-owned target data uses a
  paired model key `<model>#morph-kf=<path>` so target sets exist before animation binding. See
  the [C4g deep dive](deep-dives/c4g-ee2-morph-animation.md). Affects
  [C4g](track-c-external-assets.md).
- EE2 particle runtime mapping. Decision: [C4h](track-c-external-assets.md) translates EE2
  legacy `NiParticles` effects into C1-style imported particle effect values and materializes
  them onto the existing Panopticon particle runtime. The corpus has 129 particle `fx_*` files,
  259 `NiParticles` systems, and no `NiPSys*` blocks, so `NiPSys*` is an explicit rejection path.
  Attachment names stay in the DDF binding layer and resolve to C4c node bones or fixed world
  origins. Deterministic captures require request-scoped particle RNG seeding. See the
  [C4h deep dive](deep-dives/c4h-ee2-particle-effects.md). Affects
  [C4h](track-c-external-assets.md).
- Legacy EE2 KF vintages. Decision: [C4i](track-c-external-assets.md) supports DDF-bound NIF
  10.0.1.0 keyframe-only clips by version-gating the C4c reader, parsing root-list footers,
  trimming text-key marker values, accepting proven legacy flags, and resolving uppercase `.KF`.
  NetImmerse 4.2.2.0 is rejected because only three unbound files use an inline-type container
  outside the parser architecture. Legacy property, morph, and skinned consumers remain with
  C4e, C4g, and C4d. See the [C4i deep dive](deep-dives/c4i-ee2-legacy-kf-formats.md). Affects
  [C4i](track-c-external-assets.md).
- StarCraft II storage access. Decision: [C7a](track-c-external-assets.md) is an in-house,
  read-only reader for local CASC storage with an MNDX root, with no native dependency and no
  CDN access. BLTE `N` and `Z` frames are supported; encrypted `E` and unknown frames are
  rejected. `CascFileSystem` exposes case-insensitive `/` paths. Duplicate paths with equal
  content keys collapse to one entry, and conflicting duplicates are rejected. SC2 roots stay
  separate from game assets. See the [storage deep dive](deep-dives/sc2-casc-m3-storage.md). Affects [C7a](track-c-external-assets.md).
- StarCraft II texture conversion. Decision: [C7b](track-c-external-assets.md) decodes DXT1 and
  DXT5 top mips in the importer and emits C3 embedded RGBA images, so C3 keeps rejecting DDS.
  Normal maps are DXT5nm (X in alpha, Y in green, Z reconstructed). Specular becomes metallic 0
  and roughness `lerp(1, r0, luma(spec))` with `r0 = (2 / (n + 2))^(1/4)`. Diffuse alpha is
  forced to 1 because it is the team-color mask. Layer paths resolve in the model's package, then
  `mods/core.sc2mod/base.sc2assets`, never teen or locale packages. Team color, effects, decals,
  and non-opaque batches are out of scope. See the [storage deep dive](deep-dives/sc2-casc-m3-storage.md). Affects
  [C7b](track-c-external-assets.md).
- StarCraft II coordinates and keys. Decision: M3 data is conjugated by `K = S(10) * Rz(+90
  degrees)`, mapping `(x, y, z)` to `(-10y, 10x, 10z)`, so one SC2 unit is 10 world units and
  -Y facing becomes +X. A Ranger side-by-side capture validates it, and a mismatch stops for
  review. Model keys are full CASC paths; animation keys are `<model>#seq=<name>` and
  `<model>#m3a=<path>#seq=<name>`. See the [storage deep dive](deep-dives/sc2-casc-m3-storage.md). Affects [C7b](track-c-external-assets.md) and
  [C7c](track-c-external-assets.md).
- StarCraft II skeleton and clips. Decision: [C7c](track-c-external-assets.md) keeps the first
  `MODL` skin-bone-count bones (lookup bones plus ancestors) and drops attachment, volume, and
  effect bones with their tracks. Bind transforms come from `IREF`, not `BONE` defaults; the
  defaults become constant channels for unanimated components. Sequences must have exactly one
  `STC_`, keys stay linear in milliseconds without resampling, and non-bone tracks are ignored.
  `.m3a` clips bind by animation id and require matching bone names. Group blending, masks, and
  root motion are out of scope. See the [C7c deep dive](deep-dives/c7c-sc2-skinned-animation.md). Affects [C7c](track-c-external-assets.md).
- StarCraft II scale and composition. Decision: [C7c](track-c-external-assets.md) extends C4f
  instead of adding a parallel path: `ImportedBone.BindScale` becomes `Vector3`, `ScaleX/Y/Z`
  channels are added, and `ImportedAnimation.Composition` adds `AbsoluteLocal` beside the default
  `RestRelative`. Non-uniform rest scale with animated rotation cannot be expressed as
  `offset * rest`, so absolute clips supply complete local transforms. `Transform.Scale` stays a
  float, existing formats stay byte-identical, and skinned normals keep the C4f normalize rule
  without an inverse transpose. See the [C7c deep dive](deep-dives/c7c-sc2-skinned-animation.md). Affects [C7c](track-c-external-assets.md) and
  [C4f](track-c-external-assets.md).
- C1 import-seam shape. Decision: importers parse source files into CPU-only engine interchange
  values; engine-owned materializers validate those values and create runtime models, animations,
  materials, textures, and GPU resources. `OnDemandAssetLoadStrategy` carries the selected
  `IModelImporter` / `IAnimationImporter`; shipped games select internal W3D adapters through a
  public in-assembly factory, while
  `OnDemandAssetLoadStrategy.None` remains importer-free. Referenced skeleton loading remains
  behind `IModelImporter` and the existing hierarchy cache. Missing assets return null with one
  loader warning; malformed or unsupported source data throws a contextual exception. This keeps
  file formats out of runtime graphics types, makes imported data inspectable without a GPU, and
  gives C2/C3/C4 one stable boundary. Affects
  [Track C](track-c-external-assets.md) (C1).
- C2 retargeting scope. Decision: imported animation channels use either direct bone indices or
  exact case-insensitive bone names. Binding occurs once per `ModelAnimation` and target
  `ModelBoneHierarchy`, before `AnimationInstance` playback, and the immutable result is held in a
  weak-key cache by hierarchy identity. W3D retains direct-index behavior, including omitting
  out-of-range channels rather than failing the asset. C2 adds no INI alias syntax, heuristic
  matching, humanoid semantics, bind-pose compensation, or per-frame name lookup. Affects
  [Track C](track-c-external-assets.md) (C2).
- C3 material contract. Decision: core metallic-roughness data uses a typed, format-neutral
  payload on `ImportedMaterial`, not shader-property naming conventions. Typed texture bindings
  carry external or embedded image sources, UV set, sampler, role, and color-space semantics.
  Engine-owned materialization validates and decodes them, generates missing tangent bases when
  possible, and renders through one core PBR shader path. Advanced `KHR_materials_*` semantics
  remain C6. Affects [Track C](track-c-external-assets.md) (C3/C4/C6).
- C6 extension activation. Decision: morph targets and advanced PBR remain request-scoped
  experiments behind named feature gates that default off. Morph deltas and weight animation use
  primitive-target identities separate from vertices and bone channels, with morph-before-skin
  deformation. Advanced materials use typed optional families and normalized shader feature keys;
  transmission and volume semantics require an explicit renderer-pass experiment. Required
  unsupported semantics fail, while optional material extensions may be ignored with a warning
  only when core output remains complete. Affects
  [C6](track-c-external-assets.md).
- BFME-family models. Decision (2026-10-18): BFME, BFME2, and RotWK models load through the native
  W3D reader and its C1 adapters, not a separate importer, because the existing reader already
  parses 99.9% of the corpus. Bad corpus files fail soft with one diagnostic each. BFME gameplay
  INI stays in D3. Revised the same day after grounding: shader materials stay on the existing
  `NormalMapped` and `Simple` shader sets instead of C3 PBR, and two-bone skins use the existing
  runtime, so C8b no longer depends on C3 and C8c no longer depends on C4d. See the
  [deep dive](deep-dives/c8-sage-family-w3d.md) section 6. Affects
  [C8a-C8c](track-c-external-assets.md).
- Unlicensed format references. Decision (2026-10-18): public tools without a published license
  (AssetEditor for Total War, ptasev Rise-of-Nations for BH3) are factual references only and no
  code is copied, matching the C7 clean-room rule. MIT tools (RPFM, Resource Manager, LSLib) may be
  ported with credit. Affects [C9-C11](track-c-external-assets.md).
- Granny decompression. Decision (2026-10-18): C10b and C10c support uncompressed Granny files only
  and reject Oodle1 and BitKnit2 sections explicitly. Decompression is the deferred C10d phase,
  gated on a provenance and license review of the decompressor source. See the
  [deep dive](deep-dives/c10-aoe3de-granny-models.md#6-references-and-licensing). Affects
  [C10b-C10d](track-c-external-assets.md).
- Rise of Legends source. Decision (2026-10-18): C11 reads an installed game directory only.
  Installer repack extraction depends on patched third-party tools and installer passwords, so it
  stays out of the roadmap. See the [deep dive](deep-dives/c11-rise-of-legends-models.md#2-install-and-packaging).
  Affects [C11a](track-c-external-assets.md).
- Total War: SHOGUN 2 models. Decision (2026-10-29): SHOGUN 2 is its own phase group, C12, not
  part of C9. Its `PFHs` packs, `RMV2` versions 0 to 2, `VMPF` parts, and version 1 clips share no
  binary layout with THREE KINGDOMS, so only the pack project and the LZ4 decoder are shared. See
  the [deep dive](deep-dives/c12-total-war-shogun2-models.md#9-phase-split). Affects
  [C9a](track-c-external-assets.md) and [C12a-C12c](track-c-external-assets.md).
- Large-map ceiling & compatibility. Decision: no size cap; no compatibility constraint.
  Maps scale arbitrarily (bounded by disk, not RAM/format), to be grown gradually for scaling
  experiments; Panopticon may extend/replace the `.map` format and need not stay loadable in the
  original engine. Makes chunked storage (B4a-5) a core requirement. See
  [B4a deep dive](deep-dives/b4a-larger-maps.md).
- Variable-fidelity consistency stance. Decision: fidelity is a function of replicated
  simulation state for anything shared or distributed; view-driven fidelity is allowed only for
  single-node, non-distributed experiments. Shared and distributed
  runs therefore derive fidelity inputs from replicated state so every shard agrees on each
  entity's fidelity level, never from a local camera or view. View-driven fidelity, such as LOD
  by camera distance, remains available for single-node experiments where no shard can disagree.
  Affects [B7](deep-dives/b7-variable-fidelity.md) and
  [A2](track-a-foundations.md)/[B6](deep-dives/b6-distributed-simulation.md).
- Asset licensing. Decision: out of scope (single-user tool). For the foreseeable future
  this is a personal experimentation tool used by one operator, so import tooling
  ([C5](track-c-external-assets.md)) does not enforce or track provenance or licensing. Revisit
  this before the tool is distributed, shared, or used to redistribute external assets; the engine
  still ships no assets. Until then, respecting source licenses is the operator's manual
  responsibility.
- Simulation-domain scope & mapping. Decision: validate against a transport/graph domain
  first; host non-RTS domains by extending the existing `GameObject`/module system. The
  [Simulation Domain Model seam](track-a-foundations.md) keeps the domain pluggable (RTS first,
  then transport/graph, later city builder / agent-based). Two sub-questions are now settled:
  - (1) First non-RTS validation domain = transport/graph. Chosen over a city-builder because
    a graph/network domain best stresses non-spatial partitioning (graph-cut rather than a 2-D
    grid), edge-coupled interaction locality, and edge-owner resolution authority: exactly the
    parts of the A4 seam, [B6](deep-dives/b6-distributed-simulation.md) partitioning, and A5-4
    divergence bucketing that an RTS-only design would leave RTS-shaped. It forces the seam to be
    genuinely topology-general early.
  - (2) Mapping strategy = extend the `GameObject`/module system, not parallel domain adapters
    over the B3 data-oriented storage. The module-based composition engine is intended to be
    generic enough to host non-RTS entities/interactions (transport vehicles/links as
    `GameObject`s with domain-specific modules), so a domain is expressed as a set of modules +
    a topology/interaction adapter rather than a separate runtime. This keeps one entity runtime,
    one save/persist path, and one set of B3 storage optimizations behind the domain seam. (Also
    resolves the storage half of the ECS adoption depth question toward incremental,
    behind the module API rather than a separate ECS substrate per domain.)
  - Affects [A4](track-a-foundations.md), [A1](deep-dives/a1-headless-host.md),
    [B6](deep-dives/b6-distributed-simulation.md), and [B3](track-b-scale.md).
- P0 harness: runtime object references. Decision: implicit auto-handles + opt-in names + query
  selectors. Authored commands in the [P0 harness](deep-dives/p0-playable-baseline-harness.md)
  reference an object an earlier command created (a produced unit, a built structure) by an
  implicit handle keyed to the producing command, resolved at dispatch from build/production
  results, so an author normally writes nothing ("attack the barracks built in command #3" just
  works). An explicit name is opt-in sugar for readability/disambiguation. Objects the script did
  not create (enemy/world objects such as an attack target) are referenced by a small set of
  query selectors (faction/relationship/definition/proximity) resolved against `GameLogic` at
  dispatch. Chosen over named-handles-only (forces noisy boilerplate and can't name pre-placed or
  enemy objects) and query-only (non-deterministic and fragile for the script's own objects). This
  generalizes today's `AfterActionTiming`/`DeferredAction` trigger model. Affects
  [P0](track-p-playable-baseline.md) (P0-1).
- P0 harness: assertion DSL. Decision: JSON predicates over a fixed vocabulary, with a
  code-defined escape hatch. The authored assertion surface for the
  [P0 harness](deep-dives/p0-playable-baseline-harness.md) is declarative JSON predicates (e.g.
  `{path: "player[1].money", op: ">=", value: 0}`) over a fixed path/operator vocabulary spanning
  outcome, state, and per-tick invariant assertions: serializable into scenario files, trivially
  machine-readable, and cheap for low-effort agents/CI to author. Complex invariants that exceed the
  vocabulary use an escape hatch of code-defined (C#) predicates registered by name and
  referenced from the scenario. Chosen over JSON-only (too limiting for rich invariants), a
  small expression language (needs a parser/evaluator for marginal ergonomic gain), and
  code-only (not serializable or agent-authorable). Affects
  [P0](track-p-playable-baseline.md) (P0-4).
- P0 harness: MAUI screenshot mode. Decision: agents verify MAUI-layer changes (layout, tab
  switching, view wiring, error display) by running the app in screenshot-and-exit mode via
  `REPLAYSKETCH_SCREENSHOT_PATH`, `REPLAYSKETCH_SCREENSHOT_TAB`, and `REPLAYSKETCH_SCREENSHOT_DELAY`
  environment variables. `MainPage.OnLoaded` detects the variables, selects the requested tab, waits
  the settle delay, captures the window via `Microsoft.Maui.Media.Screenshot.Default.CaptureAsync()`,
  saves the PNG, and calls `Application.Current.Quit()`. Chosen over an external `screencapture`
  script (requires window ID detection, timing heuristics, and no communication with the app) and
  over UI-test frameworks (heavy, platform-bound, flaky). The in-app approach is self-contained,
  requires no extra tooling or permissions, and works with `dotnet run`. Implemented as P0-1a;
  documented in `src/Panopticon.Tools.ReplaySketch.Maui/AGENTS.md`. Affects
  [P0](track-p-playable-baseline.md) (P0-1a).
- P0 harness: MAUI validation reach + agent playbook. Decision: keep logic in `.Core`, validate
  headlessly through the test suite plus run-and-assert, minimize MAUI-only code behind injectable
  seams, and document the loop in a checked-in `AGENTS.md`. ReplaySketch logic stays in
  `Panopticon.Tools.ReplaySketch.Core` and is validated headlessly by
  `Panopticon.Tools.ReplaySketch.Tests` plus the [P0 harness](deep-dives/p0-playable-baseline-harness.md)
  run-and-assert entry point (P0-5). `MainViewModel` is reduced to a thin shell by abstracting
  `Preferences.Default`, `MainThread`, and `Process.Start` or launcher behavior behind injectable
  seams (P0-1b/P0-1c), so the command surface is unit-testable without a MAUI runtime. The
  documented validation loop lives in `src/Panopticon.Tools.ReplaySketch.Maui/AGENTS.md` (P0-7),
  mirroring `Panopticon.Tools.TestHarness/AGENTS.md`. Chosen over MAUI UI automation and over leaving
  validation undocumented. Affects [P0](track-p-playable-baseline.md) (P0-1b/P0-1c/P0-7).
- P0 harness: ScenarioMatrix sweep placement. Decision: the cartesian-product matrix expansion
  primitive belongs in P0 (P0-5b), not deferred to A5. A5-2 adds tooling and visualization on
  top; it does not re-implement expansion or execution. All axis dimensions (OrderType, faction,
  map, assertion parameters) are in scope; the matrix is built out incrementally alongside
  implementation. Affects [P0](track-p-playable-baseline.md) (P0-5b) and
  [A5](track-a-foundations.md) (A5-2).
- P-track object ownership tracking. Decision: maintained per-`Player` owned-object set and
  counts. Building and unit counts, plus an owned-object set, are maintained incrementally on
  create, transfer, `OnDie`, and `Destroy`, giving O(1) ownership checks. This replaces an
  on-demand all-objects scan such as the `Player.GetEnergy` pattern (`Player.cs:92-104`) because
  the same state is queried frequently by the victory check ([P3](track-p-playable-baseline.md))
  and the AI decision loop ([P2](track-p-playable-baseline.md)), and because maintained counts fit
  the scale tracks better. Realized as foundational sub-phase
  [P1-5](deep-dives/p1-skirmish-setup.md), owned by P1 and shared by both consumers. Affects
  [P1](track-p-playable-baseline.md), [P2](track-p-playable-baseline.md),
  [P3](track-p-playable-baseline.md).
- Skirmish AI actuation. Decision: emit `Order`s through `OrderProcessor`. The AI issues the
  same `Order`s a human would (`BuildObject`/`CreateUnit`/`ForceAttack…`) rather than calling engine
  APIs directly, so AI actions enter the recorded/replayed order stream and remain consistent with the
  [P0 harness](deep-dives/p0-playable-baseline-harness.md). Trade-off accepted: the AI must synthesize
  valid order arguments (positions, object ids). Must be settled before P2-2. Affects
  [P2](track-p-playable-baseline.md).
- Skirmish AI competence scope. Decision: minimum legal opponent in Track P; configurable
  difficulty tiers designed but deferred. Track P implements only a minimum legal AI: it builds,
  economizes, produces, and mounts at least one attack so a match terminates win/lose; it is not
  balanced or tournament-grade. The AI is structured for configurable difficulty tiers, but only
  the minimum tier is implemented now; richer behavior (reactive defense, teching, multi-wave
  pressure) is later expansion (a candidate [A4](track-a-foundations.md) pluggable-AI experiment
  seam). Affects [P2](track-p-playable-baseline.md).
- Pluggable AI decision policy. Decision: formalize how a player's strategic decisions get made
  (build/produce/attack) as its own A4 experiment seam now that P2 has a reference
  implementation to carve it from. `AIPlayer.OnDecisionTick()`'s current subclass-override shape
  becomes an injectable `IAIDecisionPolicy`, with `SkirmishAIPlayer`'s rule-based state machine
  as the reference implementation; actuation is unaffected: every policy still emits `Order`s
  through `OrderProcessor` (the P2 actuation decision above), so the recorded/replayed order
  stream holds regardless of which policy runs. This is what unblocks a
  neural-network policy trained by running many seeded self-play matches (a population of
  weight-vector genomes, fitness scored off `GameOutcome`, evolved via selection/mutation across
  generations in an A4 experiment sweep). Design-only for now; implementation (interface
  extraction, a first NN policy, and the training harness) is follow-on work. Detailed design:
  [deep-dives/a4-pluggable-ai.md](deep-dives/a4-pluggable-ai.md). Affects
  [A4](track-a-foundations.md), [P2](track-p-playable-baseline.md).
- Decentralized hierarchical AI. Decision: generalize the A4 AI decision policy seam with a
  rank hierarchy (general, one per player; commander, one per squad; soldier, one per unit) of
  per-rank neural networks, rather than a single flat policy per player. This is an alternative
  policy family behind the same `IAIDecisionPolicy` seam, not a replacement for it: a
  `HierarchicalAIDecisionPolicy` adapter runs the ranks and still emits every order through
  `OrderProcessor`, so the actuation constraints already established for A4/P2 hold unchanged.
  Squads derive from the existing team/selection model; a squad's commander is
  promoted from its highest-value surviving member on commander death. Network complexity scales
  by rank (soldier smallest, general largest), not by total unit count. Design-only for now;
  implementation and a co-evolutionary training scheme are follow-on work. Detailed design:
  [deep-dives/a4-decentralized-hierarchical-ai.md](deep-dives/a4-decentralized-hierarchical-ai.md).
  Affects [A4](track-a-foundations.md).
- Single-player `LocalPlayer` identification. Decision: explicit human-slot flag at setup. The
  skirmish/lobby config marks which slot is the local human; `PlayerManager` reads that flag instead
  of the ad-hoc positional `_players[2]` fallback (`PlayerManager.cs:39-46`). Affects
  [P1](track-p-playable-baseline.md).
- Skirmish spawn path. Decision: one shared SP+MP skirmish spawn. Single-player becomes "a
  skirmish with one human + AI slots," driven through the same start-position/starting-building/camera
  code as multiplayer (`Game.cs:745-763`), removing the SP/MP split. Affects
  [P1](track-p-playable-baseline.md).
- Victory defeat rule. Decision: real Generals rule by default, configurable. A player is
  defeated when they have no command/production structures and no means to rebuild (no
  dozer/worker + insufficient resources), per Generals skirmish rules, implemented as the default
  but exposed as a configurable/data-driven rule so experiments can swap simpler/stricter
  conditions. Affects [P3](track-p-playable-baseline.md).
- Match end-state enumeration. Decision: six end states, full `GameOutcome` type, all validated
  by P7. Three `OutcomeReason` values: `Eliminated` (standard defeat or sudden-death resolution),
  `Surrendered` (any player/AI issues `OrderType.EndGame`), `ScoreDecision` (time-limit expiry
  with configurable score-metric tiebreak). Two additional `OutcomeStatus` values beyond
  `InProgress`/`Decided`: `SuddenDeath` (transitional, mutual simultaneous elimination detected,
  match continues). Mutual elimination resolves via sudden-death continuation to the next
  `Eliminated` outcome. Time-limit expiry resolves via highest-score win (score metric is
  configurable, default = buildings standing). Team wins are expressed by setting `WinningTeam`
  rather than a separate reason. All six end states have P7 regression scenarios. Affects
  [P3](track-p-playable-baseline.md) (P3-5/P3-6), [P7](track-p-playable-baseline.md).
- Unit-contract validation scope. Decision: per-archetype full contract via P0 harness; one
  representative per role; surfaced gaps are explicit, not silent. Six archetypes cover all
  meaningful unit-behaviour variation in Generals/ZH: infantry, vehicle, aircraft, builder,
  support, superweapon. Each is validated against a full contract (produce/move/attack/every
  weapon mode/every special ability/die with ownership cleanup) via one P0 harness scenario.
  "Full" means every `SpecialPowerType` in the unit's command set and every `WeaponSlot` are
  exercised; unimplemented handlers produce explicit assertion failures (not silent no-ops) and
  become tracked P6b gap items. Archetype granularity chosen over per-faction-unit (50+ units,
  excessive) and per-role-category-only (misses ability/weapon-mode variation). Representative
  units are from the USA reference faction; the scenario pattern is faction-agnostic via the P0
  harness (any faction's unit can be substituted by name). Affects
  [P6b](track-p-playable-baseline.md) (all sub-phases), [P7](track-p-playable-baseline.md).
- Framework audience & distribution scope. Decision: single-operator personal research tool,
  whole framework. The entire simulation framework, not just the C-track import tooling, is a
  personal experimentation tool for one operator for the foreseeable future. This generalizes the
  asset-licensing Decision above: no multi-user robustness, external/third-party reproducibility, or
  distribution hardening is a goal. Human-facing tooling ([A5](track-a-foundations.md) authoring/
  visualization, [B1](track-b-scale.md) benchmarks) targets that one operator, and runs need only be
  reproducible-for-comparison by them, not citable or shareable by others. Deliberate and revisitable:
  if the framework is ever shared, published, or used to distribute assets, multi-user concerns (and
  the C5 provenance/licensing stance) must be revisited first. Affects the
  [README guiding principles](README.md), [A5](track-a-foundations.md), [B1](track-b-scale.md).
- Near-term scale anchor. Decision: 10k units at interactive rate (~30 fps) on one machine.
  The first committed waypoint toward the long-term thousands–millions goal. Track B milestones
  (B2–B5, B7) are designed and budgeted against this single anchor; the larger {100k, millions}
  figures stay directional, not yet budgeted. [B1](track-b-scale.md) fixes a seeded 10k-unit
  interactive scenario as the reference workload, and B2 (allocation), B3 (parallelism),
  B4 (pathfinding), B5 (rendering), and B7 (fidelity) each target sustaining ~10k units at ~30 fps
  single-node before the anchor is raised. Affects [B1–B5, B7](track-b-scale.md).
- Geospatial map import source boundary. Decision: consume operator-supplied exports, not a live
  Google service. B4b accepts KML/KMZ exported from Google Earth plus georeferenced DEM and imagery
  raster files exported from operator-authorized Google Earth Engine datasets or other sources.
  Google basemap imagery and terrain are not supported inputs. B4b records source and projection
  metadata but does not authenticate to, scrape, or download from Google services. Repository
  tests use synthetic fixtures. This keeps the importer deterministic and service-independent
  while leaving source-data rights with the operator under the existing single-operator scope.
  Affects [B4b](deep-dives/b4b-geospatial-map-generation.md).
- B4b geospatial pipeline. Decision: the B4b-1 deferred choices are settled as follows.
  1. Dependencies: `BitMiracle.LibTiff.NET` with in-house GeoTIFF key parsing; in-house WGS84
     Transverse Mercator (Krüger series, order 6); no projection package.
  2. Coordinate systems: sources are EPSG:4326 and WGS84 UTM only; others are reprojected by the
     operator at export time.
  3. Target: WGS84 Transverse Mercator with a custom central meridian (default: extent centre
     rounded to 0.01 degrees) or a UTM preset. Import fails when point scale error exceeds 0.1%.
     One import can therefore span UTM zones 31 and 32 for continuous panning across the front.
  4. Vertical datum: metadata only, no geoid conversion; mixed known datums are rejected.
  5. Payload: `float32` game-unit heights in 128-interval chunks (131 x 131 with apron) and
     chunk-aligned RGBA8 PNG imagery.
  6. KML altitude: geometry is always clamped to terrain; altitude and `styleUrl` are kept as
     attributes.
  7. Materials: integer classification values and polygon attributes map through manifest rules;
     no built-in vocabulary. An ESA WorldCover mapping ships only as an example.
  8. Legacy `.map` export: deferred until H5. A dev-only preview converts payloads into a resident
     legacy `MapFile` for play testing.
  9. Reimport: replace-only.
  10. Placement: new CPU-only `Panopticon.Geospatial` project with no engine references.
  B4b-1..6b proceed before B4a and H with a standalone payload; B4b-7..9 stay blocked. Affects
  [B4b](deep-dives/b4b-geospatial-map-generation.md), [B4a-5](deep-dives/b4a-larger-maps.md),
  [H1-H2](track-h-storage-formats.md).
- Western Front terrain currency. Decision: only the geographic area must be historical. Modern
  land cover, roads, and buildings from present-day sources are acceptable in W3a and W3b
  generated terrain; 1916 trench, wire, and crater state comes from W-track summaries, not from
  source imagery. W3a adds operator-digitized 1870 features (rail, bridges, fortress, inundations,
  border) over modern layers.
  Affects [W3a-W3b](deep-dives/w3-w6-integration-experiments.md), [B4b](deep-dives/b4b-geospatial-map-generation.md).
- Western Front decision gates. Decision: the twelve gates of the
  [Western Front charter](deep-dives/western-front-scale-simulation.md#11-decision-gates) are
  settled as follows, and the goal becomes [Track W](track-w-western-front.md).
  1. Extent: a Somme 1916 sector of about 30 x 20 km in UTM zone 31N. W0-W2 use synthetic data;
     W3b generates the sector through B4b; W6 covers July-November 1916. Sedan 1870 (W3a)
     precedes it; see the Sedan Decisions below.
  2. Granularity (revised by the detail-first stance below): the platoon (about 50) is the
     smallest persistent formation; the tactical entity is one soldier, one `GameObject` with its
     own health, weapon, and ammunition. Squads and sections are A4-3 groupings, not objects.
  3. Clocks (revised): a domain-owned multi-rate scheduler over host logic frames. Campaign
     cadence 1 h, operational 1 min, tactical one logic frame.
  4. Aggregate combat: stochastic Lanchester (square law for aimed fire, linear law for area and
     artillery fire) on a seeded `IRandom`, plus a mean-field variant for comparison.
  5. Promotion and demotion: triggers and hysteresis are an experiment input behind an activation
     policy; conservation is exact over integer quantities, with no tolerance.
  6. Command and information: echelon-local fog; orders travel per link with latency by
     communication type (runner, telephone, wireless) and can be disrupted.
  7. Logistics: commodities are artillery ammunition, small-arms ammunition, and personnel
     replacements; a link is owned by its tail node's region.
  8. Terrain outside active regions: per-region summarized trench, wire, and crater levels.
  9. Sharding: deferred to W5, keyed by region, for memory and capacity rather than speed.
  10. Persistence: recording tiers only through W5; save/load arrives in W6 as a domain-state
      snapshot plus host save.
  11. Interaction: headless runs plus A5 visualization; no player control.
  12. Acceptance (revised): W0 and W1 measure only. The all-tactical run is the reference and
      the primary product; W2 measures aggregate error against a whole-sector all-tactical run.
  Affects [Track W](track-w-western-front.md).
- Western Front detail-first stance. Decision: Western Front runs are offline, never real time,
  and detail outranks performance. This revises gates 2, 3, 9, 10, and 12 above. Performance work
  (B1, B6, B7) measures cost and adds capacity; it does not reduce modelled detail by default.
  Affects [Track W](track-w-western-front.md), [B1](track-b-scale.md), [B6](deep-dives/b6-distributed-simulation.md),
  [B7](deep-dives/b7-variable-fidelity.md).
- Western Front aggregate numerics. Decision: seeds derive from XxHash64 over
  `runSeed|regionPairKey|tickIndex|streamName`; Poisson draws are exact (Knuth below lambda 10,
  Hörmann PTRS above); the mean-field accumulator is a fixed-point `long` at 1e-6 personnel;
  parameters are JSON `{ value, unit }` pairs checked against a unit schema; reference command
  policies are rule-based and reactive to force ratios in each headquarters' intelligence picture.
  Experiments use 64 seeds per arm or cell. Affects [W1](deep-dives/w1-aggregate-sector.md),
  [W2](deep-dives/w2-fidelity-promotion.md), [W4](track-w-western-front.md).
- Western Front tactical engagement. Decision: placeholder soldiers are `AmericaInfantryRanger`
  (blue) and `ChinaInfantryRedguard` (red) with stock health; a casualty is an object death,
  classified killed, wounded, or missing by seeded draw. Artillery stays operational and fires into
  an active area only when a bombard order targets it, as direct `DamageInfo` impacts. Every
  contested region activates by default, with no concurrency cap, no fixed engagement bound, and a
  3-tick dwell. Affects [W2](deep-dives/w2-fidelity-promotion.md).
- Western Front sector. Decision: W3b covers lat 49.90-50.17 N, lon 2.52-2.80 E in UTM 31N at true
  scale (0.3048 m per game unit), from IGN RGE ALTI 1 m reprojected by the operator, modern
  OSM-derived features, and operator-digitized 1916 front lines. Regions are 1 km with 8 x 8 cells.
  Affects [W3b](track-w-western-front.md), [B4b](deep-dives/b4b-geospatial-map-generation.md).
- Sedan 1870 interim goal. Decision: before the Somme, Track W simulates the Battle of Sedan,
  31 August 00:00 to 2 September 12:00 1870 local mean time, as phase W3a, the first geospatial
  battle and the large-scale proof of concept. The former W3 Somme sector becomes W3b and depends
  on W3a; W3a owns the generic topology builder, grid, feature mapping, and A5 geospatial
  rendering. The all-tactical run (about 320,000 soldier `GameObject`s) is the reference, with
  aggregate and mixed arms measured against it. Forces follow the historical order of battle to
  battalion, battery, and squadron, expanded to the platoon floor by period templates. Orders
  follow a historical timeline delivered with simulated latency; recipients execute, adapt, or
  reject; the outcome emerges. Acceptance is measure only; historical figures are a reference.
  Affects [W3a](deep-dives/w3a-sedan-1870.md), [W3b](deep-dives/w3b-somme-sector.md),
  [W6](deep-dives/w6-long-run-campaign.md).
- Sedan geography and datasets. Decision: the extent is lat 49.58-49.82 N, lon 4.72-5.20 E in UTM
  31N at true scale, from IGN RGE ALTI 1 m and the Wallonia 1 m DTM mosaicked by the operator
  (Belgian heights shifted to `NGF-IGN69` by a recorded constant), modern OSM-derived layers, and
  operator-digitized 1870 features from public-domain staff maps. No geospatial data is committed.
  The order-of-battle and orders-timeline datasets are committed JSON transcribed from
  public-domain primary sources (Moltke/Forbes 1893, Clarke's General Staff translation, Hooper
  1887) with per-entry source and confidence. Times use Sedan local mean time, UTC+00:19:48.
  Affects [W3a](deep-dives/w3a-sedan-1870.md).
- Sedan tactical model. Decision: artillery is tactical, each gun a crewed direct-fire object with
  limber and caisson; period fire uses a seeded per-shot hit table behind a new weapon
  hit-resolution seam that is null by default; shells burst with a lethal radius and La Hitte
  time fuzes have two presets; cavalry troopers carry a `horse` holding. New families `horse` and
  `mitrailleuse` and sinks `captured` and `interned` join the ledger; equipment capture is a
  holding transfer. Couriers and a field telegraph join the comm types; telephone and wireless are
  disabled. Command changes are timeline entries. Local surrender and army capitulation are
  policies over encirclement and cohesion; capitulation sets `GameOutcome` `Decided` with
  `Surrendered`. Reference runs use 4 seeds and comparison arms 64. No save/load before W6; the
  scale probe escalates if a reference seed projects beyond 14 days of wall-clock. Affects
  [W3a](deep-dives/w3a-sedan-1870.md), [seams](seams.md).
- Western Front experiments and distribution. Decision: W4 sweeps the full 216-cell grid. W5 is the
  first B6 implementation domain, ahead of RTS and F4a: shards are strips perpendicular to the
  front so contested pairs never cross shards, lockstep per operational tick in compressed mode and
  per logic frame in tactical mode, handoffs every host frame, JSON boundary messages in process
  and compact binary across processes. W3a defines the historical order-of-battle schema; whether W6 uses it for 1916 is decided at W6.
  Affects [W4-W6](track-w-western-front.md), [B6](deep-dives/b6-distributed-simulation.md).
- Benchmark harness. Decision: B1 is a custom macro harness over the headless `SimulationHost`
  (per-tick and per-system time, allocations, GC, peak memory) with BenchmarkDotNet for micro
  benchmarks only; results are JSON records compatible with A2 reports plus a CSV summary;
  medians carry bootstrap 95% confidence intervals and non-overlap flags a regression. Affects
  [B1](track-b-scale.md).
- Variable fidelity scope. Decision: B7 plans B7-1 (model and policy), B7-4 (group aggregation),
  and B7-6 (visualization and error) only; per-object levels live in a `FidelityRegistry` keyed by
  object id. Affects [B7](deep-dives/b7-variable-fidelity.md).
- Map storage format. Decision: B4a-2 undo stores region deltas; B4a-4 uses CDLOD; B4a-5 owns the
  runtime chunk store and H3 owns the on-disk encoding. The H3 map is a directory package
  (`manifest.json` plus one raw little-endian file per chunk per layer), mirroring the B4b payload.
  `WI-H1-02` confirms the choice against the H1 audit. Affects [B4a](deep-dives/b4a-larger-maps.md),
  [H1-H3](track-h-storage-formats.md).
- Western Front clock compression. Decision: with no active tactical region, one host frame
  advances 60 s of theater time (one operational tick). With an active region, a host frame
  advances one logic frame, and theater time is `entryMs + round(frames * MsPerLogicFrame)` so the
  float frame interval never accumulates drift. Mode changes apply only on operational boundaries
  and are recorded as clock conversions. Affects [W0](deep-dives/w0-charter-fixture.md#7-clock-and-scheduler).
- Western Front runtime placement. Decision: the domain lives in a separate
  `Panopticon.Simulation.WesternFront` project on the shared `GameObject`/module runtime, granted
  `InternalsVisibleTo` because `ModuleData.CreateModule` is internal. Conserved integers stay on
  member formation records, including while a formation is inside an aggregate. W2 is the first
  B7-4 group-aggregation instance and does not wait for B7. Affects
  [Track W](track-w-western-front.md), [B7](track-b-scale.md).
- Reconstructable domain state. Decision: add an optional `IDomainStateProjection` beside
  `ISimulationDomainModel` (checklist item A4-4) rather than widening `DomainEntitySummary`. A
  domain declares a versioned schema whose fields carry unit, layer, and conservation role
  (`Holding`, `Sink`, `Source`, `None`); conserved fields must be integers. State records go to a
  separate `*.domainstate.bin` artifact, and the new `RunOutput` fields are omitted when null, so
  RTS run-output JSON is unchanged. Affects [A4](track-a-foundations.md),
  [A4-4 deep dive](deep-dives/a4-4-domain-state-projection.md).
- Non-RTS validation domain completeness. Decision: minimal partition-stress harness. The first
  non-RTS (transport/graph) domain (see the Simulation-domain scope Decision) is implemented only as
  far as needed to exercise graph-cut (non-spatial) partitioning and edge-coupled interaction/owner
  authority, not a complete, terminating, Track-P-grade scenario. It instantiates graph entities,
  runs them headlessly, and emits the partition/boundary metrics A4/B6 need, but requires no
  economy/production/victory/AI loop. Just enough to prove the [A4](track-a-foundations.md) domain
  seam and [B6](deep-dives/b6-distributed-simulation.md) partitioning are topology-general rather than
  RTS-shaped. Affects [A4](track-a-foundations.md), [B6](deep-dives/b6-distributed-simulation.md).
- C5 external-model tooling. Decision: extend ReplaySketch with a dedicated Model tab, no
  dedicated viewer. ReplaySketch Core owns session state, stable diagnostics, animation pairing,
  preview eligibility, and reports; MAUI owns presentation. TestHarness performs one isolated
  graphics-backed capture per versioned request and disposes GPU resources on exit. External
  roots never layer into game assets, and transforms remain preview-only. This preserves the
  no-fragmentation tooling convention and keeps graphics out of the external-model Core path.
  Affects
  [C5](track-c-external-assets.md).
- Save/load (StatePersister) reach. Decision: single-node only. The single `StatePersister`
  save/persist path is required to work only for single-node runs; it is not required to serialize
  sharded/distributed ([B6](deep-dives/b6-distributed-simulation.md)) state. Distributed runs rely on the
  recorded snapshot/delta stream for analysis and replay, not on
  `StatePersister`. The "one save/persist path" in the Simulation-domain scope Decision means one path
  per node, not one path spanning shards. Headless single-node runs may still round-trip save/load;
  cross-shard save/load is out of scope unless a later experiment needs it. Affects
  [A1](deep-dives/a1-headless-host.md), [B3](track-b-scale.md),
  [B6](deep-dives/b6-distributed-simulation.md).
- Non-SAGE gameplay mechanics scope. Decision: hosted as new Track D content on the existing
  RTS domain, not a new Simulation Domain Model. Age of Empires/Empire Earth style tech trees and
  era/age progression are new gameplay content and mechanism (a generalized prerequisite graph
  plus a new era state machine), added by extending the existing `GameObject`/module/`ObjectDefinition`
  system per the domain-scope Decision above: they are not different enough in kind (entities,
  interactions, topology) to justify a second Simulation Domain Model implementation. External
  content (models, INI overrides) for this track is ingested via the standalone
  [ModCollector tool](plans/mod-collector.md) (download/stage community mod archives into `.big`
  files) plus Track C's import abstraction (C1). ModCollector is a genuine prerequisite for
  Track D phase D3 onward, but D1/D2 (the tech-tree/era mechanism itself) do not need it and can
  be validated against existing built-in content first. Affects
  [Track D](track-d-gameplay-expansion.md),
  [deep-dives/d1-d2-tech-tree-era-progression.md](deep-dives/d1-d2-tech-tree-era-progression.md).
- Object physics, deformable terrain & destruction scope, Decision: own track (Track F), not folded
  into an existing track; destruction is physics-based, not model-swap. Runtime terrain deformation
  (craters/scorch marks) does not exist in original SAGE, and Track F's destruction (F4a/F5)
  replaces the canned model-swap approach (rubble/damage-state model swaps, already covered under
  P6-2/P6-3) with rigid-body physics simulation (debris/structural collapse via a new pluggable
  physics seam). Both are new fidelity beyond what P6-2/P6-3 verify, so neither is gated by Track P
  playable-baseline fidelity. The track is split out because it cuts across terrain storage
  ([B4a](deep-dives/b4a-larger-maps.md)), rendering ([B5](track-b-scale.md)), a new physics substrate,
  combat ([P6](deep-dives/p4-p6-economy-production-combat.md)), and save/replay rather than fitting
  inside any single existing track; the same
  reasoning that split gameplay content out as Track D. Named "Track F" (not "Track D"/"Track E")
  to avoid collisions with two pending unmerged branches: `docs/track-d-gameplay-expansion`
  (Gameplay Content Expansion) and `docs/track-e-naval-warfare` (Naval Warfare). Since superseded
  by events: both branches merged (PRs #5 and #6), so Tracks D and E are on `main` and the letter
  assignment stands on that basis rather than on pending work. The physics
  engine/library is an open candidate, settled when F4 starts, mirroring how Track C left the
  first external model format ([C4](track-c-external-assets.md)) as a candidate. Since settled:
  BepuPhysics2, see the object physics engine Decision below. Affects
  [Track F](track-f-terrain-destruction.md).
- Object physics engine, Decision: BepuPhysics2, consumed as NuGet packages `BepuPhysics` +
  `BepuUtilities` 2.4.0 (Apache-2.0, GPLv3-compatible) behind an engine-internal object physics
  seam, settling the F4 candidate. SAGE behavioral fidelity is no longer binding for physics.
  Every instantiated `GameObject` resolves through one engine-level participation policy:
  dynamic, kinematic, static, sensor, or no physics. Physical units, projectiles, loose objects,
  structures, bridges, props, and watercraft use Bepu as applicable; drawable-only effects,
  script markers, and other objects with no physical role remain outside the solver. Bepu owns
  solid contact generation/response while `PartitionCellManager` retains gameplay queries and
  non-physical regions. Each `GameEngine` receives one backend through injected configuration;
  the proof-of-concept process-global selector is retired. Migration is incremental by category
  inside Bepu worlds, with the current `PhysicsBehavior` Euler integrator retained as the reference
  backend. Bepu becomes the default only after the F4a cross-game classification and scenario
  matrix passes.
  Gameplay-decisive reads may consume contact state and are validated by invariants and loose
  authored bounds. Design: [physics-seam plan](plans/physics-seam.md). Affects
  [Track F](track-f-terrain-destruction.md), [Track E](track-e-naval-warfare.md),
  [Track B](track-b-scale.md), and [Track H](track-h-storage-formats.md).
- Rendering clarity & image quality scope, Decision: own track (Track G), not folded into
  [B5 rendering at scale](track-b-scale.md), and not gated on Track P. The current renderer is
  faithful to the original game but grainy on high-resolution displays, with a screen-door pattern
  on alpha-tested geometry. The causes span a genuine correctness bug (no
  `SDL_WINDOW_ALLOW_HIGHDPI`, so the swapchain is sized from logical points and the frame is
  rendered below native resolution and resampled), plus absent capability across antialiasing,
  cutout filtering, colour space, texture filtering, shadow provisioning, and 2D scaling. B5 is
  about throughput at entity scale, so image quality would be a grab-bag inside it, the same
  reasoning that split Tracks D, E, and F out. Track G touches presentation only and changes no
  simulation behaviour, so unlike Tracks A/B/C it does not need a playable skirmish loop first.
  Named "Track G" because D, E, and F are all merged on `main`
  ([Track D](track-d-gameplay-expansion.md), [Track E](track-e-naval-warfare.md),
  [Track F](track-f-terrain-destruction.md)). Affects
  [Track G](track-g-rendering-clarity.md).
- Original-look compatibility under Track G, Decision: two presets, `Legacy` and `Enhanced`,
  with `Enhanced` as the default and the target of future work. `Legacy` pins sample count 1,
  the hard `0x60` alpha-test cutoff, gamma-space shading, 4x anisotropy, and a single 1024²
  shadow cascade, reproducing the pre-track output exactly; the G1 high-DPI fix applies to both
  because it is a correctness bug rather than a look change. Presets are a settings layer rather
  than an [experiment seam](seams.md): they select fixed pipeline state, not swappable
  implementations measured by the A4 harness, so they stay out of that harness. The alternative,
  a single unconditional visual upgrade, was rejected because reproducing the original look is a
  standing requirement, and the alternative of   a full per-feature settings matrix without presets
  was rejected as untestable: G3 golden frames are captured per preset. Affects
  [Track G](track-g-rendering-clarity.md).
- Terrain grain filtering, Decision: precompute filtered terrain mip chains as source-hashed
  sidecar data instead of paying for per-pixel runtime supersampling. Original game files remain
  unchanged. Policy and validation: [G7 offline texture packs](implementation/track-g-g7-texture-filtering.md#offline-terrain-texture-packs).
- Realistic proportions scope. Decision: presentation-only scale correction in Track G (G10),
  grounded in a measured reference scale; weapon ranges and collision geometry stay untouched.
  Measured against installed assets (INI `Geometry` cross-checked with W3D mesh bounds, FX meshes
  excluded), infantry, ground vehicles, and civilian/faction buildings are already mutually
  proportionate at roughly 5 world units per meter (terrain cell 10 units ≈ 2 m); aircraft models
  are the offenders, undersized roughly 2-2.5x versus real counterparts (MiG ≈ 6.6 m in-game
  versus ≈ 17.3 m real). G10 documents the reference scale and adds an opt-in visual model-scale
  correction for the aircraft class behind the G2 settings layer, in the spirit of the `Enhanced`
  preset. Attack-range compression (≈ 100x, e.g. `CrusaderTankGun AttackRange = 150` ≈ 30 m
  versus 2.5+ km real) is deliberate gameplay and map-scale data rather than presentation, so it
  is explicitly out of scope, as are `Geometry` collision boxes (selection/pathing consumers such
  as `Scene25D.GetBoundingSphere`). Chosen over rescaling assets (mutates original data) and over
  simulation-side scaling (would change gameplay and break the A3 seam). Affects
  [Track G](track-g-rendering-clarity.md) (G10).
- Map & unit storage-format scope. Decision: own track (Track H), scoped to on-disk persistence
  and schema design, not runtime performance. A new storage system for maps and unit/object
  definitions is a genuine gap: no roadmap phase designs a successor on-disk format for the
  `.map` file or per-game INI `ObjectDefinition` data. This is deliberately kept separate from
  [B3](track-b-scale.md) (data-oriented runtime layout for unit/`GameObject` state) and
  [B4a-5](deep-dives/b4a-larger-maps.md) (chunked terrain runtime storage for scale), which
  already own the in-memory/streaming side of "storage" for units and maps respectively. Track H
  instead answers a different question: what does content look like on disk, how does its schema
  version and evolve, and how do maps and object definitions get authored, converted, and
  validated against that schema. B4a-5's "extend or replace the `.map` format" milestone stays
  scale-driven (chunking so the whole grid need not be resident); Track H's H3 designs the
  general-purpose container/versioning envelope and reserves a terrain section for B4a-5's
  chunk representation, so the two efforts converge on one map format rather than competing.
  Split out as its own track for the same reason as Tracks D/E/F/G: it cuts across terrain
  storage (B4a-5), the external-asset import pattern ([C1](track-c-external-assets.md)), the
  modding/content pipeline ([Track D](track-d-gameplay-expansion.md),
  [ModCollector](plans/mod-collector.md)), and authoring tooling (WorldBuilder), rather than
  fitting inside any single existing track. Named "Track H" because A–G are already assigned on
  `main`. Its hard dependency is Track P (P7); coordination with F4a concerns optional
  engine-level physics fields and does not gate the format seam. Affects
  [Track H](track-h-storage-formats.md).
- HUD rework scope. Decision: replacing the pre-rendered HUD is in scope for G9, as an additional
  work item rather than a new track or phase. G9's original scope (native-resolution 2D,
  logical-point layout, better upscaling) makes the legacy 800x600 WND/APT bitmaps look as good as
  upscaling allows, but the HUD remains pre-rendered art. G9-3 replaces the control bar's bitmaps
  with dynamically drawn equivalents (vector/procedural panels, buttons, frames; live text), which
  is the genuine resolution independence the phase is named for. Chosen over a separate UI track
  because the work shares G9's whole substrate (the 2D layer, logical-point layout, the G2 preset
  switch, and the G3 golden frames that gate it) and has no scope beyond rendering clarity; a
  UI-framework or UX redesign stays out of scope and would be new roadmap material if ever
  wanted. Gated behind the `Enhanced` preset with `Legacy` pixel-identical to the pre-rework
  capture, consistent with the rendering-clarity scope Decision. Affects
  [Track G](track-g-rendering-clarity.md) (G9).
- Neural-network AI policy v1 action and feature scope. Decision: v1 ships a 9-feature
  input, 9-8-5 tanh/softmax network over a five-action catalog (none, supply build, next
  structure, combat unit, attack) with descoped gather reassignment and no policy RNG, per
  [neural-network-ai-policy.md](deep-dives/neural-network-ai-policy.md). Expanding the
  catalog or feature vector (enemy economy estimates, spatial targeting, special powers) is
  deferred until the evolutionary training harness produces fitness results that justify
  the added genome size; until then the v1 shapes are the stable genome contract.
  Affects [Track A](track-a-foundations.md) (A4).
- RTS topology description scope. Decision: the RTS domain adapter returns an empty
  <c>DescribeTopology</c> result for now; RTS visualization is position-driven and no current
  consumer (A5 or B6) needs occupied-cell adjacency. Revisit when one does; the contract
  already carries the attachment point. Affects
  [Track A](track-a-foundations.md) (A4-1).
- Per-rank hierarchical fitness signal. Decision: v1 hierarchical co-evolution uses shared
  outcome credit only (every rank in a match receives the flat scorer's match fitness). Per-rank
  shaped fitness, such as a commander's squad survival rate, requires per-rank T0 run-output
  metrics that do not exist yet; when they land, the co-evolution contract grows per-rank credit
  assignment without changing the schedule. Affects
  [Track A](track-a-foundations.md) (A4-3f).
- Mixed-roster mass battle scope. Decision: own track (Track M), on the RTS domain and independent
  of Track W. The battle is AI versus AI, 1000 ground and air combat units per side,
  pre-deployed with fixed airfields and no economy or production. Batch runs are headless; because 2000 units sit well inside the near-term
  scale anchor, a live real-time windowed view is also a milestone, not only replay. The two sides
  field different mixed Generals and EE2 rosters of equal points (see the points Decision below); a
  mirrored run serves only as a map-bias control. Named "Track M" because no branch reserves M. Affects
  [Track M](track-m-mass-battle.md).
- Mass battle map source. Decision: map selection is the first phase (M0), so the capacity probe and
  every later scenario run on the real battlefield. Evaluate operator-supplied community Generals maps first and
  author a map with B4a only if none meets the requirements in the
  [deep dive](deep-dives/mass-battle-1k.md#4-map). Community maps are never committed. Deployment
  zones are scenario data, so downloaded maps stay unmodified. Affects
  [M0](track-m-mass-battle.md).
- EE2 gameplay definitions. Decision: a bounded `.ddf` reader and translator in M2 emits scoped
  `ObjectDefinition`s for ground combat units and aircraft; M3 normalizes model scale, tile distances, and `rps`
  counter classes through authored, recorded factors and tables. Affects
  [M2, M3](track-m-mass-battle.md).
- Mass battle fairness. Decision: armies are asymmetric and balanced by points, not mirrored.
  Generals `BuildCost` and EE2 `dbtechtreenode.csv` resource costs convert to one points scale
  through authored per-resource rates; both 1000-unit manifests total equal points within a recorded
  tolerance. Unit stats are not tuned for balance. Affects [M3, M5](track-m-mass-battle.md).
- Multi-unit pathfinding ownership. Decision (2026-10-22): the cost of multi-unit path requests is
  its own track, [Track N](track-n-pathfinding-performance.md), not a B4 reopening. B4 closed on
  request rate; M4 showed per-order cost (912 full searches on one decision tick, 664 from blocked
  followers). Track N builds no hierarchical layer; its only new structure, a shared-destination
  goal field, is gated on measurement after request sharing. Behavior-changing options default off
  until N5 evidence and an operator decision. The no-hierarchy clause is superseded by the layered
  navigation decision (2026-10-28).
- Terrain impact decals. Decision (2026-10-26): persistent scorch marks are Track G phase G11, not
  Track F; F keeps crater geometry and heightmap deformation and reuses G11's decal store for
  crater textures. Decals are a terrain overlay mesh following the original height-map grid, not
  per-pixel projected decals like `RadiusCursorDecals`, so hundreds of marks cost one draw.
  Runtime decals are not saved, matching the original. See the
  [deep dive](deep-dives/g11-terrain-impact-decals.md#3-design).
- Navigation deadlocks. Decision (2026-10-26): deadlock work extends Track N as N6 to N10 rather
  than a new track, because it shares N's measurement, options, and scenarios and N5's
  default-flip decision waits on it. Resolution is discrete (wait, friendly give-way, bounded local
  repath, destination slots, crowd arrival), matching the ported original-engine fields; continuous
  local avoidance stays out because it changes every move and every baseline. The global grid stays
  unit-free. New behavior options default off until N10. Continuous steering is later added behind
  an option by the layered navigation decision (2026-10-28), with this discrete resolution as its
  fallback. See the
  [deep dive](deep-dives/n-navigation-deadlocks.md#5-decisions).
- Confined passage queueing. Decision (2026-10-27): proactive queueing at narrow passages extends
  Track N as N11 to N14 and complements N8's reactive resolution; queued units report `Queued` so
  N8 leaves them alone. Passages are detected from cross-sections along each route, not from a
  precomputed choke graph, and the grid stays point-sized. Ad hoc passage groups are
  navigation-transient, not `SquadHierarchy` squads. Ordering is deterministic on the logic thread.
  `PassageQueueing` defaults off until N14. See the
  [deep dive](deep-dives/n-confined-passage-queueing.md#5-decisions).
- Layered navigation. Decision (2026-10-28): Track N extends as N15 to N20 to split navigation into
  strategic, main-path, local-route, and steering layers with explicit ownership of a per-unit
  `NavigationPlan`. The strategic layer is a fixed-size sector-and-portal graph over the existing
  grid with local rebuilds, not a navigation mesh; flow fields stay closed (N3). This supersedes the
  "no hierarchical layer" clause of the Track N ownership decision: B4's gate measured request
  latency, while the layers serve route scope, group cohesion, and local replanning. Continuous
  reciprocal steering becomes the lowest layer behind `UnitSteering`, with N8's discrete
  resolution as fallback. Navigation groups are transient and carry no AI intent. New options
  default off until N20. See the [deep dive](deep-dives/n-layered-navigation.md#6-decisions).
- Real-world terrain scope. Decision (2026-10-31): geospatial map generation moves out of Track B
  (phase B4b) into its own track, [Track R](track-r-real-world-terrain.md), as R1 to R5. It is a
  content and authoring pipeline rather than scale work, and new source types or generation stages
  take further R phases instead of growing a Track B sub-phase. Card IDs, sub-phase labels, the
  deep dive, and the plan keep their B4b names to avoid renaming every reference. W3a now depends
  on R2, the standalone payload its cards already consume, instead of the whole former B4b.
