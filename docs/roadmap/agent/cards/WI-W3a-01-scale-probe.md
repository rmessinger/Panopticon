# Task Card `WI-W3a-01`: Measure Sedan tactical scale

| Field | Value |
|---|---|
| Card ID | `WI-W3a-01` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Measure the headless tactical runtime cost for Sedan-scale soldier counts with Chassepot-scale acquisition range, without changing committed product code. Record the projected reference run wall-clock in the W3a deep dive.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §6, §10, §12, and Appendix current-state hooks.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §3-§7.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §6.
- `src/Panopticon.Simulation/SimulationHost.cs:68-147`: host construction and fixed-frame run loop.
- `src/Panopticon.Simulation/Logic/PartitionCellManager.cs:10` and `src/Panopticon.Simulation/GameData.cs:793`: long-range target scan bounds.
- `src/Panopticon.Simulation/SageGame.cs:21-25`: Generals 30 Hz logic rate.

## Steps

1. Create a session-local probe under the session workspace, not committed source.
2. Boot a headless `SimulationHost` and spawn `AmericaInfantryRanger` and `ChinaInfantryRedguard` placeholder infantry at increasing counts up to 320,000.
3. Raise weapon `AttackRange` and `VisionRange` to about 3,937 game units.
4. Measure per-logic-frame time, per-system time if available, peak memory, and target acquisition cost.
5. Extrapolate the measured frame cost to 6.48M frames.
6. Add a W3a appendix named `Scale probe results` and append one `.exchange` result line.

## Files likely to change

- `docs/roadmap/deep-dives/w3a-sedan-1870.md`
- `.exchange`
- Session-local probe files, not committed

## Acceptance criteria

- [ ] Report includes per-frame cost and peak memory at the largest measured count, targeting 320,000 soldier objects.
- [ ] Report includes target acquisition cost with about 3,937 game-unit attack and vision range.
- [ ] Report projects wall-clock for 6.48M frames.
- [ ] If projected reference wall-clock exceeds 14 days per seed, the card stops and escalates to the operator.
- [ ] If no map can host the probe, the blocker and largest measured count are recorded.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project session-scale-probe -- --max-count 320000 --range 3937
```

## Out of scope / guardrails

Committed product code is read-only. Modelled detail is not reduced by default.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
