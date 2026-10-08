# Task Card `WI-H2a-19`: Stage Generals INI unit registration

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-19` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md): unit-content import layer |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `M` |
| Prerequisites | `WI-H2a-13` |
| Diagrams | [Architecture diagrams](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Register Generals unit content only through the publisher, removing parse-time registration of objects, weapons, armor, locomotors, and upgrades.

## Context to read (and only this)

- H2a deep dive: Generals reference adapter notes (G5).
- `src/Panopticon.Simulation/Data/Ini/IniParser.BlockParsers.cs`: unit block registration.
- `src/Panopticon.Simulation/Logic/Object/ObjectDefinition.cs:18-52`: `DefaultThingTemplate`, child, and reskin parent lookups at parse time.
- `src/Panopticon.Simulation/Content/Importing/Units/Generals/` and `Materialization/UnitContentPublisher.cs`.

## Steps

1. Give the INI loader a staging lookup that child, reskin, and default-template parsing read before the store.
2. Route the five unit block kinds into staging; publish the staged closure with `UnitContentPublisher.Publish` into the root scope instead of `Adopt`.
3. Keep lazy references by name so they resolve after publication; keep load order and first-wins semantics.

## Files likely to change

- INI block registration and the Generals adapter.
- `UnitContentPublisher` root-scope publication.
- Existing Generals adapter and parity tests.

## Acceptance criteria

- [x] No unit block reaches an `AssetStore` collection except through the publisher.
- [x] Generals and Zero Hour boot with zero import diagnostics; parity tests and P9 suites pass unchanged.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~GeneralsUnit"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No change to non-unit INI subsystems or map loading. Stop on determinism deviations under repository policy.

## On completion

Update Status and H2a-19, and append validation to `.exchange`.
