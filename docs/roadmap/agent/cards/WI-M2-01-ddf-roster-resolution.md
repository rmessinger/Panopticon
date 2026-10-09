# Task Card `WI-M2-01`: EE2 roster and DDF resolution

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-M2-01` |
| Roadmap phase | [M2](../../track-m-mass-battle.md): EE2 combat units |
| Status | `Done` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | H2a, C4d, P9 complete |
| Diagrams | [M2 import pipeline](../../deep-dives/mass-battle-1k.md#9-architecture-diagrams) |

## Goal

Expand H2a's bounded DDF loading to all `db/Units/` files and inherited dependencies. Record an explicit late-epoch reference roster and a capability inventory for the remaining M2 cards.

## Context to read (and only this)

- `docs/roadmap/deep-dives/h2a-unit-content-import.md`: M2 handoff.
- `docs/roadmap/deep-dives/mass-battle-1k.md`: sections 5 and 9.
- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/`: reader, source files, adapter, CSV, policy.
- `src/Panopticon.Simulation/Content/Importing/Units/UnitImportResolution.cs`: inherited records and dependency closure.
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/DdfReaderTests.cs`, `Ee2UnitAdapterTests.cs`, `Ee2RealUnitImportTests.cs`.

## Steps

1. Survey the operator install at `EMPIRE_EARTH_II_PATH`. Inventory every `db/Units/` DDF, parent references, properties, abilities, epoch rows, model bindings, and available patch archives. Store names and diagnostics, not proprietary file contents, in the M2 implementation plan.
2. Author the reference roster: at least one late-epoch infantry, light tank, heavy tank, artillery, fighter, bomber, and helicopter. Record exact unit ID, source path, epoch, region, and visual binding for every entry. Derive names and values from the install; missing classes are blockers, not substitutions.
3. Extend source discovery and the existing reader only for syntax evidenced by the survey. Preserve explicit bounded `DdfPaths` for existing profiles. Discover inherited dependencies deterministically, including dependencies outside `db/Units/` when referenced.
4. Resolve inherited properties and ability overrides for the entire inventory without requiring every discovered unit to be publishable. Materialization and publication must operate on the requested root closure, not unrelated naval or building records.
5. Produce per-unit capability diagnostics. Unknown metadata remains visible; unsupported required gameplay blocks the affected root. Exclusions remain explicit policy entries. Record the supported archive layer and precedence; do not silently mix patch and base values.
6. Add synthetic parser, inheritance, missing-parent, cycle, and closure-isolation tests plus an installed-inventory test. Reconcile WI-M2-02 with the recorded ability keys before making it `Ready`.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/`
- `src/Panopticon.Simulation/Content/Importing/Units/UnitImportResolution.cs` (only if closure isolation requires it)
- `test/Panopticon.Simulation.Tests/Content/Importing/Units/`
- `docs/roadmap/implementation/track-m-m2-ee2-combat-units.md`

## Acceptance criteria

- [x] Every installed `db/Units/` file is parsed and its UnitType inheritance is resolved or reports a precise source-qualified failure.
- [x] The manifest covers all seven reference classes with real IDs and available epoch/model rows.
- [x] Parent overrides retain provenance; missing parents and cycles fail explicitly.
- [x] Unsupported gameplay in an unrelated discovered unit does not reject a supported requested root.
- [x] Unsupported gameplay in a requested root does reject publication; no catch-all exclusion is added.
- [x] Existing bounded Maceman tests remain green; standard validation passes.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
  dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~DdfReader|FullyQualifiedName~Ee2"
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No new translator, gameplay runtime, balance conversion, or proprietary assets in the repository.
- Parsing and resolution coverage do not imply full-game gameplay support.
- If source layering changes authored values, document the decision before implementing it.

## On completion

- Mark `Done`, tick M2-1, and append evidence to `.exchange`.
- Reconcile WI-M2-02 against the inventory; split uncertain runtime design into smaller cards if necessary.
