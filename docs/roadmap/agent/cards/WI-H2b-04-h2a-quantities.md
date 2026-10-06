# Task Card `WI-H2b-04`: Migrate H2a quantities and materializers to metric

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2b-04` |
| Roadmap phase | [H2b](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-H2b-03` |
| Diagrams | [Architecture gate](../../deep-dives/h2b-metric-core.md#architecture-diagrams), populated by `WI-H2b-02` |

## Goal

Make H2a materialization produce metric runtime values from tagged source quantities, with one normalization per field.

## Context to read (and only this)

- `src/Panopticon.Simulation/Content/Importing/Units/Types/QuantityUnit.cs`: `GameDistance` and unqualified rate tags.
- `src/Panopticon.Simulation/Content/Importing/Units/Materialization/UnitConversionPolicy.cs`: `ToGameDistance`, `ToDistancePerFrame`, `ToDistancePerFrameSquared`.
- `src/Panopticon.Simulation/Content/Importing/Units/Generals/GeneralsUnitAdapter.cs`: `AttackRange`, `MinimumAttackRange`, `Speed`, `VisionRange` tagging.
- `src/Panopticon.Simulation/Content/Importing/Units/Ee2/Ee2UnitAdapter.cs`: `SourceDistance` and `SourceDistancePerSecond` fields.
- Card 02 quantity migration table and native-binding policy.

## Steps

1. Apply the card 02 tag semantics: replace or redefine `GameDistance` so legacy feet and metric values cannot be confused.
2. Convert the policy to metric output; EE2 uses a declared meters-per-source-unit scale, never an inferred one.
3. Tag Generals adapter reads per card 02 so native definitions already normalized by INI parsing are not scaled twice.
4. Update materializer tests with explicit converted expectations and add a double-conversion guard test.

## Files likely to change

- Files under `src/Panopticon.Simulation/Content/Importing/Units/` named by card 02.
- Matching tests under `test/Panopticon.Simulation.Tests/Content/Importing/Units/`.

## Acceptance criteria

- [ ] Every distance and rate leaving materialization is metric, with an `AppliedConversion` record.
- [ ] A Generals unit routed through H2a and through native INI yields identical runtime values.
- [ ] EE2 without a declared source scale still fails import rather than defaulting.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Plus the targeted tests pinned by `WI-H2b-02`.

## Out of scope / guardrails

No balancing, no new EE2 fields, no publication changes. Draft until `WI-H2b-02` pins exact files and expectations; split if larger than S.

## On completion

Set Status, tick H2b-4 in the checklist, and append the result to `.exchange`.
