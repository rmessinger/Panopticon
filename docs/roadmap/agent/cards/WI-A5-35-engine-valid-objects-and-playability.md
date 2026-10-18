# Task Card `WI-A5-35`: Engine-valid objects and new-map playability defaults

| Field | Value |
|---|---|
| Card ID | `WI-A5-35` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-6 WorldBuilder baseline |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-A5-28` (blank chunks exist before playable content can populate them) |
| Diagrams | Not applicable: bounded WorldBuilder baseline repair. |

## Goal

Make a WorldBuilder-authored map loadable and skirmish-playable by the engine, and make every
authored object engine-valid, closing the save (object encoding) and play steps of the audit
([baseline](../../implementation/track-a-a5-6-worldbuilder-baseline.md)).

## Context to read (and only this)

- The round-trip, playability, and object-encoding rows of the WorldBuilder baseline audit.
- `src/Panopticon.Tools.WorldBuilder.Core/NewMapFactory.cs`, `PlaceObjectTool.cs`,
  `WaypointTool.cs`.
- `src/Panopticon.Simulation/Scripting/Waypoints.cs` (`ObjectTypeName`,
  `Player_{i}_Start` convention, required properties) and
  `src/Panopticon.Simulation/Logic/Map/SidesListUtility.cs` (skirmish side requirements).
- `src/Panopticon.Simulation/Data/Map/MapObject.cs` (`Properties` null from the placement ctor).

## Steps

1. New-map defaults: `NewMapFactory` populates a minimal skirmish-playable set on top of the
   blank chunks from `WI-A5-28`: a `SidesList` with neutral, civilian, and two faction players
   derived from the installed game's player templates, default `GlobalLighting`, and one
   `Player_{i}_Start` waypoint per player slot at spread positions.
2. Object encoding: `PlaceObjectTool` and `WaypointTool` create `MapObject`s with non-null
   `Properties` (empty list for plain objects) so `MapObject.WriteTo` never NREs.
3. Waypoint encoding: the waypoint tool writes `TypeName` `"*Waypoints/Waypoint"` with
   `waypointID` and `waypointName` properties, matching `Waypoint(MapObject)`.
4. Add Core tests: a factory map saves without exceptions; its waypoints parse as engine
   waypoints; start waypoints resolve per player.
5. Playability proof is `WI-A5-02`'s round-trip test; this card supplies the content it needs.

## Acceptance criteria

- [x] A `NewMapFactory` map saves and re-opens with sides, lighting, and start waypoints.
  - `SidesList.CreateSkirmish` (neutral, civilian, then one player per faction template; order
    load-bearing for the skirmish loader), `NewMapParameters.FactionTemplateNames` defaults to
    `FactionAmerica`/`FactionGLA`, and `AddPlayerStartWaypoints` places one
    `Player_{i}_Start` waypoint per faction slot on the diagonal with height-sampled Y.
- [x] Placed objects and waypoints survive save with non-null properties.
  - `MapObject`'s placement ctor has defaulted non-null `Properties` since WI-A5-28 (the audit's
    null row predates it), so `PlaceObjectTool` needed no change.
- [x] Authored waypoints parse as engine waypoints (type name and properties).
  - `Waypoint.CreateWaypointMapObject` writes the starred `*Waypoints/Waypoint` type name plus
    required `waypointID`/`waypointName`; `WaypointTool` now places that encoding and numbers
    ids after existing waypoints.
- [x] Core tests cover the defaults and the encoding without the UI.
  - `NewMapPlayableDefaultsTests`: default and custom faction templates round-trip (sides
    factions, waypoint names/ids, `WaypointCollection.TryGetPlayerStart`); suite 10/10.
- [x] Standard validation passes (see [guide](README.md#standard-validation)).

## Out of scope / guardrails

- Starting maps from arbitrary paths outside the install/user-data file system roots is an
  engine capability change, not a WorldBuilder repair; the interim workflow copies the saved map
  into the user-data folder. Record the limitation, do not fix it here.
- Map metadata editing windows (Map Settings Apply, Player List, Teams mutation) get their own
  cards; this card only supplies new-map defaults.
