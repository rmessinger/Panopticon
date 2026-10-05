# Task Card `WI-H2a-05`: Resolve typed content dependencies

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-05` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-04` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Produce a resolved dependency closure using explicit namespace and dependency-kind rules.

## Context to read (and only this)

- H2a deep dive: dependency and namespace contracts.
- Cards 03/04's landed contracts and resolver.
- `src/Panopticon.Simulation/Content/LazyAssetReference.cs`, `AssetHash.cs`.

## Steps

1. Resolve unit, weapon, armor, locomotor, projectile, ability, upgrade, production, and visual references in the reviewed kinds.
2. Validate cross-source imports and duplicates before runtime hash-based registration.
3. Test cross-file/forward references, missing/wrong-kind/ambiguous targets, and legal versus forbidden cycles.

## Files likely to change

- Proposed `src/Panopticon.Simulation/Content/Importing/Units/UnitDependencyResolver.cs`.
- Proposed `test/Panopticon.Simulation.Tests/Content/Importing/Units/UnitDependencyResolverTests.cs`.

## Acceptance criteria

- [ ] Every required reference has one typed target or a blocking source-attributed diagnostic.
- [ ] Identical local names from two sources remain distinct; cross-source access is explicit.
- [ ] Legal recursive content references are not confused with forbidden inheritance cycles.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

No source discovery, game simulation, or runtime registration. Names are reconciled before Ready.

## On completion

Update Status and H2a-5, reconcile card 06, and append validation to `.exchange`.
