# Implementation Plan: Track P · P0-2 — Generic Definition Resolution

> Track: [Track P — Playable Baseline](../track-p-playable-baseline.md)
> Phase: [P0 — ReplaySketch arbitrary-command harness](../deep-dives/playable-baseline-harness.md) · sub-phase P0-2: Generic definition/player resolution
> Checklist item: [implementation-checklist.md](../implementation-checklist.md) — second item under P0

## Problem

`ReplayExporter.BuildOrders` has two hardcoded fallback branches that assume only
USA or GLA factions are in play:

```csharp
bool isGla = factionIndex == 4;
var defName = action.BuildingDefinitionName
    ?? (isGla ? KnownDefinitions.GlaBarracksName : KnownDefinitions.UsaBarracksName);
var unitName = action.UnitDefinitionName
    ?? (isGla ? KnownDefinitions.GlaRebelName : KnownDefinitions.UsaRangerName);
```

The `KnownDefinitions` class (`ReplayExporter.cs:21-32`) encodes only 4 definition
names for USA and GLA. China (factionIndex 3) and all sub-faction indices (5-12)
silently fall through to the USA defaults — wrong hashes, wrong units.

`BuildingCatalog` and `UnitCatalog` already have complete, correct per-faction
lists for USA (2), China (3), GLA (4), and all sub-factions (5-12). The exporter
does not use them for fallbacks.

There is also a residual use of `KnownDefinitions.UsaBarracksName` as a last-resort
fallback in `ActionTreeController.cs:305`, which is similarly unnecessary.

Checklist item AC:
- A scenario referencing any object definition by name (incl. a non-USA/GLA faction)
  exports orders with the correct definition hash
- No faction names remain hardcoded in `ReplayExporter`
- Test resolves a definition from a non-default faction (China)

## Approach

Replace the `KnownDefinitions` fallback paths with catalog lookups.
Extract the resolution logic into two `internal static` helper methods to make
the behavior directly unit-testable without going through the full export pipeline.

### ReplayExporter.cs changes

Delete the `KnownDefinitions` class.

Extract two `internal static` resolution helpers:

```csharp
internal static string ResolveConstructDefinitionName(string? specified, int factionIndex)
{
    if (specified != null) return specified;
    return BuildingCatalog.ForFaction(factionIndex)
        .FirstOrDefault(b => b.DisplayName == "Barracks")?.DefinitionName
        ?? throw new InvalidOperationException(
            $"No barracks definition found for factionIndex {factionIndex}. " +
            "Set BuildingDefinitionName explicitly or ensure BuildingCatalog covers this faction.");
}

internal static string ResolveUnitDefinitionName(string? specified, int factionIndex)
{
    if (specified != null) return specified;
    return UnitCatalog.ForFaction(factionIndex).FirstOrDefault()?.DefinitionName
        ?? throw new InvalidOperationException(
            $"No unit definition found for factionIndex {factionIndex}. " +
            "Set UnitDefinitionName explicitly or ensure UnitCatalog covers this faction.");
}
```

Replace the `BuildOrders` fallback calls to use these helpers. Remove `bool isGla`.

### ActionTreeController.cs change

Line 305 already uses `BuildingCatalog.ForFaction(factionIndex)` as the primary
lookup. Remove the trailing `?? KnownDefinitions.UsaBarracksName` so unknown
factions produce a null `buildingDefinitionName` (the label then uses just the verb
without a building name, which is acceptable).

### InternalsVisibleTo

Create `src/OpenSage.Tools.ReplaySketch.Core/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("OpenSage.Game.Tests")]
```

This follows the pattern used by `OpenSage.Game/AssemblyInfo.cs`.

### Tests

Add three tests in `ReplaySketchTests.cs`:

- `ResolveConstructDefinitionName_China_ReturnsChineseBarracks`: calls helper with
  `specified = null, factionIndex = 3`, asserts result is `"ChinaBarracks"`.
- `ResolveUnitDefinitionName_China_ReturnsFirstChineseUnit`: calls helper with
  `specified = null, factionIndex = 3`, asserts result is `"ChinaInfantryRedGuard"`.
- `ResolveConstructDefinitionName_ExplicitNameTakesPrecedence`: calls helper with
  `specified = "SomeCustomBuilding", factionIndex = 3`, asserts result is
  `"SomeCustomBuilding"` (explicit override wins).

## Files to Change

| File | Change |
|---|---|
| `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` | Delete `KnownDefinitions` class; extract `ResolveConstructDefinitionName` and `ResolveUnitDefinitionName` helpers; remove `isGla` branch |
| `src/OpenSage.Tools.ReplaySketch.Core/Profiles/ActionTreeController.cs` | Remove `?? KnownDefinitions.UsaBarracksName` fallback |
| `src/OpenSage.Tools.ReplaySketch.Core/AssemblyInfo.cs` | NEW — `InternalsVisibleTo("OpenSage.Game.Tests")` |
| `src/OpenSage.Game.Tests/Tools/ReplaySketch/ReplaySketchTests.cs` | Add 3 tests for helper methods |

## Out of Scope (Later P0 Items)

- Generic player targeting (`PlayerIndex` resolution from scenario) → checklist item 3
- Runtime object references via auto-handles and query selectors → checklist item 4
- Full `OrderType` mapping for move/attack/ability → checklist item 5
- Validation/assertion layer → checklist item 6
- Headless run-and-assert entry point → checklist item 7
