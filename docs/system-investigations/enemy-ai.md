# Enemy AI Architecture

This document explains how "enemy AI" works in OpenSAGE today: which parts of the
original SAGE AI have been ported, which are still stubs, and how the pieces fit
together at runtime. It covers two distinct layers that the original engine calls
"AI":

1. **Per-unit AI** (`AIUpdate` + `AIUpdateStateMachine`): the behavior state
   machine attached to every `GameObject` that can move, fight, guard, wander,
   etc. This is the layer that is substantially ported and functional.
2. **Per-player / skirmish AI** (`AIPlayer` / `SkirmishAIPlayer`): the
   strategic, build-order/attack-wave decision maker for computer-controlled
   players. **This layer is currently a save/load stub only**: the original
   fields are preserved (mostly as `_unknownXxx` placeholders) so replays and
   save files round-trip correctly, but none of the decision-making logic has
   been ported yet. Computer players in OpenSAGE currently rely on map/team
   scripts (`OpenSage.Scripting`) rather than autonomous strategic AI.

Because of this, "enemy AI" in practice today means: individual units/structures
reacting to their surroundings (scanning for enemies, chasing, guarding,
pathfinding) under the direction of either the player, a team script, or their
own default idle behavior.

## 1. Explanation

### 1.1 Per-unit AI (`AIUpdate`)

Every `GameObject` that can act autonomously has an `AIUpdate` module
(`OpenSage.Logic.Object.Update.AIUpdate.AIUpdate`, subclassed for special
movement types like `JetAIUpdate`, `DozerAIUpdate`, `TransportAIUpdate`,
`WorkerAIUpdate`, etc.). `AIUpdate`:

- Owns a lazily-created `AIUpdateStateMachine` (`OpenSage.Logic.AI`) that
  implements the actual behavior (idle, move, attack, guard, hunt, wander,
  panic, dock, follow-waypoints, ...).
- Drives locomotion: it holds a `LocomotorSet`/`Locomotor`, target point queue
  (`TargetPoints`), and asks `IGameEngine.Navigation` to compute paths
  (`CalculatePath`) when a new destination is set.
- Optionally owns a `TurretAIUpdate` for units/structures with turrets, which
  independently scans for and tracks weapon targets (idle scanning, turning,
  attacking, recentering) each logic frame.
- Runs once per logic frame from `GameObject.Update()` via `AIUpdate.Update()`,
  which ticks the state machine, updates the turret, and advances movement
  along `TargetPoints` using the current `Locomotor`.

### 1.2 Generic state machine framework

The AI behavior is expressed with a small, reusable finite-state-machine
framework in `OpenSage.Logic.AI`:

- **`StateMachineBase`**: holds a dictionary of `StateId -> State`, the
  current state, a "goal object/position", and a sleep timer. `Update()` calls
  the current state's `Update()`, and `SetState`/`SetStateInternal` handle
  transitions (`OnExit` on the old state, `OnEnter` on the new one).
- **`State`**: a single behavior. Each state is registered via
  `DefineState(id, state, successStateId, failureStateId, conditions...)`.
  When a state's `Update()`/`OnEnter()` returns `StateReturnType.Success`,
  `Failure`, `Continue`, or `Sleep(n)`, the owning machine looks up the
  matching transition (fixed success/failure target, or one of the
  registered `StateConditionInfo` predicates) and switches state accordingly.
- States can nest **sub state machines**, e.g. `GuardState`, `HuntState`,
  `AttackAreaState`, `AttackTeamState` each wrap their own private
  `StateMachineBase` subclass (`GuardStateMachine`, `AttackAreaStateMachine`,
  ...) to implement multi-step behaviors, similar to hierarchical state
  machines/behavior trees.
- **`AIUpdateStateMachine`** is the top-level machine attached to every
  `AIUpdate`. It registers ~28 top-level states (see `AIStateIds`): `Idle`,
  `MoveTo`, `FollowWaypoints*`, `AttackPosition/Object/Area/Squad`, `Dead`,
  `Dock`, `Guard`, `Hunt`, `Wander`, `Panic`, `RappelInto`, `WanderInPlace`,
  etc. Almost every non-idle state's failure transition leads back to `Idle`.

### 1.3 Per-player / skirmish AI (largely unimplemented)

`Player.AIPlayer` is non-null for any non-human player (`AIPlayer` for a
"generic" computer player, `SkirmishAIPlayer` for a skirmish opponent, see
`Player.cs`). Both classes today only implement `Persist()` for save/load
compatibility; their fields are mostly undocumented placeholders
(`_unknownInt1`, `_unknownFloat`, ...) carried over from reverse engineering
the original binary format. **Neither class currently has an `Update()` method
or any decision-making logic** (build orders, attack waves, resource
management). This is a known gap; see `docs/roadmap/` for where this fits
in the long-term plan.

In place of strategic AI, computer-controlled behavior in OpenSAGE today comes
from:
- **Map/team scripts** (`OpenSage.Scripting`): trigger-based, authored per
  map, can command teams to guard/attack/patrol using the same per-unit AI
  states described above.
- **Default idle behavior**: turret auto-acquire, `Guard`/`Wander` states,
  and reactive states like `PanicState`/`AttackAreaState` that fire from
  `AIUpdate` when a unit takes damage or spots an enemy, independent of any
  higher-level planner.

### 1.4 Pathfinding

`AIUpdate.AppendPathToTargetPoint` calls `IGameEngine.Navigation.CalculatePath`
to get a list of waypoints between the current position and a destination,
which are queued in `TargetPoints` and walked one at a time each frame. The
`Pathfinder` INI-data asset (`OpenSage.Logic.AI.Pathfinder`) currently only
exposes parsed data (e.g. `SlopeLimits`); `ValidMovementTerrain`,
`AddObjectToPathfindMap`, and `RemoveObjectFromPathfindMap` are stubs
(`// TODO(Port): Implement this.`), so fine-grained terrain classification
from the original game is not yet ported; routing currently relies on the
navigation mesh/grid in `IGameEngine.Navigation`.

## 2. Class Diagrams

### 2.1 Per-unit AI module hierarchy

```mermaid
classDiagram
    class GameObject {
        +UpdateModule[] UpdateModules
        +Update()
    }

    class AIUpdate {
        -AIUpdateStateMachine _stateMachine
        -LocomotorSet _locomotorSet
        -TurretAIUpdate _turretAIUpdate
        +List~Vector3~ TargetPoints
        +Locomotor CurrentLocomotor
        +SetTargetPoint(Vector3)
        +AppendPathToTargetPoint(Vector3)
        +FollowWaypoints(IEnumerable~Vector3~)
        +Stop()
        +Update() UpdateSleepTime
        #CreateStateMachine() AIUpdateStateMachine
    }

    class TurretAIUpdate {
        -WeaponTarget _currentTarget
        -TurretAIStates _turretAIstate
        +Update(BitArray~AutoAcquireEnemiesType~)
        -FoundTargetWhileScanning() bool
    }

    class JetAIUpdate
    class DozerAIUpdate
    class TransportAIUpdate
    class WorkerAIUpdate
    class SupplyTruckAIUpdate
    class HordeAIUpdate

    GameObject "1" o-- "many" AIUpdate : UpdateModules
    AIUpdate "1" *-- "0..1" TurretAIUpdate
    AIUpdate "1" *-- "1" AIUpdateStateMachine : lazily created
    AIUpdate <|-- JetAIUpdate
    AIUpdate <|-- DozerAIUpdate
    AIUpdate <|-- TransportAIUpdate
    AIUpdate <|-- WorkerAIUpdate
    AIUpdate <|-- SupplyTruckAIUpdate
    AIUpdate <|-- HordeAIUpdate
```

### 2.2 State machine framework

```mermaid
classDiagram
    class StateMachineBase {
        <<abstract>>
        -Dictionary~StateId,State~ _states
        -State _currentState
        -StateId _defaultStateId
        -LogicFrame _sleepTill
        +GameObject GameObject
        +StateId CurrentStateId
        +GoalObject GameObject
        +GoalPosition Vector3
        #DefineState(id, state, successId, failureId, conditions)
        +SetState(StateId) StateReturnType
        +InitializeDefaultState() StateReturnType
        +Update() StateReturnType
        +Halt()
    }

    class State {
        <<abstract>>
        -StateId _successStateId
        -StateId _failureStateId
        -List~StateConditionInfo~ _conditions
        +StateId Id
        +bool IsIdle
        +OnEnter() StateReturnType
        +OnExit(StateExitType)
        +Update() StateReturnType
        +CheckForTransitions(StateReturnType) StateReturnType
    }

    class StateReturnType {
        <<struct>>
        +StateReturnTypeKind Kind
        +LogicFrameSpan SleepForFrames
        +Continue$ StateReturnType
        +Success$ StateReturnType
        +Failure$ StateReturnType
        +Sleep(LogicFrameSpan)$ StateReturnType
    }

    class StateConditionInfo {
        <<record struct>>
        +StateTransitionDelegate Test
        +StateId ToStateId
    }

    class AIUpdateStateMachine {
        +Update() StateReturnType
    }

    class IdleState
    class MoveTowardsState
    class AttackState
    class GuardState
    class HuntState
    class WanderState
    class PanicState
    class AttackAreaState
    class AttackTeamState
    class DeadState

    class GuardStateMachine {
        -ObjectId _guardObjectId
        -Vector3 _guardPosition
    }
    class AttackAreaStateMachine

    StateMachineBase <|-- AIUpdateStateMachine
    StateMachineBase <|-- GuardStateMachine
    StateMachineBase <|-- AttackAreaStateMachine
    StateMachineBase "1" o-- "many" State : defines
    State "1" o-- "many" StateConditionInfo
    State ..> StateReturnType : returns
    AIUpdateStateMachine ..> IdleState
    AIUpdateStateMachine ..> MoveTowardsState
    AIUpdateStateMachine ..> AttackState
    AIUpdateStateMachine ..> GuardState
    AIUpdateStateMachine ..> HuntState
    AIUpdateStateMachine ..> WanderState
    AIUpdateStateMachine ..> PanicState
    AIUpdateStateMachine ..> AttackAreaState
    AIUpdateStateMachine ..> AttackTeamState
    AIUpdateStateMachine ..> DeadState
    GuardState *-- GuardStateMachine : nested sub-machine
    HuntState *-- AttackAreaStateMachine : nested sub-machine
```

### 2.3 Player-level AI (mostly stubbed)

```mermaid
classDiagram
    class Player {
        +AIPlayer? AIPlayer
        +CreateFromTemplate(...) Player$
    }

    class AIPlayer {
        <<save/load stub, no behavior yet>>
        -Player _owner
        -List~AIPlayerUnknownThing~ _unknownThings
        +Persist(StatePersister)
    }

    class SkirmishAIPlayer {
        <<save/load stub, no behavior yet>>
        +Persist(StatePersister)
    }

    Player "1" o-- "0..1" AIPlayer
    AIPlayer <|-- SkirmishAIPlayer
```

## 3. Sequence Diagrams

### 3.1 One logic frame: unit AI tick (attack a spotted enemy)

```mermaid
sequenceDiagram
    participant GL as GameLogic (per LogicFrame)
    participant GO as GameObject
    participant AIU as AIUpdate
    participant SM as AIUpdateStateMachine
    participant ST as Current State (e.g. GuardState)
    participant TUR as TurretAIUpdate
    participant NAV as IGameEngine.Navigation
    participant LOC as Locomotor

    GL->>GO: Update()
    GO->>AIU: Update()
    AIU->>SM: Update()
    SM->>ST: Update() / OnEnter()
    ST-->>SM: StateReturnType (Continue/Success/Failure/Sleep)
    SM->>SM: CheckForTransitions() -> maybe SetStateInternal(newState)
    Note over SM,ST: e.g. Guard spots enemy -> transitions to AttackObject state
    SM-->>AIU: done
    AIU->>TUR: Update(autoAcquireEnemiesWhenIdle)
    TUR->>TUR: Scan for target / track / fire weapon
    AIU->>AIU: SetTargetDirection(targetPosition)
    alt TargetPoints queued and no valid weapon target yet
        AIU->>NAV: CalculatePath(start, targetPoint)
        NAV-->>AIU: waypoint list
        AIU->>LOC: move along TargetPoints
    else weapon already has a valid target in range
        AIU->>AIU: StopMovingOnly()
    end
```

### 3.2 Issuing a move/attack order (player or script driven)

```mermaid
sequenceDiagram
    participant Cmd as Player command / Team script
    participant AIU as AIUpdate
    participant SM as AIUpdateStateMachine
    participant NAV as IGameEngine.Navigation

    Cmd->>AIU: SetTargetPoint(destination)
    AIU->>AIU: TargetPoints.Clear()
    AIU->>AIU: AppendPathToTargetPoint(destination)
    AIU->>NAV: CalculatePath(currentPos, destination)
    NAV-->>AIU: path (list of Vector3)
    AIU->>AIU: TargetPoints.AddRange(path)
    Cmd->>SM: SetState(AIStateIds.AttackObject / MoveTo / ...)
    SM->>SM: SetStateInternal(newStateId)
    SM->>SM: currentState.OnExit(), newState.OnEnter()
    Note over SM: Subsequent per-frame Update() calls drive movement/combat
```

### 3.3 Nested sub-state-machine example: Guard behavior

```mermaid
sequenceDiagram
    participant SM as AIUpdateStateMachine
    participant GS as GuardState
    participant GSM as GuardStateMachine
    participant Idle as GuardIdleState
    participant Move as GuardMoveState

    SM->>GS: OnEnter() / Update()
    GS->>GSM: Update()
    GSM->>Idle: Update()
    alt enemy detected within guard radius
        Idle-->>GSM: Success -> transition to Inner/AttackAggressor
        GSM->>SM: (via AttackAggressor path) may bubble up to AttackObject
    else unit wandered too far from guard point
        Idle-->>GSM: Failure -> transition to Return
        GSM->>Move: OnEnter()/Update() (path back to guard position)
        Move-->>GSM: Success -> back to Idle
    end
```

## 4. Current Implementation Status Summary

| Layer | Status | Notes |
|---|---|---|
| `AIUpdate` / locomotion / target points | Implemented | Movement, waypoint following, weapon-direction updates |
| `AIUpdateStateMachine` + `State` framework | Implemented | ~28 states registered; several sub-states still marked `Unknown*` pending full reverse-engineering |
| `TurretAIUpdate` | Partially implemented | State machine (Idle/Scanning/Turning/Attacking/Recentering) works; `FoundTargetWhileScanning` scan logic is stubbed (`return false`) |
| `Pathfinder` (fine-grained terrain classification) | Stub | `ValidMovementTerrain`, `Add/RemoveObjectFromPathfindMap` are `TODO(Port)`; actual routing goes through `IGameEngine.Navigation` |
| `AIPlayer` / `SkirmishAIPlayer` (strategic AI) | Stub (save/load only) | No build order, attack wave, or economy decision-making has been ported |

See `docs/roadmap/` for tracks that plan to build out full strategic AI on top
of this per-unit foundation.
