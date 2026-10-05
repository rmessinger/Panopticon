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

`WI-H2a-01` adds a file:line loading audit here. `WI-H2a-02` specifies the contract, capability
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
| [WI-H2a-01](../agent/cards/WI-H2a-01-unit-loading-audit.md) | Focused loading audit | None | Ready |
| [WI-H2a-02](../agent/cards/WI-H2a-02-unit-import-design.md) | Contract and grounded architecture | 01 | Draft |
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
