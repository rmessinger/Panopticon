# Track M · M5: 1000 vs 1000 battle implementation plan

Source of truth: [M5 checklist items](../implementation-checklist.md#m5-1000-vs-1000-battle),
[mass battle charter](../deep-dives/mass-battle-1k.md) :
[M5 architecture diagrams §9](../deep-dives/mass-battle-1k.md#m5-battle). Cards:
[`WI-M5-01..10`](../agent/cards/).

## Starting gaps

Code read for the plan: the M1/M4 probe runner and profile records, `MassBattlePlacement`,
`RosterBudgetEvaluator`, `UnitContentImportRunner`, `VictorySystem`, `VictoryConditionRule`,
`SimulationHost`, `RunOutputRecorder`, `StatisticalRunHarness`, `RunRecording`,
`HarnessWindowedFrame`, `PlayerManager`, and the A5 cards.

| Gap | Evidence | Owner |
|---|---|---|
| Army AI wiring lives in the probe | Policy payload, `Players[2 + i]`, `DisableAI` loop, and policy casts in `MassBattleCapacityProbeRunner` and `MassBattleSquadInvariantSampler` | `WI-M5-01` |
| No runner spawns an M3 army | `MassBattleCapacityProfile.Blocks` is shared by both sides; `mass-battle-rosters.json` is only priced (`Ee2RosterBudgetTests`); EE2 units publish as `ee2:<RowKey>` while the manifest uses bare row keys | `WI-M5-02` |
| Airfield parking far below roster aircraft | The probe parks 8 jets per side; the manifests carry 100 and 120 aircraft per side | `WI-M5-02` |
| No P3 outcome for an army battle | The probe sets `NeverDefeatedVictoryConditionRule` and stops on its own `AliveUnits == 0` check; `DefaultVictoryConditionRule` keys on production structures and builders | `WI-M5-03` |
| No seed knob or host-driven run | `ProbeSeed = 1` constant; the probe steps `game.Update` directly, so `SimulationHost` recorders never see a tick | `WI-M5-04` |
| No per-unit-type kills and losses | `PlayerScoreManager` kill/loss dictionaries are never written; `GameObjectLifecycleEventArgs` has no killer; the last damage source is private to `ActiveBody` | `WI-M5-05` |
| No batch runner or win-rate interval | `StatisticalRunHarness` gives Student-t intervals over continuous metrics; no binomial interval exists | `WI-M5-06` |
| No map-bias control | No mirror or side-swap code | `WI-M5-07` |
| Battle runs cannot be replayed | `RunRecording.Attach` is test-only; A5-3 replayer (`WI-A5-04`) and bundle writer (`WI-A5-09`) are `Blocked`; ReplaySketch plays only `.rep` order files, which cannot reproduce direct spawns | `WI-M5-08` |
| No spectator | `PlayerOwner` has no observer value; `PlayerManager.OnNewGame` falls back to `Players[2]` (side A) as local player | `WI-M5-09` |
| Live frame rate unmeasured at battle scale | M1 measured headless only (median 0.96 fps); M4 headless median `tick.wallMs` 121.9 ms; B5 met 34.7 fps at 1k units on `RenderingScaleProbe`, not on the battle | `WI-M5-10` |

## Decisions

1. One profile drives all modes. `MassBattleDeploymentProfile` gains an optional per-side
   roster reference (manifest side id); capacity blocks stay for the M1 probe. Batch, live, and
   replay differ only in the step driver: `SimulationHost` headless, `HarnessWindowedFrame`
   windowed.
2. Army AI comes from `ArmyAiSides` (`WI-M5-01`); the battle has no scripted orders.
3. Outcome: a side is defeated when it owns no living non-structure unit that can attack;
   airfields do not keep a side alive. A `VictoryOptions` frame limit with `UnitCount` scoring
   decides stalled battles as `ScoreDecision`. Default limit 54000 frames (30 game minutes), a
   knob.
4. Aircraft park only. Each side gets enough airfield anchors in its zone for its roster's
   aircraft; anchors are scenario data. If the zone cannot hold them, `WI-M5-02` stops for an
   operator decision instead of airborne spawning.
5. Win rates use a Wilson score 95 percent interval per side. Runs without a winner
   (`SuddenDeath` at the frame limit or a score tie) count as draws, are reported separately, and
   stay in the denominator.
6. The map-bias control is a mirror match: both zones field the same roster. The control's side A
   interval must include 50 percent. A side-swap batch of the asymmetric battle is reported
   alongside to separate roster strength from zone advantage.
7. Batch size is a knob. `WI-M5-07` picks the acceptance size from measured run length, since a
   single run at the M4 tick cost (121.9 ms) can take tens of minutes of wall time.
8. Replay waits for A5: `WI-M5-08` stays `Blocked` until `WI-A5-04` and `WI-A5-09` land; it only
   attaches the recording and proves the scrub at 2000 units.
9. The live milestone is measured, not forced. `WI-M5-10` records the median and attributes the
   miss; engine fixes become B-track follow-up cards, not M5 work.

## Card sequence

| Card | Checklist | Prerequisites | Status |
|---|---|---|---|
| [`WI-M5-01`](../agent/cards/WI-M5-01-army-ai-side-setup.md) army AI side setup | M5-1 | M4 | `Done` |
| [`WI-M5-02`](../agent/cards/WI-M5-02-roster-deployment-profile.md) roster deployment profile | M5-1 | M3 | `Ready` |
| [`WI-M5-03`](../agent/cards/WI-M5-03-army-eliminated-outcome.md) army-eliminated outcome | M5-1 | P3 | `Done` |
| [`WI-M5-04`](../agent/cards/WI-M5-04-mass-battle-scenario.md) mass battle scenario | M5-1 | `WI-M5-01..03` | `Ready` |
| [`WI-M5-05`](../agent/cards/WI-M5-05-kill-loss-per-unit-type.md) kills and losses per unit type | M5-2 | `WI-M5-04` | `Ready` |
| [`WI-M5-06`](../agent/cards/WI-M5-06-seeded-batch-runner.md) seeded batch runner | M5-2 | `WI-M5-05` | `Ready` |
| [`WI-M5-07`](../agent/cards/WI-M5-07-map-bias-control-and-batch-acceptance.md) map-bias control and batch acceptance | M5-2 | `WI-M5-06` | `Ready` |
| [`WI-M5-08`](../agent/cards/WI-M5-08-battle-recording-and-replay.md) battle recording and replay | M5-1, M5-3 | `WI-M5-04`, `WI-A5-04`, `WI-A5-09` | `Blocked` |
| [`WI-M5-09`](../agent/cards/WI-M5-09-spectator-live-view.md) spectator live view | M5-4 | `WI-M5-04` | `Ready` |
| [`WI-M5-10`](../agent/cards/WI-M5-10-live-frame-rate-measurement.md) live frame rate measurement | M5-4 | `WI-M5-09` | `Ready` |

`WI-M5-01..03` are independent. After `WI-M5-04`, the batch chain (`05..07`) and the live chain
(`09..10`) run in parallel. M5-1 closes when `WI-M5-08` adds the replay mode to the scenario
from `WI-M5-04`.

## Group 1 results

### WI-M5-01: what landed

`ArmyAiSides` owns the side wiring: `ApplyPolicies` before game start, `Resolve` (side id,
`Player`, `AIPlayer`, policy, plus `DisableAI` on every non-side player) after it, and the
`SidePlayerBaseIndex = 2` slot offset. `HierarchicalAIDecisionPolicy.GetStats(owner)` exposes
the read-only ground stats (`HierarchicalAIPolicyStats`) and the air-mission counters went
public; the probe report, squad-invariant sampler, and M0 deployment runner no longer cast the
policy or resolve player slots themselves. Validation: install-free `ArmyAiSidesTests` 4/4;
scaled army-AI and scripted probe reports byte-identical on seed 1 before and after the
refactor; full non-interactive suite 2383 passed / 8 known failures. Residual:
`ScenarioCaptureRunner.ResolvePlayerSlot` keeps its own `Players[2 + slotIndex]` (scenario-actor
slot seam, not army-side resolution).

### WI-M5-03: what landed

`ArmyEliminatedVictoryConditionRule`: a side is defeated when it owns no living non-structure
object that can attack; airfields never keep a side alive, parked aircraft do. Selected from
outside Simulation through `VictoryConditionKind` on `VictoryOptions` (`ApplyOptions` maps it to
the rule, matching the frame-limit seam); `DefaultVictoryConditionRule` and the check cadence
are untouched. Validation: new install-free tests 5/5 (elimination winner, mutual elimination
to `SuddenDeath`, airfield-only defeat, parked aircraft survival, options-path frame limit with
`UnitCount` `ScoreDecision`); existing victory tests 23/23 and the P7 matrix 2/2 unchanged; full
non-interactive suite 2388 passed / 8 known failures.
