# Task Card `WI-W0-03`: Build the synthetic topology fixture

| Field | Value |
|---|---|
| Card ID | `WI-W0-03` |
| Roadmap phase | [Track W · W1](../../track-w-western-front.md): aggregate sector simulation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W0-01` |
| Diagrams | [W1 §13](../../deep-dives/w1-aggregate-sector.md#13-architecture-diagrams) |

## Goal

Add the W0 synthetic topology with region, cell, node, link, and authority helpers.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w0-charter-fixture.md` §5.
- `src/Panopticon.Simulation/Simulation/Types/DomainTopologyDescription.cs`: topology DTO.
- `src/Panopticon.Simulation.WesternFront/` key helpers from `WI-W0-01`.

## Steps

1. Add topology records for sectors, regions, cells, nodes, links, and adjacency.
2. Build the two-sector, four-column fixture with 3280-unit regions and 8 x 8 cells of 410 units.
3. Implement `RegionOf`, `CellOf`, `LinkOwnerRegion`, and bounded `DescribeTopology` projection.
4. Add tests for dimensions, ownership, adjacency, link ownership, and cell keys.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Topology/*.cs`
- `test/Panopticon.Simulation.Tests/WesternFront/WesternFrontTopologyTests.cs`

## Acceptance criteria

- [ ] Every region reports width and height 3280 world units.
- [ ] Every region has 64 cells and each cell is 410 world units wide.
- [ ] `link:blue.rail->blue.dump0` is owned by the tail node region.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WesternFrontTopology"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No scenario objects or state capture.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- List follow-up cards to create: none.
