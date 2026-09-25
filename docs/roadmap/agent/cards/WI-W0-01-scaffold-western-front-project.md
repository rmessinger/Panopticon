# Task Card `WI-W0-01`: Scaffold the Western Front project and stable keys

| Field | Value |
|---|---|
| Card ID | `WI-W0-01` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Create `Panopticon.Simulation.WesternFront` with the stable-key helpers and enums every later W0 card uses.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §2-§3 and §4.2 (enum values).
- `src/Panopticon.Mods.BuiltIn/Panopticon.Mods.BuiltIn.csproj`: minimal project file pattern.
- `src/Panopticon.Simulation/AssemblyInfo.cs`: `InternalsVisibleTo` list.
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`: project references.

## Steps

1. Create `src/Panopticon.Simulation.WesternFront/Panopticon.Simulation.WesternFront.csproj`
   (`Nullable` enabled, reference `Panopticon.Simulation`) and add it with
   `dotnet sln src/Panopticon.sln add src/Panopticon.Simulation.WesternFront`.
2. Add `[assembly: InternalsVisibleTo("Panopticon.Simulation.WesternFront")]` to
   `src/Panopticon.Simulation/AssemblyInfo.cs`.
3. Reference the new project from `Panopticon.Simulation.Tests`.
4. Add enums `Echelon` (Army 0, Corps 1, Division 2, Brigade 3, Battalion 4), `FidelityLevel`
   (Aggregate 0, Operational 1, Tactical 2), `ClockMode` (Compressed 0, Tactical 1), and
   `SystemCadence` (Campaign, Operational, Tactical).
5. Add `WesternFrontKeys` static helpers that build and parse every §3 key format, plus
   `FormationKey` (record struct over the key string exposing side, path segments, parent key, and
   echelon from path depth).
6. Add `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontKeysTests.cs` covering each format,
   parent derivation, ordinal ordering, and rejection of malformed keys.

## Files likely to change

- New `src/Panopticon.Simulation.WesternFront/` project
- `src/Panopticon.sln`, `src/Panopticon.Simulation/AssemblyInfo.cs`
- `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontKeysTests.cs`

## Acceptance criteria

- [ ] `dotnet build src` builds the new project.
- [ ] `FormationKey.Parent` of `formation:blue/a1/c1/d1/b2/bn1` is `formation:blue/a1/c1/d1/b2`, and its echelon is `Battalion`.
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

- Set Status above to `Done` or `Blocked`. Set W0 Status to `In progress` in the track doc.
- Append a result line to `.exchange`.
- Unblock `WI-W0-02`, `WI-W0-03`, and `WI-W0-04`.
