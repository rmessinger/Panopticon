# Task Card `WI-VG-05`: Switch the validation ground to Dark Mountain

| Field | Value |
|---|---|
| Card ID | `WI-VG-05` |
| Roadmap phase | [Validation ground gap closure](../../plans/validation-ground-gap-closure.md) |
| Status | `Done` |
| Type | `behavior-change` |
| Effort | `S` |
| Prerequisites | none |

## Goal

Move the unit validation ground from Alpine Assault to Dark Mountain and re-baseline, so the
sandbox measures on a larger, flatter map with room for the full roster at spacing.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/ScenarioBuilder.cs`: `BuildValidationGroundRun` default map.
- `src/Panopticon.Tools.TestHarness/AGENTS.md`: Unit Validation Ground section, baseline refresh.
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json`: baseline.

## Why Dark Mountain

Alpine Assault's playable area constrains the builder to two usable flat discs, crowding roster
placement. A terrain survey of all 51 Generals maps (largest 4-connected flat region within
2.5 world z-units, steep-edge fraction at |dz| > 3) ranked Dark Mountain best for the ground:
400x400 playable cells (520x520 with border 60), a 77,858-cell contiguous flat patch spanning the
whole map bounding box at z0, and only 3.3% steep edges. Twilight Flame is larger but mountainous;
Tournament Island is flatter on paper but its z0 plane is water.

## Steps

1. Change the fallback map in `BuildValidationGroundRun` and the documented default in
   `TestHarness.json` / `AGENTS.md` to `maps/Dark Mountain/Dark Mountain.map`.
2. Refresh the baseline headless (`ValidationGroundRefreshBaseline: true`, baseline path set).
3. Run headless twice more and confirm the two reports are byte-identical (determinism check).
4. Confirm the install-free gates still pass against the new baseline
   (`CommittedBaseline_CoversEveryCatalogUnit`, golden frames consistent with weapon INI data).

## Files likely to change

- `src/Panopticon.Tools.TestHarness/ScenarioBuilder.cs`
- `src/Panopticon.Tools.TestHarness/TestHarness.json`
- `src/Panopticon.Tools.TestHarness/AGENTS.md`
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-baseline.json`

## Acceptance criteria

- [ ] Default validation-ground map is Dark Mountain in code and docs.
- [ ] Baseline refreshed on Dark Mountain; two follow-up headless runs byte-identical.
- [ ] Install-free validation-ground tests pass against the refreshed baseline.
- [ ] No tolerance widened and no minimum frame floor lowered.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario UnitValidationGround
dotnet test test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj --filter "FullyQualifiedName~ValidationGround"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not adjust per-unit placement overrides unless a unit fails to place on the new map; record
  any such failure in `.exchange` instead of widening tolerances.
- Do not change the regression comparison logic or exit codes.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to `.exchange`.

## Result

2026-10-04: Done. Default map switched to `maps/Dark Mountain/Dark Mountain.map` in
`ScenarioBuilder.BuildValidationGroundRun` (fallback constant), `TestHarness.json`, and
`TestHarness/AGENTS.md`.

2026-10-05 correction: the first attempt was inert. The validation-ground profile
`src/Panopticon.Tools.ReplaySketch.Core/Profiles/unit-validation-ground-scenario.json` pins
`MapPath`, and a non-empty profile path takes precedence over the `ScenarioBuilder` fallback, so
every run behind the first commit (baseline refresh, golden changes, captures, contact sheet)
actually executed on Alpine Assault. The effective switch point is the profile's `MapPath`, now
`maps/Dark Mountain/Dark Mountain.map`; the fallback/doc changes remain correct for
profile-less configurations.

On genuine Dark Mountain: refresh run measured 26/26 pass; two follow-up comparison runs each
measured 26/26 pass with byte-identical report JSONs (determinism check); install-free gates
green (6/6). Captures verify the terrain actually changed (mean RGB 63,61,43 vs Alpine's
85,88,71). The refreshed goldens are byte-identical to the Alpine re-capture: kill timings are
weapon-delay driven and independent of the map, so the committed baseline needed no further
change beyond the earlier re-capture (`ChinaJetMIG` 34 -> 48 stale-golden fix,
`AmericaTankCrusader` 5 -> 4, `GLAInfantryTunnelDefender` 47 -> 46, entries sorted by name).

The refresh run itself drops units that never complete within the headless frame cap, so the four
pre-existing timeout units (`AmericaVehicleTomahawk`, `ChinaVehicleNukeLauncher`,
`GLAInfantryTerrorist`, `GLAVehicleSCUDLauncher`) were carried over verbatim from the prior
baseline to keep `CommittedBaseline_CoversEveryCatalogUnit` intact; they time out identically on
both maps (unchanged headless condition, also reflected in `.exchange`). No tolerances widened,
no minimum frame floors lowered, no placement failures on the new map.
