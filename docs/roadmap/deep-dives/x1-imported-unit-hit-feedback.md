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

The blocked capture's placement/playback extension is gated by [WI-X1-05](../agent/cards/WI-X1-05-target-impact-design.md). That read-only audit grounds the new diagrams and source-edit cards; the diagrams below describe the existing binding path, not an approved directional-contact implementation. Findings are owned by the [implementation plan](../implementation/track-x-x1-hit-feedback.md#capture-results-blocked-visible-impact-gate).

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

## Evidence

The audit records source archive entry names, actual asset lookups, damage type selection, throttle, and the chosen policy representation. Final evidence includes the duel profile, command, damage assertions, and shared GIF. Headless health loss is not evidence of a rendered impact. No source assets are committed.
