# X1: Hit-feedback card sequence

> Audience: human + agent.

Design and architecture: [X1 hit feedback](../deep-dives/x1-imported-unit-hit-feedback.md).
Phase and dependencies: [Track X](../track-x-fixes-tweaks.md).

| Card | Deliverable | Status |
|---|---|---|
| [WI-X1-01](../agent/cards/WI-X1-01-hit-feedback-policy.md) | Source/runtime audit and bounded compatibility policy | Done |
| [WI-X1-02](../agent/cards/WI-X1-02-imported-damage-fx-binding.md) | Opt-in imported-vehicle damage-effect binding | Done |
| [WI-X1-03](../agent/cards/WI-X1-03-hit-feedback-regressions.md) | Policy, lifecycle, and combat regression tests | Done |
| [WI-X1-05](../agent/cards/WI-X1-05-target-impact-design.md) | Directional tank-impact and Rebel playback audit/design | Done |
| [WI-X1-06](../agent/cards/WI-X1-06-imported-impact-effect-selection.md) | Opt-in impact-effect selection for imported weapons | Ready |
| [WI-X1-07](../agent/cards/WI-X1-07-attacker-facing-impact-placement.md) | Attacker-facing bounds placement for mapped imported targets | Ready |
| [WI-X1-04](../agent/cards/WI-X1-04-readable-duel-capture.md) | Readable labeled duel and rendered impact evidence | Blocked |

One commit per card; leave any PR open for operator merge. Reconcile Draft cards against preceding evidence before execution. Stop and preserve artifacts on determinism/golden-output deviations, pending operator determination.

Unblocking order: X1-05 audit/design (Done), X1-06 and X1-07 (independent), then X1-04 recapture. Audit completion alone does not satisfy the visual gate. Projectile support is separate.

---

## Audit and results

### Policy audit

Installed `INI.big`: `Data/INI/Weapon.ini` defines `GLARebelMachineGun` with `SMALL_ARMS`, damage 5, effectively instant speed 999999, `ProjectileObject = NONE`, and `WeaponFX_GenericMachineGunFire` at the muzzle. `Data/INI/DamageFX.ini` defines `TankDamageFX`: small-arms major/minor effects both use `FX_DamageTankStruckSmallArms`, heroic variants use `FX_HeroicDamageTankStruckSmallArms`, and throttle defaults to 100 ms (3 logic frames). `Data/INI/FXList.ini` supplies sparks, smoke at height 10, a 500 ms light pulse, and impact sound. These are target effects, not projectiles.

Policy: optional `CaptureContentImport.DamageEffects` maps exact unit names to installed Generals DamageFX names. The runner converts keys to source-qualified `ContentId` values and passes the mapping and `AssetStore.DamageFXs.GetByName` lookup into `UnitMaterializationContext`. Shared materialization validates every requested subject (typed vehicle), DamageFX, and non-null major/minor FXList reference before staging. Missing subjects or assets produce Materialize diagnostics naming the subject and reference. Absence preserves existing behavior. Source records are not changed.

Binding: a narrow `ArmorTemplateSet.CreateImported` factory assigns the validated reference on the detached unit's armor set. Native private setters and collections remain unchanged. `ActiveBody.ValidateArmorAndDamageFX` reads the set; `DoDamageFX` selects damage type and enforces throttle; `DamageFX.DoDamageFX` executes at victim rotation/translation. No projectile or damage-processing change is needed.

Capture reconciliation: the published EE2 tank has no authored armor record. A null-valued armor reference on the effect-only set preserves the existing `Armor.NoArmor` path exactly, including signed amounts. No neutral armor asset or native armor mapping is introduced.

Capture reuse: actor `Label`, manifest health/max-health and screen projections, AutoFit span, `CaptureStride`, and `RenderEveryFrame` already exist. The generic GIF annotator needs only an opt-in health panel using manifest values. Wider crop padding retains model bounds and particles; no general HUD is required. Visual placement/throttle assertions can use rendered frame sequences if no existing effect recorder is available.

Tests: synthetic policy success/absence/missing-subject/missing-effect/missing-FXList, native identity preservation and failed publication; configured/unconfigured real-weapon health/death comparison. Commands: standard build, non-interactive tests, format, brace check, then the card's ScenarioCapture command and generic GIF annotator with labels and health.

### Binding results

`CaptureContentImport.DamageEffects` and context validation are implemented. `ArmorTemplateSet.CreateImported` keeps the native private setter intact. Five synthetic policy cases pass: exact opt-in, absence, missing DamageFX, missing subject, and missing FXList. Build and scoped format pass. Full non-interactive tests found `ParkingPlaceBehavior_ReportLanding` expecting -1 but receiving 0; independent base reproduction is recorded with final validation.

### Regression results

Eleven focused cases pass. `UnitDamageEffectLifecycleTests` uses the existing `FXNugget.Execute` seam to record actual effect execution. The mock host has no drawable, so the real weapon's `DamageNugget` is executed against its real `WeaponTarget`, rather than invoking the full muzzle/fire state machine or injecting synthetic damage inputs. Target position/rotation and a three-frame throttle are asserted. Configured and unconfigured health traces, damage counts, and death outcomes agree, with authored armor and with no armor. Successful publication/unload and rejected erroneous content preserve native definition and DamageFX identities. Shared armor does not spread the policy to another unit; non-vehicles are rejected.

Full non-interactive validation has one pre-existing failure: `ParkingPlaceBehavior_ReportLanding`, expected -1, actual 0. A detached clean worktree at `5bc7eb13f` reproduces the same assertion. No determinism or golden assertion failed.

Final capture results are recorded below; X1 remains In progress until visible-impact evidence passes.

### Capture results: blocked visible-impact gate

The profile is explicitly named `ee2-tank-rebel-duel`. It retains all six publication rows but spawns only the tank and Rebel. Both complete models remain framed; labels and measured health bars occupy a separate GIF panel. The 360-frame duration ends before either actor dies. The camera fits at height 177; GIF crop padding 260 compensates for the tank's gameplay radius being much smaller than its rendered bounds. Capture cadence is one frame per 30 Hz logic tick.

Real-weapon results: Rebel 120 to 30 HP, three hits at capture frames 45, 152, and 259. Tank 300 to 225 HP, fifteen small-arms hits from frame 201 through 343. Both `ActorDamaged` expectations pass. Sampled tank-region pixels (100,150 to 540,390) remain unchanged across hits despite the validated binding. Muzzle flashes on the Rebel are visible, but do not establish target-impact rendering. Target impacts in both directions remain visually unproven.

Likely follow-up: inspect source visual scaling and target particle playback. The imported tank covers its target origin and the manifest radius does not describe its visible model. `ParticleSystemFXNugget.Execute` also currently ignores the parsed `Height` field. Neither issue is repaired by changing damage, adding fake flashes, or importing an unrelated armor. These findings require a bounded follow-up design before X1-4 can be closed.

Operator review correction: tank impacts are visible between the wheels, not on the Rebel-facing surface. The pixel sample above did not establish that effects were absent and is not a placement proof. `DamageFX.DoDamageFX` uses `victim.Translation` and `victim.Rotation`; it does not calculate surface contact from the attack direction. Placement must be distinguished from occlusion and model scale.

The tank's recoil animation is visible without a shell or visible Rebel impact. `UnitCombatMaterializer` creates a direct `DamageNugget`, copies projectile speed, but does not materialize a projectile object. This is the current typed-import contract, not evidence that a rendered shell disappeared. The native infantry effect selection/playback at the Rebel remains a separate investigation from projectile support. No cosmetic shell is added to conceal that gap.

### Target-impact design (WI-X1-05)

#### Hit traces

| Direction | Damage type, amount | Target armor set | DamageFX group | Executed effect | Position |
|---|---|---|---|---|---|
| Rebel to tank (capture frames 201, 206, ...) | `SMALL_ARMS`, 5 | X1 effect-only set, `Armor.NoArmor` | `TankDamageFX`, `SMALL_ARMS`, 100 ms throttle | `FX_DamageTankStruckSmallArms`: `TankStruckSparks`, `TankStruckSmallArmsSmoke` (both `Height = 10`), light pulse, sound | `victim.Translation`: terrain-level origin under the hull |
| Tank to Rebel (capture frames 45, 152, 259) | `UNRESISTABLE`, 30 | native `HumanArmor` + `InfantryDamageFX` (`FactionUnit.ini`) | `InfantryDamageFX`, `UNRESISTABLE` | none: `MajorFX`/`MinorFX = UNRESISTABLE None` | not reached |

Source facts:

- `Ee2ImportPolicy.DamageType` defaults to `UNRESISTABLE`, so authored EE2 damage applies one-to-one. `Ee2UnitAdapter.AttackRecords` stamps it on every imported weapon; `UnitCombatMaterializer` copies it to the `DamageNugget`.
- `IniParser.ParseFXListReference` returns null for `None`; `DamageFX.GetDamageFXList` therefore yields no list. The missing Rebel impact is fully explained by effect selection. Neither shells, particle lifecycle, nor occlusion are involved.
- `ActiveBody.DoDamageFX` already supports a presentation-only selector: `DamageInfoInput.DamageFXOverride` replaces the damage type for effect lookup and throttle when it is not the `Unresistable` sentinel. `WeaponTarget.DoDamage` never sets it.
- `DamageFX.DoDamageFX` executes at `victim.Translation` with `victim.Rotation`. `ParticleSystemFXNugget.Execute` adds `Offset` but never reads the parsed `Height`. The tank's small-arms particles therefore start at terrain height at the hull center, inside the imported model. This matches the operator observation of impacts between the wheels.
- `IGameObjectClient.TryGetModelSpaceBounds` merges visible mesh bounds in draw-module space. `Drawable` renders with `InstanceMatrix * ModelTransform.Matrix * Transform.Matrix`; `InstanceMatrix` is only changed by `SwayClientUpdate` (trees). `ProjectileNugget` already consumes these bounds from logic for presentation.

Hypothesis, not verified: original Generals adds `Height` to the particle Z. Restoring it would move every native damage effect, so it is not an X1 prerequisite. Directional contact supplies a full 3D point for opted-in targets.

#### Decision: Rebel feedback

Opt-in weapon-side effect selection, not a damage change. `CaptureContentImport.ImpactEffectDamageTypes` maps an imported unit name to a Generals damage type used only for `DamageFXOverride`. The capture maps `LightMounted1UpgradeEpoch15` to `ARMOR_PIERCING`, the Generals tank-gun type; `InfantryDamageFX` resolves it through `DEFAULT` to `FX_DamageInfantryStruck`. This is a recorded compatibility choice, not EE2-authored data. `DamageType` stays `UNRESISTABLE`, so health, armor, and death are unchanged. Validation: the subject must be a typed unit with at least one weapon in the import; the name must parse as a `DamageType` and must not be `UNRESISTABLE` (the "no override" sentinel). `DamageNugget` gains an internal `DamageFXOverride` (default `Unresistable`) threaded through `WeaponTarget.DoDamage`, so native weapons are unchanged. Card: [WI-X1-06](../agent/cards/WI-X1-06-imported-impact-effect-selection.md).

#### Decision: attacker-facing placement

Scope: armor sets created by `ArmorTemplateSet.CreateImported` with a DamageEffects mapping carry `DamageFXPlacement.AttackerFacingBounds`; every other set defaults to `VictimOrigin`. Native Generals and unmapped imports keep current placement. No new profile field.

Contact is a presentation approximation from transformed rendered bounds, not mesh collision:

1. Inputs: target-local bounds from `victim.Client.TryGetModelSpaceBounds`, local-to-world `ModelTransform.Matrix * Transform.Matrix`, and the source object's translation from `DamageInfoInput.SourceID` (already fetched by `ActiveBody.DoDamageFX`).
2. Transform the source into target-local space. Cast from the bounds center toward it and take the slab exit point of the box. Transform back to world. Scale and rotation are handled by the matrix, not a radius.
3. Rotation stays `victim.Rotation` so `OrientToObject` nuggets behave as today.
4. Fallbacks: no client or bounds (headless), dead or missing source, non-invertible or non-finite transform, or result: victim origin. Source inside the bounds or degenerate direction: world bounds center.

The gameplay radius and fixed capture offsets are not used. Damage processing and throttle are untouched: `ActiveBody` computes the position only after throttle passes and passes it to a `DamageFX.DoDamageFX` overload. The geometry lives in a pure static helper testable without graphics. Card: [WI-X1-07](../agent/cards/WI-X1-07-attacker-facing-impact-placement.md).

Projectile materialization stays out of scope. Recoil without a shell is the typed-import contract described above; X1-4 judges impacts only.

Shared artifacts:

- `/mnt/pit2/Network Storage/temp/x1-hit-feedback-duel.gif`, labeled health/framing evidence, not a passing impact proof.
- `/mnt/pit2/Network Storage/temp/x1-hit-feedback-report.json`, damage results and blocked verdict.
- `/mnt/pit2/Network Storage/temp/frames/x1-hit-feedback/manifest.json` and `frame_0000.png` through `frame_0359.png`.
- `frames/x1-hit-feedback/replaysketch-map.png` and `replaysketch-model.png` under the shared directory. Map terrain and markers render. Model screenshot mode captures Ready controls but an empty summary/preview; this is not model-rendering evidence. No front-end code changed.

Reproduction:

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario ScenarioCapture --scenario capture-ee2-reference-roster.json
python3 scripts/annotate_capture_gif.py /tmp/panopticon_capture_ee2-tank-rebel-duel \
  "/mnt/pit2/Network Storage/temp/x1-hit-feedback-duel.gif" --labels --health --padding 260
```

Final validation: build, full format verification, brace check, eleven policy/lifecycle tests, and two Python overlay tests pass. Full non-interactive repeat: 2198 simulation, 356 ReplaySketch, and 53 mathematics tests pass, seven simulation tests skip, and the independently reproduced landing assertion fails. One earlier run also reported a missing P7 invariant predicate; the full repeat passed that test. No registry or unrelated test fixes are included.
