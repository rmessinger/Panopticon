# H2a: Unit-content import layer

> Part of [Track H](../track-h-storage-formats.md). Design scaffold for the shared unit-content
> import prerequisite to M2. No implementation contract is settled until `WI-H2a-02` completes.
>
> Audience: human + agent.

## Scope and ownership

Source adapters emit engine-owned unit-content definitions. A shared resolver applies inheritance,
resolves dependencies, validates capabilities, and materializes native runtime definitions.
Generals INI is the reference adapter; a bounded real EE2 ground-combat slice proves the second source.
The pipeline must not implement an independent simulation or require INI text generation.

H2a runs next before M2. It does not depend on H1's storage audit, H2's map interchange, or H3's
container. P9 supplies gameplay validation and C4d supplies the EE2 visual path. M2 expands the
EE2 roster and aircraft behavior. M3 owns cross-game balance and points. H4 adds storage over the
same representation. Track D owns new tech-tree/era mechanics, not implicit importer fallbacks.

## Required contract

- Gameplay: body/health, geometry, movement, weapons, armor, targeting, abilities, costs,
  production, prerequisites, and upgrades.
- Presentation: model, material, animation/state, attachment, effect, and sound references.
- Resolution: source-qualified typed identities, provenance, inheritance, cross-file references,
  declared dependencies, explicit overrides, capability requirements, and diagnostics.
- Conversion: declared coordinate, distance, speed, time, and resource semantics. Source values
  and applied conversions remain inspectable; no implicit cross-game rebalance.
- Publication: validate the complete required dependency closure before registering content;
  errors leave no partial content. Unload preserves built-in definitions and reference validity.

Source syntax and live runtime instances must not leak into interchange. Existing native module
coverage must remain intact for Generals, including defaults, child/reskin objects, and map overrides.
The design audit must decide how engine-owned module data preserves that coverage without exposing
source-specific parser objects or hiding unsupported mechanics in opaque payloads.

Unknown metadata can produce warnings only when classified as non-gameplay. Unsupported required
mechanics, unresolved references, invalid numeric values, and collisions block publication. Optional
visual omissions require an explicit policy and a diagnostic. Import acceptance is not a claim that
every source game's mechanics are supported.

## Evidence and design gate

`WI-H2a-01` added the file:line [loading audit](#loading-audit). `WI-H2a-02` specifies the contract, capability
matrix, merge rules, numeric validation, namespacing, override/lifetime rules, and test roster.
Both retain native runtime definitions as the materialization target.

The Generals reference roster must cover the existing six archetypes and supported abilities,
production, upgrades, inherited defaults, and scoped overrides. The EE2 proof selects one real
ground combat unit with resolvable inherited stats and idle/move/attack/death visual states.
If no unit satisfies the available Track C importer capabilities, report the missing capability
and add a reviewed follow-up; do not substitute a different game's gameplay values.

All names and new file destinations in Draft cards are provisional. Before promotion to Ready,
reconcile them with the reviewed design and landed prerequisites. Split any card whose audited
module coverage exceeds a small bounded change. Publish the execution grouping in the existing
staging-branch model; leave every PR open for operator merge.

## Card sequence

| Card | Deliverable | Prerequisites | Initial status |
|---|---|---|---|
| [WI-H2a-01](../agent/cards/WI-H2a-01-unit-loading-audit.md) | Focused loading audit | None | Done |
| [WI-H2a-02](../agent/cards/WI-H2a-02-unit-import-design.md) | Contract and grounded architecture | 01 | Ready |
| [WI-H2a-03](../agent/cards/WI-H2a-03-interchange-contracts.md) | Engine-owned interchange | 02 | Draft |
| [WI-H2a-04](../agent/cards/WI-H2a-04-inheritance-resolution.md) | Inheritance resolution | 03 | Draft |
| [WI-H2a-05](../agent/cards/WI-H2a-05-dependency-resolution.md) | Typed dependency resolution | 04 | Draft |
| [WI-H2a-06](../agent/cards/WI-H2a-06-capability-validation.md) | Capability validation | 05 | Draft |
| [WI-H2a-07](../agent/cards/WI-H2a-07-body-movement-materialization.md) | Body and movement | 06 | Draft |
| [WI-H2a-08](../agent/cards/WI-H2a-08-weapon-armor-materialization.md) | Weapons and armor | 07 | Draft |
| [WI-H2a-09](../agent/cards/WI-H2a-09-ability-materialization.md) | Selected native abilities | 08 | Draft |
| [WI-H2a-10](../agent/cards/WI-H2a-10-production-upgrade-materialization.md) | Production and upgrades | 09 | Draft |
| [WI-H2a-11](../agent/cards/WI-H2a-11-visual-state-binding.md) | Track C visual-state binding | 08 | Draft |
| [WI-H2a-12](../agent/cards/WI-H2a-12-scoped-content-publication.md) | Validated scoped publication | 10, 11 | Draft |
| [WI-H2a-13](../agent/cards/WI-H2a-13-generals-reference-adapter.md) | Generals reference adapter | 12 | Draft |
| [WI-H2a-14](../agent/cards/WI-H2a-14-generals-behavior-parity.md) | Generals parity evidence | 13 | Draft |
| [WI-H2a-15](../agent/cards/WI-H2a-15-ee2-ddf-reader.md) | Bounded EE2 reader | 03 | Draft |
| [WI-H2a-16](../agent/cards/WI-H2a-16-ee2-source-adapter.md) | EE2 interchange adapter | 12, 15 | Draft |
| [WI-H2a-17](../agent/cards/WI-H2a-17-multi-source-validation.md) | Mixed-source P9 proof and handoff | 14, 16 | Draft |

## Loading audit

Result of `WI-H2a-01`. Paths are relative to `src/Panopticon.Simulation/` unless noted; line ranges cite the checkout at the time of the audit.

### Evidence table

| Stage | Behavior | Citation | Obligation for H2a |
|---|---|---|---|
| Entry | `ContentManager` constructor loads subsystems in a fixed order: Core, Audio, Players, ParticleSystems, ObjectCreation, Locomotors, Sciences, Armor, Weapons, FXList, and others. Object parsing runs before locomotors, armor, and weapons, so object references are lazy. | `Content/ContentManager.cs:75-94` | Materialization must not assume dependency order; resolution is by name after all inputs are known. |
| Entry | `UpgradeManager.Initialize` runs after all subsystems load. | `Content/ContentManager.cs:117` | Upgrade materialization needs an explicit post-load step. |
| INI file load | One `IniParser` per file; failures throw out of the constructor and abort the whole load. | `Content/ContentManager.cs:137-144` | Publication must be transactional; the legacy path has no rollback. |
| Subsystem legend | Generals ships no `subsystemlegend.ini`; a retro-fitted legend string supplies `TheThingFactory` (`Default\Object.ini` plus `Data\INI\Object`), `TheUpgradeCenter`, `TheLocomotorStore`, `TheArmorStore`, `TheWeaponStore`. | `Content/SubsystemLoader.cs:13-63`, `:148-154` | The Generals reference adapter reuses this file set and order. |
| Subsystem files | `InitFile` and `InitPath` entries resolve through the composite file system; `InitPath` recurses `*.ini` with an exclusion set. Only `InitFile` and `InitPath` entry kinds are handled. | `Content/SubsystemLoader.cs:369-402` | File ordering is part of override precedence and must be preserved. |
| Defaults | An `Object` block starts from a clone of `DefaultThingTemplate` unless it is that template. | `Logic/Object/ObjectDefinition.cs:18-30`, `Data/Ini/IniParser.cs:676-679` | Interchange must express omitted values distinctly from explicit values so defaults apply at resolution. |
| Child and reskin | `ChildObject` and `ObjectReskin` clone the referenced parent (resolved at parse time via `.Value`) and parse the block over the clone. | `Logic/Object/ObjectDefinition.cs:32-52`, `Data/Ini/IniParser.BlockParsers.cs:59`, `:132-133` | Inheritance needs an explicit parent edge; parse-time forced resolution of a lazy reference means file order matters today. |
| Clone depth | `Clone` is a `MemberwiseClone` plus copies of the kind set, geometry, contact points, module dictionaries, weapon/armor/locomotor sets. The source carries `// TODO: Clone any other lists`. | `Logic/Object/ObjectDefinition.cs:1252-1297` | Resolution must deep-copy every mutable collection; shallow sharing is a known risk. |
| Field dispatch | `IniParseTable` lookups by field name; an unknown field throws `IniParseException`. | `Data/Ini/IniParser.cs:1013-1028` | Today there is no warn-only path; H2a classification of non-gameplay metadata is new behavior. |
| Block dispatch | Top-level block names map to store `Add` calls (`Object`, `ChildObject`, `ObjectReskin`, `Armor`, `Weapon`, `Locomotor`, `Upgrade`). | `Data/Ini/IniParser.BlockParsers.cs:38`, `:59`, `:115`, `:132-133`, `:158`, `:164` | Adapter output kinds map one-to-one to these stores. |
| References | Typed parse helpers return `LazyAssetReference<T>` keyed by name (weapon, armor, locomotor, upgrade, object, FX, and others); resolution is deferred to first `Value` access. | `Data/Ini/IniParser.cs:510-516`, `:541-544`, `:571-580`, `:681-688`; `Content/LazyAssetReference.cs:187-190` | Dependency resolution replaces lazy by-name lookups with a validated closure. |
| Units | Parsers convert at parse time: ms and seconds to logic frames, per-second velocity and acceleration to per-frame values, degrees to radians. | `Data/Ini/IniParser.cs:463-484` | Conversions and source values must stay inspectable; the interchange cannot carry already-converted frames only. |
| Behavior modules | Behavior, Body, Draw, ClientUpdate, and ClientBehavior blocks parse into `ModuleDataContainer` (tag, `ModuleData`, inheritance mode, inherited flag). `ModuleData` has 408 concrete subclasses. | `Logic/Object/ObjectDefinition.cs:283-294`, `Logic/Object/Module.cs:9-64` | Module data is the largest preservation risk; see the unknowns list. |
| Module inheritance | Default-inherited modules are dropped when the child declares a module of overlapping `ModuleKinds`; `AlwaysInherit` and `OverrideableByLikeKind` modes differ; `RemoveModule`, `ReplaceModule`, and `AddModule` edit by tag. | `Logic/Object/ObjectDefinition.cs:441-486`, `:1340-1375`; `Logic/Object/Module.cs:66-76` | The resolver must reproduce these rules for Generals parity. |
| Scopes | `AssetStore` pushes and pops scopes across every collection and singleton; `Game` pushes one root scope before content loads. | `Content/AssetStore.cs:350-373`, `Game.cs:588` | Publication scope is `AssetStore` scope. |
| Identity | Names hash case-insensitively to a `uint` instance id. | `Content/AssetHash.cs:7-9` | Source-qualified identity cannot collide through this hash without a namespace rule; unresolved. |
| Add semantics | `Add` ignores a later definition when the instance id already exists in the top scope (first wins); a new asset gets a monotonically increasing internal id. | `Content/ScopedAssetCollection.cs:134-150` | Collision detection is silent today; H2a must report collisions as errors. |
| Lookup | `GetByName` searches scopes top-down, then calls the on-demand loader if one exists and caches into the top scope. | `Content/ScopedAssetCollection.cs:62-104` | Lookups from a pushed scope see built-ins beneath it. |
| Pop | `PopScope` disposes every asset in the scope, removes it from the list and id map; references held elsewhere are not invalidated. | `Content/ScopedAssetCollection.cs:47-60`; `Content/LazyAssetReference.cs:187-190` | Unload must not leave dangling lazy references; the lazy reference does not cache by design. |
| Map overrides | The only per-map or per-user scope pushes are the temporary `UserMapCache` scope. No Simulation code path loading `map.ini`-style object overrides was found. | `Data/Map/UserMapCache.cs:31`, `:79` | Scoped override coverage for Generals is unverified; see unknowns. |
| Object creation | `GameLogic.CreateObject` builds a `GameObject` from an `ObjectDefinition`, which constructs live modules from stored module data. | `Logic/GameLogic.cs:94-103` | Materialization target is `ObjectDefinition` with its module data. |
| In-memory definitions | `TransportGraphDomainModel` already registers code-built `ObjectDefinition`s with `ObjectDefinitions.Add`. | `Simulation/Transport/TransportGraphDomainModel.cs:104`, `:146` | Precedent for non-INI definition registration. |
| P9 spawning | `ValidationGroundHost` resolves a definition by name and calls `CreateObject` for each placement. | `Panopticon.Tools.TestHarness/ValidationGroundHost.cs:29-33`, `:395-403` | Mixed-source proof reuses this spawn path. |

### Traced example

An `Object` block resolves in this order: file open (`ContentManager.cs:137`), block dispatch (`IniParser.cs:1128`), `DefaultThingTemplate` clone (`ObjectDefinition.cs:20-22`), field parse with lazy weapon, armor, and locomotor references (`IniParser.cs:571-580`, `:541-544`), `Add` into the top scope (`ScopedAssetCollection.cs:134`), and later `GetByName` at spawn (`ValidationGroundHost.cs:395`). Weapon, armor, and locomotor templates parse through their own named blocks (`WeaponTemplate.cs:15-22`, `ArmorTemplate.cs:15-22`, `LocomotorTemplate.cs:11-22`) and are never merged into the object.

### Native data that must survive a source-neutral representation

- Module data: 408 `ModuleData` subclasses carry body, update, die, contain, upgrade, special-power, and draw behavior. The neutral form must hold each family's authored fields or fail explicitly.
- Module identity: tag, `ModuleKinds`, and `ModuleInheritanceMode` drive inheritance and override.
- Weapon sets keyed by condition bits, armor sets in a list, locomotor sets keyed by set type.
- Geometry shapes and contact points; kind-of flags; build cost, time, prerequisites, and upgrade references.
- Lazy references by name, with the first-wins collision behavior of `Add`.

### Unknowns and risks

- Whether every one of the 408 module families is required for the six reference archetypes; the card 02 coverage map decides.
- Map-level object overrides for Generals: no loading path was found in `Panopticon.Simulation`; the owner of any such path is unconfirmed.
- Name hashing is case-insensitive and global per collection; source qualification has no place to live in the current key.
- Shallow clone paths flagged by the in-source TODO may share mutable state between parent and child objects today.
- `LazyAssetReference` does not cache across scope pops; stale references after unload are not yet characterized.
- Unknown-field handling throws; a warn-only mode for non-gameplay metadata does not exist.

### Candidates

Generals archetypes for the reference roster (known support, via P0 six-archetype validation): infantry, vehicle, aircraft, builder, support, superweapon, with USA reference units. Abilities, production, and upgrade cases are selected in card 02.

EE2 ground-combat candidate: not selected. The install holds `db.zip` with ground-unit files such as `heavy_infantry1.ddf` and `heavy_infantry2.ddf`, and C4d proved the skinned British Druid with five bound clips ([C4d](../track-c-external-assets.md)). Unverified: whether any combat unit has resolvable inherited stats and all of idle, move, attack, and death states bound through the available importer capabilities. Source record relationships are in [`EE2_UNIT_VISUAL_PIPELINE.md`](../../system-investigations/EE2_UNIT_VISUAL_PIPELINE.md).

## Architecture diagrams

Design gate: pending `WI-H2a-01` and `WI-H2a-02`. Before any source-edit card becomes Ready,
replace this section with a grounded class diagram and success/failure sequence diagrams.
Show source adapters, engine-owned interchange, resolution/validation, runtime module construction,
Track C visual resolution, `AssetStore` publication, and scope teardown. Name real call sites and
distinguish existing classes from proposed contracts. Include rollback when materialization fails.

## Validation

Synthetic contract/resolver/adapter tests require no proprietary content. Real-install tests declare
their prerequisites explicitly; missing installations never count as successful proof. Use existing
P0/P9 hosts rather than a new harness. Compare authored values and behavioral invariants, not
same-seed simulation identity. Preserve and report determinism-test deviations under repository policy.

Source-edit cards run the standard build/test/format checks and the brace-newline check. ReplaySketch
changes require screenshot-mode verification of both the affected panel and the map panel. Evidence
captures follow `docs/screenshots.md`; no proprietary source assets are committed.
