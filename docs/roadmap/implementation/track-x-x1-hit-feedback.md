# X1: Hit-feedback card sequence

> Audience: human + agent.

Design and architecture: [X1 hit feedback](../deep-dives/x1-imported-unit-hit-feedback.md).
Phase and dependencies: [Track X](../track-x-fixes-tweaks.md).

| Card | Deliverable | Initial status |
|---|---|---|
| [WI-X1-01](../agent/cards/WI-X1-01-hit-feedback-policy.md) | Source/runtime audit and bounded compatibility policy | Ready |
| [WI-X1-02](../agent/cards/WI-X1-02-imported-damage-fx-binding.md) | Opt-in imported-vehicle damage-effect binding | Draft |
| [WI-X1-03](../agent/cards/WI-X1-03-hit-feedback-regressions.md) | Policy, lifecycle, and combat regression tests | Draft |
| [WI-X1-04](../agent/cards/WI-X1-04-readable-duel-capture.md) | Readable labeled duel and rendered impact evidence | Draft |

One commit per card; leave any PR open for operator merge. Reconcile Draft cards against preceding evidence before execution. Stop and preserve artifacts on determinism/golden-output deviations, pending operator determination.

---

## Audit and results

### Policy audit

Installed `INI.big`: `Data/INI/Weapon.ini` defines `GLARebelMachineGun` with `SMALL_ARMS`, damage 5, effectively instant speed 999999, `ProjectileObject = NONE`, and `WeaponFX_GenericMachineGunFire` at the muzzle. `Data/INI/DamageFX.ini` defines `TankDamageFX`: small-arms major/minor effects both use `FX_DamageTankStruckSmallArms`, heroic variants use `FX_HeroicDamageTankStruckSmallArms`, and throttle defaults to 100 ms (3 logic frames). `Data/INI/FXList.ini` supplies sparks, smoke at height 10, a 500 ms light pulse, and impact sound. These are target effects, not projectiles.

Policy: optional `CaptureContentImport.DamageEffects` maps exact unit names to installed Generals DamageFX names. The runner converts keys to source-qualified `ContentId` values and passes the mapping and `AssetStore.DamageFXs.GetByName` lookup into `UnitMaterializationContext`. Shared materialization validates every requested subject (typed vehicle with armor), DamageFX, and non-null major/minor FXList reference before staging. Missing subjects or assets produce Materialize diagnostics naming the subject and reference. Absence preserves existing behavior. Source records are not changed.

Binding: a narrow `ArmorTemplateSet.CreateImported` factory assigns the validated reference on the detached unit's armor set. Native private setters and collections remain unchanged. `ActiveBody.ValidateArmorAndDamageFX` reads the set; `DoDamageFX` selects damage type and enforces throttle; `DamageFX.DoDamageFX` executes at victim rotation/translation. No projectile or damage-processing change is needed.

Capture reuse: actor `Label`, manifest health/max-health and screen projections, AutoFit span, `CaptureStride`, and `RenderEveryFrame` already exist. The generic GIF annotator needs only an opt-in health panel using manifest values. Wider crop padding retains model bounds and particles; no general HUD is required. Visual placement/throttle assertions can use rendered frame sequences if no existing effect recorder is available.

Tests: synthetic policy success/absence/missing-subject/missing-effect/missing-FXList, native identity preservation and failed publication; configured/unconfigured real-weapon health/death comparison. Commands: standard build, non-interactive tests, format, brace check, then the card's ScenarioCapture command and generic GIF annotator with labels and health.

### Binding results

`CaptureContentImport.DamageEffects` and context validation are implemented. `ArmorTemplateSet.CreateImported` keeps the native private setter intact. Five synthetic policy cases pass: exact opt-in, absence, missing DamageFX, missing subject, and missing FXList. Build and scoped format pass. Full non-interactive tests found `ParkingPlaceBehavior_ReportLanding` expecting -1 but receiving 0; independent base reproduction is recorded with final validation.

Final capture results pending.
