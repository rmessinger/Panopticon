# Implementation plan: Track H H4 to H7 storage

> Audience: human + agent implementation plan for the H4 unit storage, H5 migration, H6 authoring,
> and H7 regression work. Planning base `2e5d3770` (H2 group 4, PR #326).

Source of truth: [Track H](../track-h-storage-formats.md) phases H4 to H7, with the design scaffolds
[H4](../deep-dives/h4-unit-definition-storage.md), [H5](../deep-dives/h5-legacy-migration.md),
[H6](../deep-dives/h6-authoring-pipeline-integration.md), and
[H7](../deep-dives/h7-validation-roundtrip-regression.md). H3 is the sibling plan
[track-h-h1-h3-map-storage.md](track-h-h1-h3-map-storage.md). Ticket status lives in OpenProject
([ticket workflow](../agent/README.md#ticket-workflow)). This plan creates no tickets and invents no
work package numbers.

## Settled choices

- Container: directory package with `manifest.json` at the root. H3 map packages and H4 unit
  packages are separate formats with separate manifest schemas; shared code is limited to generic
  primitives (JSON writing, SHA-256, safe relative paths) with no format coupling, so either
  format versions independently (H4 D-H4-1, matching the settled H1 decision).
- Unit fidelity: valued unclassified fields are primary. Retained INI payloads apply only to
  legacy-bound records (H4 D-H4-2, D-H4-3).
- Map editor model: `MapFile` stays. H6 converts at the `EditorMap` boundary (H6 D-H6-1).
- Packages are loose directories in v1. In-archive addressing gives a named diagnostic (H6 D-H6-3).
- INI stays a permanent legacy read path. Packages are an export and load format; no forced
  conversion in the INI iteration loop (H6 D-H6-7).
- Conversion copies values verbatim. No Buildability polarity inference (H5 disposition rules).
- Determinism and golden byte-identity failures stop the run for user determination (H7 stop rule).

## Card sequence

Dependency order. Each row lists its prerequisites; a phase gate row lists what must be merged
before the next group starts.

| Card | Phase | Output | Prerequisites |
|---|---|---|---|
| `WI-H3-01..04` | H3 (sibling) | Manifest schema, chunk layers, legacy to package hooks, round-trip tests. Status Draft. | H2-01 (done) |
| `WI-H4-01` | H4 | Schema and record-fidelity design gate | H2a merged |
| `WI-H4-02` | H4 | Package reader implementing `IUnitSourceAdapter`; valued unclassified fields | H4-01 |
| `WI-H4-03` | H4 | Package writer from resolved records | H4-02 |
| `WI-H4-04` | H4 | Physics fields and forward-compatibility proof (gated on AQ-H4-1) | H4-03 |
| `WI-H4-05` | H4 | Bounded INI parity proof | H4-03, H4-04 |
| `WI-H5-01` | H5 | Legacy map conversion (`convert-map`) | H3-01..04 merged; retained-section and companion manifest slots decided |
| `WI-H5-02` | H5 | Legacy INI conversion (`convert-ini`) | H4-02, H4-03, H4-04 |
| `WI-H5-03` | H5 | Round-trip validator and report | H5-01, H5-02 |
| `WI-H5-04` | H5 | Retained-backend integration for converted maps | H5-01; H3 package reader |
| `WI-H5-05` | H5 | Behavioral equivalence harness | H5-03, H5-04 |
| `WI-H6-01` | H6 | WorldBuilder package open and save boundary conversion | H5-01, H5-04 |
| `WI-H6-02` | H6 | WorldBuilder package edit round trip, capture evidence | H6-01 |
| `WI-H6-03` | H6 | Staged pack loading through H4 adapter and AssetStore scopes | H4-02, H4-03, H5-02 |
| `WI-H6-04` | H6 | INI edit and reload preservation check | H4-02 |
| `WI-H7-01` | H7 | Map round-trip regression suite | H5-01, H5-03, H6-01 |
| `WI-H7-02` | H7 | Unit round-trip regression suite | H4-05, H5-02, H5-03 |
| `WI-H7-03` | H7 | ReplaySketch new-format scenario proof | H5-05, H6-03 |
| `WI-H7-04` | H7 | Fault-injection catch proof | H7-01, H7-02 |

## Phase gates

Gates are merge conditions, not start conditions. A gate closes when the named card is merged.

| Gate | Blocks | Condition |
|---|---|---|
| G1. H4 schema gate | H5-02 and all H4-02 to H4-05 | `WI-H4-01` review confirms D-H4-1 through D-H4-7 (amended if needed). |
| G2. H3 packages | H5-01, H5-04, H6-01 | `WI-H3-01..04` merged, including the manifest companion and retained-section slots. |
| G3. Retained-backend integration | Any playable claim (H5 milestone, H6-03 pack play, H7-03 playable claim) | `WI-H5-04` merged; the playable claim is made only by `WI-H5-05` and `WI-H7-03`. |
| G4. Track H H5 gate | All H6 cards | Track doc dependency: H5 complete. `WI-H6-04` has no code dependency on H6-01 to H6-03. |
| G5. Validator gate | H7-01, H7-02 | `WI-H5-03` merged, so the suites share one comparer. |

## Acceptance by phase

| Phase | Acceptance |
|---|---|
| H4 | A package-authored unit loads with full parity to its INI-authored equivalent (`WI-H4-05`). Adding an optional field does not break older packages. No BepuPhysics types or handles appear in package code (grep gate in `WI-H4-04`). |
| H5 | A converted legacy map and unit roster load through the new formats and play identically to the originals under the P0 assertion layer (`WI-H5-05`). Silent section drops fail conversion and load (`WI-H5-04`). |
| H6 | WorldBuilder authors, saves, and reloads a package map (`WI-H6-01`, `WI-H6-02`). A staged pack's object definitions resolve alongside built-in content and unload cleanly (`WI-H6-03`). The INI loop touches no package (`WI-H6-04`). |
| H7 | The suite catches an injected mismatch for maps and unit definitions (`WI-H7-04`). A ReplaySketch scenario using new-format content passes the same headless assertions as its legacy equivalent (`WI-H7-03`). |

## Evidence

- Map and unit round-trip and parity tests: standard headless gates, real-install variants prefixed
  with `CNC_GENERALS_PATH` as their scopes state.
- WorldBuilder Avalonia behavior (`WI-H6-02`): stills and a save-reload GIF under
  `/mnt/pit2/Network Storage/temp/`, per [screenshots.md](../../screenshots.md).
- ReplaySketch changes (`WI-H7-03`): screenshot-mode proof of map and model panels per
  [AGENTS.md](../../../AGENTS.md#validation).
- Behavioral and fight evidence: GIFs from the `ScenarioCapture` recipe.

## Coordination notes

- `WI-H3-01` owns the manifest. The retained-section slot (H5) and companion slot (H6 D-H6-4) are
  agreed with it before `WI-H5-01` or `WI-H6-01` starts.
- AQ-H4-1 (F4a participation vocabulary) gates `WI-H4-04`. Track F coordination is in
  [track-f-terrain-destruction.md](../track-f-terrain-destruction.md).
- AQ-H6-2 (staged pack contract with Track D) gates the pack path in `WI-H6-03`. ModCollector remains
  external Track D scope ([mod-collector.md](../plans/mod-collector.md)).
- H2b metric cutover can land between H4 and H5. Packages store declared tags and never rescale
  (H4 D-H4-6), so the cutover does not invalidate these cards.
- Track doc phase-resource rows for H4 to H7, the implementation checklist, and the roadmap tree
  need updating to match this plan. That reconciliation happens outside this change set.
