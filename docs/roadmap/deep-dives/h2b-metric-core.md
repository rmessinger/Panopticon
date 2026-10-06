# H2b: Metric core units

> Audience: human + agent.
> Internal metric conversion after the complete H2a group is merged.

## Contract

Runtime positions, lengths, bounds, and translations use meters. Authored rates use meters per second; existing tick-based code uses meters per logic frame and meters per logic frame squared. Cadence, angles, health, damage, costs, ratios, and dimensionless animation scales remain unchanged. This is a numeric rescale, not a balance or physics redesign.

The Generals distance convention is one foot: `s = 0.3048` meters per legacy unit. Length, velocity, and acceleration scale by `s`; area and squared-distance thresholds by `s²`; volume by `s³`; inverse-length quantities by `1/s`. Gravity preserves the converted source value, not an assumed terrestrial value. Mass and force fields require evidence of physical dimensions and source units; gameplay weights are not automatically kilograms.

Raw format readers retain source-space values. Conversion belongs at the source-to-engine boundary and is inverted when writing legacy formats. Map cells, sample indices, pixels, UVs, normals, rotations, and ratios are not lengths. Runtime interchange is metric unless explicitly carrying a tagged source quantity. No conversion in generic vector or float parsing.

H2a `Quantity` remains tagged until materialization. Generals adapters reading normalized native definitions tag them as metric, not legacy feet. `GameDistance` and unqualified rate tags require explicit migration semantics. EE2 source distance uses declared meters per source unit; do not infer that tiles are meters or reuse model scales as gameplay scales. Native legacy bindings must neither bypass normalization nor be scaled twice.

glTF meter coordinates remain meters. NIF retains its evidenced axis conversion without meters-to-feet expansion. W3D converts source distances at import, including bind and animation translations. Source-specific presentation sizing stays separate from unit conversion.

## Starting point and release gate

Planning base: `74e6517fe`, the stacked H2a mixed-source tip. H2a is not considered merged or complete by this plan. Card 01 must ground its evidence in the final merged H2a result, including outstanding staged Generals registration (`WI-H2a-19` where applicable).

Use `staging/track-h-h2b` after H2a merges. Per-card commits and result lines, grouped PRs into staging, then a final staging-to-main PR. All PRs remain open for the user to merge. Partial migration is not a supported release. Keep the cutover on staging until every surface passes. Do not add a long-lived dual-unit runtime.

The first two cards are read-only audit/design work. Source-edit cards remain Draft until exact files, field dimensions, tests, and grounded diagrams are pinned. Split any card exceeding S effort. No broad replacement or speculative conversion.

## Audit deliverables

Card 01 creates `h2b-metric-inventory.md`: file:line citations, source/runtime/wire units, conversion owner, inverse writer, dimensional exponent, applicable games, and test per field or constant. Cover INI including BFME variants, H2a quantities and legacy bindings, maps/WorldBuilder, W3D/glTF/NIF and any additional live importers, particles, physics, navigation, partitioning, cameras, shaders, audio attenuation, scripts, orders/network, replays, saves, checkpoints, recording, scenarios, geospatial export, inspectors, and captures. Record dormant paths separately; do not invent unsupported importers.

Card 02 creates `h2b-metric-execution.md`: exact scope per card, quantity migration table, schema compatibility matrix, old/new numeric fixtures, tolerances, staging groups, and release checklist. Existing recorded files require a documented read/migrate/reject outcome. Never silently reinterpret an unversioned legacy payload as meters.

## Architecture Diagrams

Gate: pending cards 01 and 02. Card 02 replaces this section with grounded class and sequence diagrams naming actual parsers, adapters, materializers, consumers, and serializers. Show normalization once on read and the inverse on legacy write, including H2a native bindings. No source-edit card is Ready before this gate is populated.

## Card sequence

| Card | Scope | Prerequisites |
|---|---|---|
| [WI-H2b-01](../agent/cards/WI-H2b-01-unit-boundary-audit.md) | Merged H2a baseline and dimensional audit | H2a merged |
| [WI-H2b-02](../agent/cards/WI-H2b-02-cutover-design.md) | Design and bounded scopes | 01 |
| [WI-H2b-03](../agent/cards/WI-H2b-03-conversion-primitives.md) | Explicit conversion helpers | 02 |
| [WI-H2b-04](../agent/cards/WI-H2b-04-h2a-quantities.md) | H2a adapters and materializers | 03 |
| [WI-H2b-05](../agent/cards/WI-H2b-05-ini-gameplay.md) | Gameplay INI lengths and rates | 03, 04 |
| [WI-H2b-06](../agent/cards/WI-H2b-06-ini-presentation.md) | Presentation INI and globals | 05 |
| [WI-H2b-07](../agent/cards/WI-H2b-07-terrain.md) | Terrain spacing and elevation | 03 |
| [WI-H2b-08](../agent/cards/WI-H2b-08-map-placement.md) | Placement and WorldBuilder | 07 |
| [WI-H2b-09](../agent/cards/WI-H2b-09-w3d.md) | W3D models and animation | 03 |
| [WI-H2b-10](../agent/cards/WI-H2b-10-gltf-nif.md) | Metric-native importers | 09 |
| [WI-H2b-11](../agent/cards/WI-H2b-11-physics.md) | Physics and locomotion | 05, 07, 09 |
| [WI-H2b-12](../agent/cards/WI-H2b-12-spatial.md) | Navigation and scripts | 08, 11 |
| [WI-H2b-13](../agent/cards/WI-H2b-13-render-audio.md) | Cameras, rendering, audio | 06, 08, 10 |
| [WI-H2b-14](../agent/cards/WI-H2b-14-orders-replays.md) | Orders, network, replays | 08, 12 |
| [WI-H2b-15](../agent/cards/WI-H2b-15-persistence.md) | Saves and recording versions | 11, 14 |
| [WI-H2b-16](../agent/cards/WI-H2b-16-tools-scenarios.md) | Tools and scenarios | 04, 12, 13, 14, 15 |
| [WI-H2b-17](../agent/cards/WI-H2b-17-boundary-conformance.md) | Compatibility tests | 16 |
| [WI-H2b-18](../agent/cards/WI-H2b-18-release-proof.md) | Real-content release proof | 17 |

Numbers denote `WI-H2b` cards. Intermediate commits are not complete metric gameplay builds.

## Validation contract

Use explicit converted expectations: a legacy 10-unit cell becomes 3.048 m; a 100-unit range becomes 30.48 m; 30 legacy units/s becomes 9.144 m/s and 0.3048 m/frame at 30 Hz. A metric 1 m model stays 1 m. Cover negative coordinates, map-scale precision, squared thresholds, bind/animation alignment, inverse writes, metric-source idempotence, and native-binding paths.

Before cutover, capture final-H2a Generals and EE2 P9 evidence: paths, shots, durations, outcomes, and map/model screenshots. Compare metric positions to old positions multiplied by `s` with justified tolerances. Seconds, damage, order meaning, and visual framing remain equivalent. Floating-point rescaling can affect branches; tolerance does not excuse systematic behavior changes.

Preserve artifacts and stop on determinism or golden byte-identity deviations. Report the assertion and likely cause and await user determination before any baseline update. Same-seed determinism is not a new migration requirement. Approved metric baselines are versioned and explained; old artifacts remain identifiable.

Every source card runs these standard gates plus the targeted commands pinned by card 02:

```sh
dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

ReplaySketch changes require screenshot-mode proof of both map and model panels per `docs/screenshots.md`. Planning changes require links, dependencies, and Mermaid checks only.
