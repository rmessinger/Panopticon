# Task Card: `P0-03` — Make ReplaySketch definition resolution faction-agnostic

| Field | Value |
|---|---|
| Card ID | `P0-03` |
| Roadmap phase | [Track P · P0 — ReplaySketch arbitrary-command harness](../../track-p-playable-baseline.md) (sub-phase P0-2, see [deep dive §3.2](../../deep-dives/playable-baseline-harness.md)) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |

## Goal

Remove the hardcoded USA/GLA defaults from the ReplaySketch exporter so a scenario's object
definitions are resolved purely from the names the scenario provides (any faction, any object),
via the existing `AssetHash.GetHash(name)` path. This is the first concrete step of P0's
"express any command over any definition" generalization — a faction-agnostic exporter.

## Context to read (and only this)

- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:16-32` — the `KnownDefinitions`
  class (hardcoded USA/GLA barracks + ranger/rebel names/hashes) to delete.
- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs:194-251` — `BuildOrders(...)`, which
  currently falls back to `KnownDefinitions` via `isGla ? Gla… : Usa…` (lines 197, 203-205, 220-222);
  this is the code to change. Note `BuildOrders` is also called at lines 75 and 85 passing
  `player.FactionIndex`.
- `src/OpenSage.Tools.ReplaySketch.Core/Profiles/alpine-assault-usa-vs-gla.json` — the regression
  scenario. It already specifies `buildingDefinitionName` / `unitDefinitionName` for every
  action (lines ~95, 120, 155, 178), so removing the defaults must not change its behaviour.

## Steps

1. Delete the `KnownDefinitions` class (`ReplayExporter.cs:16-32`) and the doc-comment above it.
2. In `BuildOrders`, for `ActionType.Construct` and `ActionType.RecruitBasicUnit`, replace the
   `action.BuildingDefinitionName ?? (isGla ? … : …)` / `action.UnitDefinitionName ?? (isGla ? … : …)`
   fallbacks with a required name: read `action.BuildingDefinitionName` /
   `action.UnitDefinitionName` directly. If the name is `null`/empty, throw an
   `InvalidOperationException` whose message names the offending `ActionType` (e.g.
   `"Construct action requires a BuildingDefinitionName"`). Keep the existing
   `(int)AssetHash.GetHash(defName)` resolution unchanged.
3. Remove the now-unused `bool isGla = factionIndex == 4;` line and the `int factionIndex` parameter
   from `BuildOrders`. Update both call sites (lines ~75, ~85) to drop the `player.FactionIndex`
   argument.
4. Leave the `GatherResources` and `AttackEnemyBase` cases unchanged (they don't use definition names).

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/Services/ReplayExporter.cs` (only this file).

## Acceptance criteria

- [ ] `KnownDefinitions` no longer exists anywhere in the project (`grep -rn KnownDefinitions src` is empty).
- [ ] `BuildOrders` takes no `factionIndex`/`isGla` and resolves definitions only from the action's
      `BuildingDefinitionName` / `UnitDefinitionName`.
- [ ] A `Construct`/`RecruitBasicUnit` action with no definition name throws a clear, named exception
      (no silent USA/GLA fallback).
- [ ] The `alpine-assault-usa-vs-gla` profile still exports successfully (its actions all carry names),
      producing the same orders as before.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
grep -rn "KnownDefinitions" src   # must print nothing
```

## Out of scope / guardrails

- Do not design the open `CommandSpec` model or the assertion layer — those are P0-1/P0-4
  (escalate to a high-effort model / the [deep dive](../../deep-dives/playable-baseline-harness.md)).
- Do not change the `ActionType` enum, the JSON profile, or the deferred-injection path.
- Stay in `ReplayExporter.cs`. If the change pulls in other files, mark `Blocked` and report.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the matching item(s) in the [implementation checklist](../../implementation-checklist.md) (P0-2).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create:
  - A design card (escalate) for the open `CommandSpec` command model (P0-1).
