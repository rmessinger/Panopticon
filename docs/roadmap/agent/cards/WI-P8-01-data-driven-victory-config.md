# Task Card `WI-P8-01`: Data-driven victory configuration seam

| Field | Value |
|---|---|
| Card ID | `WI-P8-01` |
| Roadmap phase | [Track P · P8](../../track-p-playable-baseline.md): sub-phase P8-1, see [deep dive](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases); remediates a gap in [P3 victory/defeat](../../deep-dives/p3-victory-defeat.md#12-defeat-detection) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-P3-02`, `WI-P3-06` |
| Diagrams | [P8 Architecture Diagrams](../../deep-dives/p8-implementation-delta-remediation.md#6-architecture-diagrams) |

## Goal

The design states the defeat rule, score metric, and frame limit are exposed as configuration so
later experiments (including Track A's A4 pluggable policies) can vary them without re-plumbing.
Today they are only `internal` C# properties on `VictorySystem` (`ConditionRule`, `ScoreMetric`,
`FrameLimit`), settable only from test code, not from any skirmish/scenario/map configuration
surface. Close that gap: make the three values settable from the same configuration path a
skirmish setup already uses (map settings or `PlayerSetting`-adjacent skirmish options).

## Context to read (and only this)

- `docs/roadmap/deep-dives/p3-victory-defeat.md:46-68`: defeat rule / score metric / frame limit design intent as "configurable/data-driven".
- `docs/roadmap/deep-dives/p3-victory-defeat.md:133-145`: resolved design questions confirming these are meant to be config-driven, not hardcoded.
- `src/Panopticon.Simulation/Logic/VictorySystem.cs:17-34`: current `internal` `ConditionRule`, `ScoreMetric`, `FrameLimit` properties: the seam that needs a real configuration entry point.
- `src/Panopticon.Simulation/Logic/VictoryConditionRule.cs`: the rule type hierarchy (`VictoryConditionRule`, `DefaultVictoryConditionRule`).
- `src/Panopticon.Network/SkirmishManager.cs` (or wherever skirmish/game-start settings are assembled): existing skirmish configuration surface to extend, e.g. a `GameOptions`/`SkirmishSettings`-like type if one exists.
- `test/Panopticon.Simulation.Tests/Logic/Victory/VictoryRuleTests.cs` and `test/Panopticon.Simulation.Tests/Logic/Victory/TimeLimitScoreDecisionTests.cs`: existing tests that set these properties directly; keep them passing (they may continue to set the properties directly for isolated unit tests) while adding a new higher-level path.

## Steps

1. Identify (or add, if none exists) a lightweight configuration object for match-level settings that is already threaded through skirmish start (e.g. alongside `PlayerSetting[]` in `PlayerManager.OnNewGame`), or add a single new optional parameter/settings type if no natural home exists.
2. Add a frame-limit value (defaulting to `VictorySystem.NoFrameLimit`) and a named score-metric selector (e.g. an enum with at least `BuildingCount`, defaulting to the current behavior) to that configuration surface.
3. Wire it so constructing/starting a match sets `VictorySystem.FrameLimit`/`ScoreMetric` from the configuration instead of requiring test code to set them directly afterward.
4. Leave `ConditionRule` selection as an internal/test-only seam if there is no natural data-driven representation for swapping the whole defeat-rule algorithm yet (a full `VictoryConditionRule` plugin system is a larger design question; do not over-build it here); document this explicitly as a known remaining gap in the Result section if so.
5. Add a headless `[Fact]` test that starts a match through the real skirmish/config path with a non-default frame limit and score metric and asserts `VictorySystem.FrameLimit`/`ScoreMetric` reflect the configuration, not just that setting the properties directly still works.
6. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/Panopticon.Simulation/Logic/VictorySystem.cs`
- The skirmish/game-start configuration type (exact file depends on what already exists; search before adding a new one)
- `test/Panopticon.Simulation.Tests/Logic/Victory/` (new or extended test file)

## Acceptance criteria

- [ ] Frame limit and score metric can be set through a real skirmish/scenario configuration path, not only via direct `internal` property assignment in test code.
- [ ] Default behavior (no limit, `BuildingCount` metric) is unchanged when the new configuration is left at its defaults.
- [ ] A headless test starts a match through the configuration path and asserts the configured values took effect.
- [ ] If full `ConditionRule` pluggability is out of scope for this card, that is explicitly stated as a remaining gap, not silently dropped.
- [ ] Existing `VictoryRuleTests`/`TimeLimitScoreDecisionTests` still pass unmodified in behavior (only add, do not break, the direct-property test path).
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~Victory"
dotnet test src --filter Category!=Interactive
dotnet format src
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not build a full pluggable-policy system for `VictoryConditionRule` here; that is Track A's A4 concern. A named, documented remaining gap is an acceptable outcome for the rule (not the metric/frame-limit) axis.
- Do not change default victory behavior for existing matches/tests.
- Do not remove the existing `internal` test-facing properties; add a configuration path on top of them.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the P8-1 row in [`deep-dives/p8-implementation-delta-remediation.md`](../../deep-dives/p8-implementation-delta-remediation.md#3-sub-phases).
- Append a result line to repo-root `.exchange`.
- List any follow-up cards to create here:
  - Full `VictoryConditionRule` pluggability (Track A / A4), if not covered by this card.

## Result

<!-- append after completing -->
