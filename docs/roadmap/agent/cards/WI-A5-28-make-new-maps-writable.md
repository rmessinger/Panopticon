# Task Card `WI-A5-28`: Make new WorldBuilder maps structurally writable

| Field | Value |
|---|---|
| Card ID | `WI-A5-28` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-6 WorldBuilder baseline |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-01` must identify the required blank-map chunks |
| Diagrams | Not applicable: bounded WorldBuilder baseline repair. |

## Goal

Initialize every required map chunk so a new normal-sized map can accept terrain and object edits,
save, and reload.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-6-worldbuilder-baseline.md`.
- `src/Panopticon.Tools.WorldBuilder.Core/NewMapFactory.cs`, `EditorMap.cs`.
- `src/Panopticon.Simulation/Data/Map/MapFile.cs`: `CreateEmpty` and `WriteTo`.
- Required chunk types named by the audit.
- Existing map read/write tests.

## Steps

1. Initialize each chunk unconditionally required by `MapFile.WriteTo`.
2. Supply valid empty/default semantics for objects, sides, waypoints, world info, lighting, and
   blend data as identified by the audit.
3. Preserve existing loaded-map behavior.
4. Add blank save/reload and terrain-plus-object tests.

## Files likely to change

- `src/Panopticon.Simulation/Data/Map/MapFile.cs`
- `src/Panopticon.Tools.WorldBuilder.Core/NewMapFactory.cs`
- Focused map and WorldBuilder tests

## Acceptance criteria

- [ ] A blank new map saves and reloads without null failures.
- [ ] Terrain and one object survive reload.
- [ ] Every required chunk has an explicit valid default.
- [ ] Existing map fixtures still load.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~MapFile|FullyQualifiedName~NewMapFactory"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not implement missing optional WorldBuilder features or large-map storage.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
- Unblock `WI-A5-02` after `WI-A5-27` is also done.
