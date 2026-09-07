# Track A · A3: Simulation/presentation seam

> Audience: human overview above the divider; agent-context detail below it.

Implements the A3 row of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a3-sim-presentation-seam.md](../deep-dives/a3-sim-presentation-seam.md), including its
[architecture diagrams](../deep-dives/a3-sim-presentation-seam.md#8-architecture-diagrams).

## Scope

Formalize the boundary between logic state and presentation. Logic must not read render-only or
audio-only state; logic commands presentation only through the `IGameObjectClient` interface and
the one-way effect services (audio, particles, tracers). Presentation reads logic state freely.
Excludes relocating the presentation-by-role subtrees that currently live under `OpenSage.Logic`
namespaces, and excludes moving drawable scripting (`CurDrawable*` Lua functions) to a client-side
script engine; both are documented in the deep dive as named follow-ups.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| A3-1 | [`WI-A3-01`](../agent/cards/WI-A3-01-audit-logic-presentation-reads.md) | Audit logic reads of presentation state. |
| A3-2 | Deep dive | Define the boundary, the seam interface, and the diagrams (the `WI-A3-02` gate). |
| A3-3 | [`WI-A3-02`](../agent/cards/WI-A3-02-logic-presentation-boundary-test.md) | Reflection-based enforcement test with an exact allowlist. |
| A3-4 | Follow-up cards (executed in this change) | Remove every audited read violation; allowlist reaches zero. |

---

## Agent context

Entry points after the phase:

| Concern | Surface |
|---|---|
| Logic handle to an object's client side | `GameObject.Client` (`IGameObjectClient`, `OpenSage.Logic.Object`) |
| Concrete client object (presentation only) | `Drawable` (`OpenSage.Client`), obtained as `gameObject.Client is Drawable d` |
| Model condition flags | Owned by `GameObject.ModelConditionFlags` (logic state); `Drawable.ModelConditionFlags` forwards for presentation consumers |
| Bone data for logic | `IGameObjectClient.TryGetBoneTransform` (bind pose), `TryGetBonePose` (current root-relative pose), `GetWeaponLaunchBoneTransform`, `GetWeaponFireFXBoneTransform` |
| Client object lifecycle | `GameClient.CreateClientObject` / `DestroyClientObject` (`IGameObjectClient`-typed) |
| Enforcement | `LogicPresentationBoundaryTests` reflects over `OpenSage.Logic` types and fails on any reference to a presentation type |

## Logic-to-presentation read audit

Audit of `src/OpenSage.Game/Logic/` references to presentation types (`Drawable`, `Camera`,
`GraphicsDevice`, `Scene2D`, `AudioSystem`, `Cursors`, `RenderTime`, `ModelInstance`, `W3d`-prefixed
types, `OpenSage.Rendering`), classified per `WI-A3-01`. Line numbers are the pre-change tree.

### Read violations (the A3 defect list)

| Site | Logic type | Presentation state read | Logic-side value exists? | Fix |
|---|---|---|---|---|
| `GameObject.cs:340` with `1102-1104`, `1140-1152`, `1162`, `1188`, `1195`; `ProductionUpdate.cs:234-238` | `GameObject`, `ProductionUpdate` | `Drawable.ModelConditionFlags` (construction, door, sold state branched on by logic) | No | Flags become logic-owned: `GameObject.ModelConditionFlags` field; `Drawable.ModelConditionFlags` forwards |
| `GameObject.cs:1763-1767` | `GameObject.Persist` | `Drawable.ID` read/write for the sav stream | No (client id) | `IGameObjectClient.ClientObjectId` |
| `GameObject.cs:2017-2020` | `GameObject` (debug inspector) | Iterates `Drawable.DrawModules` | No | `IGameObjectClient.DrawInspector` |
| `DockUpdate.cs:37-50` | `DockUpdate` | `ModelInstance.AbsoluteBoneTransforms` (dock action/waiting positions; sim-affecting) | No | `TryGetBonePose` (root-relative, bind-pose seeded per `ModelInstance.cs:96-107`) |
| `SpawnPointProductionExitUpdate.cs:23-42` | `SpawnPointProductionExitUpdate` | `ModelInstance.RelativeBoneTransforms` (spawn points; sim-affecting) | No | `TryGetBonePose` |
| `OpenContain.cs:85-87` | `OpenContain` | `ModelBone.Transform` rest pose via `Drawable.FindBone` (exit path) | No | `TryGetBoneTransform` (bind pose) |
| `TransportContain.cs:67-93` | `TransportContain` | `ModelBone.Transform` rest pose (exit bones) | No | `TryGetBoneTransform` |
| `ParkingPlaceBehavior.cs:527-537` | `ParkingPlaceBehavior` | `ModelBone.Transform` rest pose (runway/parking bones) | No | `TryGetBoneTransform` |
| `SlavedUpdate.cs:99-105` | `SlavedUpdate` | `ModelInstance.AbsoluteBoneTransforms` (welding FX position) | No | `TryGetBonePose` |
| `ProjectileNugget.cs:65-66` | `ProjectileNugget` | `Drawable.GetWeaponLaunchBoneTransform` (projectile spawn position; sim-affecting) | Returns plain data already | Same method on `IGameObjectClient` |
| `ProjectileNugget.cs:137`, `156-157` | `ProjectileNugget` | `Drawable.TryGetModelSpaceBounds` (feeds `ModelTransform`, logic state) | Returns plain data already | Same method on `IGameObjectClient` |
| `FiringWeaponState.cs:56-58` | `FiringWeaponState` | `Drawable.GetWeaponFireFXBoneTransform` (fire FX position) | Returns plain data already | Same method on `IGameObjectClient` |
| `ObjectCreationList.cs:144-148` | `DebrisOCL` | Casts `Drawable.DrawModules[0]` to `W3dDebrisDraw`, calls `SetModelName` | No | `IGameObjectClient.SetDebrisModel` command |
| `ActiveBody.cs:808-815` | `ActiveBody` | Calls `Drawable.ReactToBodyDamageStateChange`, which writes the damage-tier condition flags from the client side | The damage tier is logic state | `ActiveBody.EvaluateVisualCondition` writes the flags on `GameObject` directly |
| `GameObject.cs:1101`, `1140`, `1181-1194`, `1376-1394`, `1430` | `GameObject` | Branches on `Drawable` existence | After the flags move, yes | Guards removed or replaced by interface null checks |

Read count: 15 clusters. All are removed in this change; the enforcement test's allowlist is empty.

### Write and type-only references (context, not defects)

| Cluster | Sites | Classification |
|---|---|---|
| Client lifecycle commands | `GameObject.cs:528` (`CreateDrawable`), `:2067` (`DestroyDrawable`), `:1157` (`Drawable.LogicTick`), `GameLogic.cs:198` (`Drawable?.Destroy()`) | write |
| Presentation state commands | `Player.cs:246` `TriggerSelection`; `HackInternetAIUpdate` states `SetAnimationDuration` (`StartHackingInternetState.cs:28`, `StopHackingInternetState.cs:28`); `HordeUpdate.cs:55,62` and `ArmorUpgrade.cs:24` `SetTerrainDecal`; `BuildingBehavior.cs:38-81`, `SubObjectsUpgrade.cs:25-33` `Show/HideSubObject`; `ToppleUpdate.cs:177` `ShadowsEnabled`, `:253` `FindClientUpdateModule<SwayClientUpdate>().StopSway()`; `SupplyWarehouseDockUpdate.cs:26` `SetSupplyBoxesRemaining`; `PowerPlantUpdate.cs:25-45`, `GameObject.cs:1378-1394` condition flag sets; `GameObject.cs:1160-1176` `BuildRenderList` delegation | write |
| Audio commands | `AudioSystem.PlayAudioEvent` in `GameObject`, `ActiveBody`, `ProductionUpdate`, `OpenContain`, `EjectPilotDie`, `GateOpenAndCloseBehavior`, `BattlePlanUpdate`, `SpecialPower`, `FiringWeaponState`, `SupplyAIUpdate`, `DozerAndWorkerState`, `HackInternetAIUpdate` states | write (one-way audio service) |
| Effect services | `ProjectileNugget` `TracerManager.CreateTracking`, `ProjectileStreamManager.Create`, `GameEngine.ParticleSystems.Create`, FXList execution | write (one-way effect services) |
| Debug drawing signatures | `PartitionCellManager.cs:286,313` `DebugDraw`; `Colliders.cs:153`; `Geometry.cs:151` | type-only; moved to `DebugOverlay` |
| Name-collision data fields | `AIData` `AltCamera*`; `ObjectDefinition` `LiveCamera*`; `CreateAHeroSystem` `CameraAngle`; `OrderType.SetCameraPosition`; `UpgradeTemplate.Cursor`; `SpecialPower.RadiusCursorRadius`; `ModelConditionFlag.BuildPlacementCursor`; `ObjectKinds.DrawableOnly`/`OrientsToCamera`; `LocomotorTemplate.cs:235` comment | data, not presentation references |
| Persisted client ids | `ChinookCombatDropState.cs:50,60` `DrawableID`; `ParticleUplinkCannonUpdate.cs:20-84` laser drawable ids | data (uint, persisted, never resolved to `Drawable`) |
| Asset-data types | `using OpenSage.Audio` in sound/template data classes; `ProjectileNugget.cs:218-221` `W3dProjectileStreamDrawModuleData` (INI data) | data |

### Presentation-by-role under `OpenSage.Logic`

| Subtree | Role | Evidence |
|---|---|---|
| `Logic/Object/Draw/` | Presentation | `DrawModule` instances are created by and attached to `Drawable` (`Drawable.cs:213-227`); `BuildRenderList`/`FindBone` serve rendering. `*ModuleData` classes are shared asset data |
| `Logic/Object/ClientUpdate/` | Presentation | `ClientUpdateModule` instances are created by `Drawable` (`Drawable.cs:237-250`) and ticked by `Drawable.LogicTick` |
| `Logic/OrderGenerators/` + `OrderGeneratorSystem` | Presentation (order-preview UI) | Cursor selection, radius-cursor decals, render lists; updated from the input path (`Scene3D.cs:398-401`), rendered at `Scene3D.cs:440` |
| `Logic/SelectionSystem.cs`, `SelectionMessageHandler.cs` | Presentation (input) | Screen picking via `Scene3D.Camera` (`SelectionSystem.cs:169,277-283`); driven by input messages |
| `Logic/GhostObject.cs`, `GhostObjectManager.cs` | Presentation (placement ghosts) | Holds `ModelInstance` per player for build placement previews |

These are excluded from the enforcement test's logic-type set by namespace/type name, per
`WI-A3-02` step 3. Relocating them under `OpenSage.Client` is a named follow-up.

### Adjacent mixed surfaces (documented, out of structural scope)

- `LuaScriptEngine` (`OpenSage.Scripting`): `CurDrawable*` functions are SAGE client scripting
  (draw-module animation-state scripts generated at `W3dModelDraw.cs:936-975`); they read drawable
  state from the shared script engine. Outside `OpenSage.Logic`, so not flagged.
- `Scene3D` (`OpenSage` root): composition layer. Drives the logic tick (`Scene3D.LogicTick`) and
  owns camera and render list building.
- `IGameEngine.GameClient` (`OpenSage` root): the client service property logic uses to create and
  destroy client objects; command-only.
