# Plan: Tracer rounds via the `Tracer` FXList nugget

> Design for rendering bullet tracers (machine-gun streaks) for weapons whose `FireFX` carries a
> `Tracer` nugget. Chooses the FXList route over the stubbed draw modules and specifies the
> client-side manager and quad pass needed to draw them.
>
> Audience: human + agent (implementation reference).

---

## Purpose

Vanilla Generals draws tracers through `Tracer` nuggets inside weapon `FireFX` FXLists
(66 uses in `INI.big`, e.g. `WeaponFX_GenericMachineGunFireWithRedTracers`). Panopticon parses
the nugget data but never renders it: `TracerFXNugget.Execute` inherits the base no-op
(`src/Panopticon.Simulation/FX/FXNugget.cs`), so every applicable weapon currently fires without a
visual streak. This plan closes that gap with the least machinery.

## Current state

- Data layer complete: `TracerFXNugget` (`src/Panopticon.Simulation/FX/FXNuggets/TracerFXNugget.cs`)
  parses `Color`, `DecayAt`, `Length`, `Probability`, `Speed`, `Width`, matching the shipped
  INI field set exactly. Shared nugget gates (`Weather`, `OnlyIfOnLand`,
  `RequiredSourceModelConditions`, `ExcludedSourceModelConditions`) already apply.
- Trigger path complete: `FiringWeaponState.TriggerWeaponFireFX` executes the weapon's `FireFX`
  at the fire-bone transform on every shot; the `FXListExecutionContext` carries `Position`,
  `Rotation`, and `GameEngine`.
- Rendering missing: no line, polyline, ribbon, or streak primitive exists in the renderer.
  `W3dTracerDraw`, `W3dStreakDraw`, `W3dProjectileStreamDraw`, and `W3dLaserDraw` are stubs.
  The working visual primitives are mesh render items (`ModelMesh.BuildRenderListWithWorldMatrix`)
  and the particle-system pass (`ParticleSystemManager`).

## Design

Implement tracers as an FXList nugget effect, not as projectile objects or a draw module:

- No projectile object is spawned; the streak is a pure client visual. No logic-side state or
  replay impact, and no interaction with the simulation host or tiered recording.
- One new `TracerManager` (client side, modeled on `ParticleSystemManager`) owns active tracers
  and draws them in its own transparent pass. No `RenderList` bucket changes.
- The fire-bone transform from `FXListExecutionContext` encodes origin and firing direction;
  `Speed`/`Length`/`Width`/`Color`/`DecayAt` come from the nugget.
- `Probability` gates which shots draw a tracer ("every Nth bullet"), rolled on
  `GameClient.Random` since the effect is cosmetic.

## Steps

1. `TracerFXNugget.Execute(FXListExecutionContext)`: gate on `Probability`, then enqueue a
   spawn request to `TracerManager` with the context position/rotation and nugget parameters.
2. `TracerManager`: holds active tracers `{ origin, direction, Speed, Length, Width, Color,
   DecayAt }`; advances them in client update; expires at distance `DecayAt`.
3. Rendering: one elongated quad per tracer, oriented along the flight direction (not a camera
   billboard), additive blend, small dynamic vertex buffer. Draw in a dedicated pass after the
   transparent bucket, the same way the particle-system pass is owned by its manager.
4. No INI or weapon changes: existing nugget gates work for free.

## Out of scope

- `W3dTracerDraw`, `W3dStreakDraw`, `W3dProjectileStreamDraw`: object-attached streak draw
  modules, separate stubs, stay unimplemented.
- `W3dLaserDraw` + `LaserUpdate`: laser weapons, a different feature.
- `ProjectileExhaust` / missile exhaust trails: parsed but unconsumed; would be a projectile
  trail system, not an FXList effect.

## Validation

- TestHarness headless unaffected (client-only change); `dotnet test src --filter
  Category!=Interactive` unchanged.
- Visual check: USA vs GLA skirmish; Rangers and Comanches show tracers immediately from their
  stock `FireFX` lists.

## Effort

`Execute` is about 10 lines; the manager plus quad rendering is the bulk of the work.
