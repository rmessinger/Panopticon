# Task Card `WI-W0-03`: Build the synthetic regional topology

| Field | Value |
|---|---|
| Card ID | `WI-W0-03` |
| Roadmap phase | [Track W · W0](../../track-w-western-front.md): charter fixture |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01` |
| Diagrams | [W0 §12](../../deep-dives/w0-charter-fixture.md#12-architecture-diagrams) |

## Goal

Add `WesternFrontTopology` with the §5 synthetic layout, ownership rules, and domain topology description.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3 and §5.
- `src/Panopticon.Simulation/Simulation/Transport/TransportGraphTopology.cs`: topology type pattern.
- `src/Panopticon.Simulation/Simulation/Types/DomainTopology.cs`.

## Steps

1. Add immutable `Sector`, `Region` (key, sector, column, bounds, owner side), `DepotNode` (key,
   region, side, position), and `TransportLink` (key, from, to, rail or road).
2. Add `WesternFrontTopology.CreateSynthetic()` producing 2 sectors, 8 regions, 6 nodes, and 8
   links exactly as §5.
3. Add `RegionAt(Vector2)`, `CellKeyAt(Vector2)`, `RegionOf(key)` for node, link, and cell keys,
   and `LinkOwnerRegion(linkKey)` (the tail node's region).
4. Add `Describe(IReadOnlySet<string> activeRegions)` returning `DomainTopologyDescription`: every
   sector, region, node, and link key, region adjacencies, link endpoints, and cells only for the
   given regions.
5. Tests for counts, ownership by column, link owner, cell keys at region corners, and adjacency.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Topology/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontTopologyTests.cs`

## Acceptance criteria

- [ ] `LinkOwnerRegion("link:blue.rail->blue.dump1")` returns the rail node's region.
- [ ] `Describe` with no active regions lists no `cell:` keys; with one active region it lists exactly 64.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontTopology"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No geospatial input (W3). No movement or routing.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-W0-06`.
