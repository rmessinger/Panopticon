# X1: Imported-unit hit feedback

> Source of truth: [Track X](../track-x-fixes-tweaks.md).
> Audience: human + agent.

## Problem and scope

The mixed-source capture imports six EE2 reference rows but demonstrates one tank versus one Generals Rebel. Its tight crop cuts off the tank. Reports show health loss, but target-side feedback is absent on the imported tank.

Generals separates firing effects from damage effects. `GLARebelMachineGun` is effectively hitscan (`ProjectileObject = NONE`); adding a projectile is not the fix. The installed `TankDamageFX` maps small-arms damage to `FX_DamageTankStruckSmallArms`, with a throttle. `ActiveBody` obtains this binding from `ArmorTemplateSet.DamageFX`. Shared imported armor materialization currently supplies armor without this damage-effect binding.

EE2 ships impact visuals too, but importing its effect sequences is a separate capability. X1 uses an explicit, opt-in Generals presentation mapping, recorded as a compatibility choice, not as source-authored EE2 data. Only the imported vehicle selected by the capture receives the mapping. Imports without the policy retain their current behavior. Requested missing effects fail with actionable diagnostics before publication; silently selecting a global default is not acceptable.

## Design gate

The [policy audit](../implementation/track-x-x1-hit-feedback.md#policy-audit) specifies the mapping and pre-publication validation. Typed source records, scoped publication, and existing Generals bindings remain unchanged.

Health bars and unit labels supplement, but do not replace, effects at the damaged target. Reuse existing capture/UI or overlay facilities first. Keep both complete model bounds and impact effects inside the frame throughout the duel. The output must be identified as a two-unit duel, not a full-roster or rearm demonstration.

[WI-X1-05](../agent/cards/WI-X1-05-target-impact-design.md) traced the blocked capture and approved two bounded extensions: opt-in impact-effect selection and attacker-facing bounds placement. Decisions and evidence are owned by the [implementation plan](../implementation/track-x-x1-hit-feedback.md#target-impact-design-wi-x1-05). The first two diagrams show the landed binding path; the last two show the approved extension, where `DamageFXPlacement` and `DamageFXContact` do not exist yet.

## Architecture diagrams

```mermaid
classDiagram
  UnitContentImportRunner --> UnitRuntimeMaterializer
  UnitContentImportRunner --> UnitMaterializationContext : DamageEffects and installed lookup
  UnitRuntimeMaterializer --> UnitMaterializationContext : validate before staging
  UnitRuntimeMaterializer --> UnitCombatMaterializer
  UnitCombatMaterializer --> ArmorTemplateSet
  ArmorTemplateSet --> DamageFX : optional binding
  ActiveBody --> ArmorTemplateSet : ValidateArmorAndDamageFX
  ActiveBody --> DamageFX : DoDamageFX
  ScenarioCaptureRunner --> UnitContentImportRunner
```

```mermaid
sequenceDiagram
  participant Import as UnitContentImportRunner
  participant Mat as UnitCombatMaterializer
  participant Armor as ArmorTemplateSet
  participant Body as ActiveBody
  participant FX as DamageFX
  Note over Import,Mat: Explicit DamageEffects mapping, validated before staging
  Import->>Mat: Shared materialization
  Mat->>Armor: Armor and opt-in DamageFX reference
  Body->>Armor: ValidateArmorAndDamageFX
  Note over Body: Existing weapon damage path
  Body->>FX: DoDamageFX after damage processing
  Note over FX: Existing target placement and throttle
```

Approved extension (WI-X1-06, WI-X1-07):

```mermaid
classDiagram
  UnitMaterializationContext --> DamageNugget : ImpactEffectDamageTypes, validated
  DamageNugget --> WeaponTarget : DamageFXOverride
  WeaponTarget --> DamageInfoInput : DamageFXOverride
  ArmorTemplateSet --> DamageFXPlacement : new, set by CreateImported
  ActiveBody --> DamageFXContact : new, AttackerFacingBounds only
  DamageFXContact --> IGameObjectClient : TryGetModelSpaceBounds
  ActiveBody --> DamageFX : DoDamageFX with position
```

```mermaid
sequenceDiagram
  participant Weapon as DamageNugget
  participant Target as WeaponTarget
  participant Body as ActiveBody
  participant Contact as DamageFXContact
  participant FX as DamageFX
  Weapon->>Target: DoDamage(type, amount, override)
  Target->>Body: AttemptDamage(DamageInfoInput)
  Note over Body: Damage, armor, death unchanged
  Body->>Body: effect type = override or damage type, then throttle
  alt AttackerFacingBounds with source and client bounds
    Body->>Contact: bounds, ModelTransform x Transform, source position
    Contact-->>Body: facing-side point or bounds center
  else otherwise
    Note over Body: victim.Translation
  end
  Body->>FX: DoDamageFX(type, amount, source, victim, position)
```

## Evidence

The audit records source archive entry names, actual asset lookups, damage type selection, throttle, and the chosen policy representation. Final evidence includes the duel profile, command, damage assertions, and shared GIF. Headless health loss is not evidence of a rendered impact. No source assets are committed.
