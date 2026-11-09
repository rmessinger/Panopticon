# H2b: Metric cutover release proof

> Audience: human + agent.
> Release proof record for WI-H2b-18 (OpenProject wp657). Contract, release model, and audit inventory: [h2b-metric-core.md](h2b-metric-core.md).

## Provenance

| Item | Value |
|---|---|
| Baseline | Commit `7341141c4`, detached worktree, no source changes. Record: `~/log/track-h-h2b-baseline-v2/BASELINE-V2.md` |
| Predetermined expectations | `~/log/track-h-h2b-baseline-v2/EXPECTED-POST-CUTOVER.md`, derived before any post-cutover run |
| Post-cutover tip | Commit `10dc7e7da` (H2b-17), branch `track-h-h2b-1-metric-cutover`, clean tree |
| Post-cutover logs | `~/log/track-h-h2b-metric-post/` |
| Installs | Generals (`CNC_GENERALS_PATH`) and Empire Earth II (`EMPIRE_EARTH_II_PATH`), both set and loaded for each run |

Post-cutover runs use the same invocations as BASELINE-V2 Runs 1 to 3. Tolerances are fixed in EXPECTED-POST-CUTOVER.md: 1e-4 m for lengths, 1e-6 m per logic frame for rates. Behavior equivalence also requires identical frame counts, manifest sample indices, and outcomes. The conversion rule is defined in [h2b-metric-core.md](h2b-metric-core.md#contract).

## Capture runs

### Run 1: mixed-source EE2

Scenario: `capture-mixed-source-ee2.json`. Result: exit 0, 120 frames. Log: `run1-mixed-source-ee2.log`.

| Quantity | Expected | Observed | Status |
|---|---|---|---|
| Maceman spawn | (143.256, 213.36) m | (143.256, 213.36) m | exact |
| Rebel spawn | (167.64, 213.36) m | (167.64, 213.36) m | exact |
| Maceman displacement, manifest samples 0 to 90 | 19.90776816 m | 19.90773 m (end X 163.16373) | delta 3.8e-5 m, inside 1e-4 m |
| Rebel displacement | 0 | 0 | identical |
| Rebel damage | 5 per hit, 120 to 115 at sample 18 | same | identical index |
| Maceman health | 75 to 5, present in samples 0 to 90 | same | identical index |
| Move.Speed | 0.210312 m per logic frame | 0.2103 distance/frame | agrees to print precision |
| AttackRange | 2.92608 m | 2.9261 m | agrees to print precision |
| GeometryMajorRadius | 0.2286 m | 0.2286 m | agrees to print precision |
| VisionRange | 18.288 m | 18.288 m | agrees to print precision |
| TurnRate | 0.4189 radians/frame | 0.4189 radians/frame | unchanged |
| PrimaryDamage, MaxHealth, ReloadTime, PreAttackDelay, DeathDuration, BuildTime | 5, 75, 45, 15, 90, 1110 | same | unchanged |
| Camera height | 36.576 m | 37 | agrees to print precision |
| Subjects span | 479 px of 1024 px | 479 px | identical |
| Projectile frames | 0 | 0 | identical |
| MinDisplacement expectation | at least 12.192 m (40 x 0.3048) | passed, detail "moved 20.5 of the required 12.2" | passed, see Known limitations |

### Run 2: Generals rebel-attack

Scenario: `capture-rebel-attack.json`. Result: exit 0, 30 frames. Log: `run2-rebel-attack.log`.

| Quantity | Expected | Observed | Status |
|---|---|---|---|
| Rebel spawn | (164.592, 182.88) m | (164.592, 182.88) m | exact |
| Humvee spawn | (195.072, 182.88) m | (195.072, 182.88) m | exact |
| Rebel health | 120 throughout | 120 throughout | identical |
| Humvee health steps | 2.5 each at samples 1, 3, 6, 17, 20, 22, to 225 | same | identical values and indices |
| Camera clamped height | 12.192 m | 12 | agrees to print precision |
| Subjects span | 493 px | 493 px | identical |

### Runs 3 and 4, replay identity

| Check | Command or filter | Result |
|---|---|---|
| ValidationGround tests | `dotnet test test/Panopticon.Simulation.Tests --filter FullyQualifiedName~ValidationGroundTests` with Generals installed | 6 passed, 0 failed (`run3-validation-ground.log`). No determinism, golden-baseline, or byte-identity assertion failed. |
| Strict installed tests | `PANOPTICON_STRICT_INSTALLED=1`, both installs, `FullyQualifiedName~H7UnitRoundTripInstalledTests` | 2 passed, 0 failed |
| Replay chunk byte identity | `FullyQualifiedName~ReplayChunkByteIdentityTests` | 4 passed, 0 failed. Legacy chunks parse and write back byte-identical through source-bit retention. |

## Full suite

Command: `dotnet test src --filter Category!=Interactive`, both installs. Result: Panopticon.Simulation.Tests 3340 passed, 30 failed, 8 skipped, of 3378 (`full-suite.log`). Mathematics 53, Geospatial 1284, Geospatial.Preview 40, WorldBuilder.Core 29, and ReplaySketch 530 are green.

| Class | Count | Basis |
|---|---|---|
| Known failures | 9 | `test/known-failures.json` entries TF-01 to TF-09. TF-10 passed this run. |
| Pre-existing at branch base | 19 | GroundFootprint 2, JetAIUpdateAttack 1, MissileAIUpdateFlight 3, ProjectileDamageTiming 6, TerrainDecalMesher 2, Card24ProofFixture 1, GeneratedMapPreview 1, SedanProofHeightSampling 3 |
| Navigation async-path flakes | 2 | AsyncPathRequestTests. Repeat runs went 2, 1, then 0 failures (`full-suite.log`, `nav-flake-repeat.log`, `nav-flake-capture.log`). No `known-failures.json` change. |
| Introduced by the cutover | 0 | none |

Fixed by H2b relative to the base run: `ValidationGroundCameraTests.FrameFormation`, two GeospatialPreview PlayerStarts tests, and the `ScenarioCaptureRunner` ConstructionProfile test.

## Local gates

| Gate | Result at tip `10dc7e7da` |
|---|---|
| `dotnet build src` | 0 errors, 15 unique warnings in src scope, pre-existing |
| `dotnet format src --verify-no-changes` | exit 0 |
| `scripts/check_brace_newline.py` | exit 0 |

## Viewer evidence

Shared viewer directory: `/mnt/pit2/Network Storage/temp/`

| File | Content |
|---|---|
| `h2b-metric-post-mixed-ee2.gif` | Run 1 capture, post-cutover |
| `h2b-metric-post-rebel-attack.gif` | Run 2 capture, post-cutover |
| `h2b-metric-post-replaysketch-map.png` | ReplaySketch map screenshot |
| `h2b-metric-post-replaysketch-model.png` | ReplaySketch model screenshot |
| `frames/h2b-metric-post-mixed-ee2/` | Run 1 per-frame PNGs and manifest |
| `frames/h2b-metric-post-rebel-attack/` | Run 2 per-frame PNGs and manifest |
| `h2b-baseline-v2-mixed-ee2-reconstructed-from-7341141c4.gif` | Baseline Run 1 GIF, not rewritten (timestamp predates post-cutover runs) |
| `h2b-baseline-v2-rebel-attack-reconstructed-from-7341141c4.gif` | Baseline Run 2 GIF, not rewritten (timestamp predates post-cutover runs) |

## Known limitations

- BFME 0.3048 remains a provisional compatibility scale. No BFME physical fixture exists, so the value is unverified physical calibration ([AQ-1](h2b-metric-core.md#aq-1-bfme-ini-unit-convention-decided)).
- The MinDisplacement detail reads the live object translation at run end (20.5 m). The manifest endpoint gives 19.90773 m, and the predetermined check uses the manifest. The v1 baseline at `2e5d3770` recorded the same detail as "moved 67.4 of the required 40 world units" (`~/log/track-h-h2b-baseline/mixed-source-ee2/manifest.json:10731`), and 67.4 x 0.3048 = 20.54.
- The detail label still reads "world units" (`src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs:1162`) while the values are meters. This is a label only.
- `run4-strict-installed.log` also contains an earlier summary with 1 failed and 1 passed. The log does not record its cause. The Run 4 result above comes from the rerun at tip.
- Printed conversions and the camera height are rounded in the logs. They agree to print precision only; the manifest carries the tolerance-checked values.
