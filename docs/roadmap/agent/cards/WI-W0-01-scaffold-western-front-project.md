# Task Card `WI-W0-01`: Scaffold the Western Front project and stable keys

| Field | Value |
|---|---|
| Card ID | `WI-W0-01` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Create `Panopticon.Simulation.WesternFront` with stable-key helpers and enums every later W0 card uses.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §2-§3 and §4.2.
- `src/Panopticon.Mods.BuiltIn/Panopticon.Mods.BuiltIn.csproj`: minimal project file pattern.
- `src/Panopticon.Simulation/AssemblyInfo.cs`: `InternalsVisibleTo` list.
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`: project references.

## Steps

1. Create `src/Panopticon.Simulation.WesternFront/Panopticon.Simulation.WesternFront.csproj` and add it with `dotnet sln src/Panopticon.sln add src/Panopticon.Simulation.WesternFront`.
2. Add `[assembly: InternalsVisibleTo("Panopticon.Simulation.WesternFront")]` to `src/Panopticon.Simulation/AssemblyInfo.cs`.
3. Reference the new project from `Panopticon.Simulation.Tests`.
4. Add enums `Echelon` (Army 0, Corps 1, Division 2, Brigade 3, Battalion 4, Company 5, Platoon 6), `FidelityLevel` (Aggregate 0, Operational 1, Tactical 2), `ClockMode` (Compressed 0, Tactical 1), and `SystemCadence` (Campaign, Operational, Tactical).
5. Add `WesternFrontKeys` helpers for every §3 key format, including platoon-depth formations and `soldier:` keys.
6. Add `FormationKey` exposing side, path segments, parent key, and echelon from path depth.
7. Add tests covering each format, parent derivation, ordinal ordering, and malformed-key rejection.

## Files likely to change

- New `src/Panopticon.Simulation.WesternFront/` project
- `src/Panopticon.sln`
- `src/Panopticon.Simulation/AssemblyInfo.cs`
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontKeysTests.cs`

## Acceptance criteria

- [ ] `FormationKey.Parent` of `formation:blue/a1/c1/d1/b2/bn1/co3/pl2` is `formation:blue/a1/c1/d1/b2/bn1/co3` and its echelon is `Platoon`.
- [ ] `soldier:blue/a1/c1/d1/b2/bn1/co3/pl2/0042` parses and round-trips.
- [ ] Malformed keys throw `FormatException`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontKeys"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No state, topology, modules, or domain adapter.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
