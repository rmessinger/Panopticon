# Task Card `WI-W3a-18`: Apply the environment schedule

| Field | Value |
|---|---|
| Card ID | `WI-W3a-18` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-W3a-17`, `WI-W0-04` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-17` and `WI-W0-04` are Done and clock schedule types are reconciled.

Add the Sedan light, visibility, weather, and movement schedule.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §8.3.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §1, §4.1, and §6.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §5 and §8.3.

## Steps

1. Add `EnvironmentSchedule` entries for 31 August through 2 September local mean time.
2. Encode sunrise, sunset, moon data, valley fog, clear intervals, rain, mud, and night modifiers.
3. Apply visibility to detection and fire hit modifiers.
4. Apply rain, mud, and night movement modifiers.
5. Add schedule lookup and behavior tests at boundary times.

## Files likely to change

- Western Front environment schedule files
- Sedan scenario parameter JSON
- Western Front tests

## Acceptance criteria

- [ ] Schedule values match W3a §8.3.
- [ ] Visibility modifies detection and fire.
- [ ] Mud, rain, and night modify movement.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~EnvironmentSchedule"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No dynamic weather generation.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
