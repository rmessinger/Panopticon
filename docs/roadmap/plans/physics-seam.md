# Object Physics Seam Plan

> Design authority for the object physics seam and the BepuPhysics2 integration. F4 and the
> WI-PS task cards reference this document for conventions and rationale. The executable
> specification lives in the card steps once F4 starts.
>
> Used by [Track F: Deformable Terrain & Destruction](../track-f-terrain-destruction.md), phase
> F4 onward, and by every system that reads object dynamics: debris, airborne units, unit
> collision response, projectiles.
>
> Audience: human overview of the physics seam design.

---

## Purpose

Replace the hand-rolled per-object integrator with rigid-body simulation behind an engine-internal
seam, migrated incrementally per object category. The seam abstracts one axis: the object dynamics
substrate. Per the [object physics engine decision](../open-questions.md), SAGE behavioral fidelity
is no longer binding for physics, so the scope is all object dynamics, not destruction-only.

Two implementations sit behind the seam:

- Reference: the current `PhysicsBehavior` pipeline (translation-only Euler integration, hand-set
  rotation rates), extracted verbatim from
  `src/OpenSage.Game/Logic/Object/Behaviors/PhysicsBehavior.cs`. Proves the seam by reproducing
  today's behavior exactly.
- Bepu: `BepuPhysics` + `BepuUtilities` 2.4.0, gravity mapped from `GameData` `Gravity = -64`
  dist/sec².

---

## Decisions

| Decision | Value | Reason |
|---|---|---|
| Library | BepuPhysics2 (`BepuPhysics` + `BepuUtilities`) | Pure C#, Apache-2.0 (GPLv3-compatible), headless-capable, needed shapes present (box, sphere, cylinder, convex hull, mesh) |
| Version | Stable 2.4.0 | 2.5.0 is a long-running beta series |
| Incorporation | NuGet package references through central package management | Matches repo convention; no fork or submodule needed |
| Determinism | None asserted for Bepu-backed categories | Cross-run divergence of physics outcomes is acceptable; validation uses loose authored bounds |

---

## Architecture

Two layers. The seam is engine-shaped; SAGE semantics stay in an adapter above it.

1. Seam (`OpenSage.Physics`): rigid-body world and body interfaces. No Bepu types cross it.
2. Adapter (`PhysicsBehavior`): keeps SAGE semantics and translates them onto whichever backend
   a body uses.

| Concern | Layer |
|---|---|
| Integration, gravity, contacts, torque, sleep | Seam backend |
| Motive-force window, lateral force filtering (`ApplyForce` / `ApplyMotiveForce`) | Adapter |
| `StickToGround`, `AllowToFall`, ground-collision relay | Adapter |
| Bounce restitution (`GroundStiffness`), fall-damage thresholds | Adapter |
| Crush levels, collision ignore lists | Adapter, mapped to backend collision filtering |
| `PitchRollYawFactor`, `CenterOfMassOffset` rotation steering | Legacy backend only; Bepu replaces with angular dynamics |

Stepping: `IPhysicsWorld.Timestep` runs once per logic tick at the current physics position
(update Order1, after Order0 steering forces). The legacy backend steps per body from
`PhysicsBehavior.Update` to preserve per-object update order; Bepu steps all bodies in one world
tick.

Backend selection is per object category (debris, projectile, unit) through a physics backend
policy, so migration is incremental and each step is revertible.

### Interface sketch

```csharp
public interface IPhysicsWorld
{
    IPhysicsBody CreateBody(in PhysicsBodyDescription description);
    void RemoveBody(IPhysicsBody body);
    void SetTerrainChunk(int chunkX, int chunkY, ReadOnlySpan<TerrainPhysicsVertex> vertices, ReadOnlySpan<int> indices);
    void Timestep(float deltaTime);
}

public interface IPhysicsBody
{
    Vector3 Position { get; set; }
    Quaternion Rotation { get; set; }
    Vector3 LinearVelocity { get; set; }
    Vector3 AngularVelocity { get; set; }
    float Mass { get; set; }
    bool IsAwake { get; }
    bool IsGrounded { get; }
    void ApplyForce(Vector3 force);
    void ApplyImpulse(Vector3 impulse, Vector3 relativePosition);
}
```

`PhysicsBodyDescription` carries shape (sphere, box, cylinder, convex hull, or triangle list),
mass properties, initial transform, and collision filter. Shape geometry sources: the existing
`Collider` types (`src/OpenSage.Game/Logic/Object/Colliders.cs`) and the W3D mesh collision types
(already parsed, currently unused for physics).

### Terrain colliders

One Bepu static mesh per terrain chunk, chunk grid matching `TerrainPatch` and B4a's chunked
heightmap storage. `SetTerrainChunk` installs or replaces one chunk; deformation (F1/F2) rebuilds
only affected chunks. Adjacent chunks share boundary edge vertices so contacts do not glitch at
seams. Rebuild cost and broadphase overhead with a full-map chunk set are benchmark scenarios.

### Bepu 2.4 implementation notes

Empirical constraints found while building the backend (verified against the 2.4.0 assembly):

- `Box` and `Cylinder` constructors take full extents, not half extents; `Sphere` takes a true
  radius. The seam's `PhysicsShape` uses half extents and converts.
- Mesh triangle winding is opposite the engine's terrain winding; `BepuPhysicsWorld.CreateMesh`
  swaps the last two indices of each triangle so normals point up (+Z).
- At engine world scale (10-unit terrain cells, gravity 64 dist/sec², falling speeds ~40/s), a
  speculative margin of 2.0 world units avoids tunneling; Bepu demo defaults (0.1) miss contacts.
- Sleep threshold 0.3 (velocity units/s) lets stacks island-sleep; lower values keep towers
  permanently awake at a 30 Hz timestep.
- Contact friction is a per-pair constant (0.3) in the narrow-phase callbacks and light angular
  damping (0.98/step) is applied in the pose integrator; without these, sliding objects tumble
  from friction torque. Per-body friction (mapping crush levels) needs a
  `CollidableProperty`-backed callbacks upgrade later.

### Timestep and threading

The 30 Hz logic tick calls `Timestep(1 / 30f)`. The Bepu `Simulation` runs single-threaded first;
an `IThreadDispatcher` is added only if benchmarks show need.

### Determinism, persistence, replay

- No determinism is asserted for Bepu-backed categories, on any machine or build. Cross-run
  divergence of physics outcomes is acceptable.
- Gameplay-decisive reads (fall-damage kills, push-apart displacement feeding pathing) are not
  gated on A2 tolerance definitions; they are validated by loose authored bounds.
- Persistence (F7): resting state per body through `StatePersister` (transform, velocities, sleep
  flag).

---

## Migration phases

| Phase | Scope | Exit criteria | Anchor |
|---|---|---|---|
| PS-0 | Seam carve-out: interfaces plus legacy backend extracted from `PhysicsBehavior` | Zero behavior change: P0 harness run and ReplaySketch screenshot output unchanged | Precedes F4; touches `OpenSage.Game` only |
| PS-1 | Bepu backend plus debris stack test scene, windowed and headless | Falling/stacking scene runs windowed and headless; ms per `Timestep` measured at 100/1k/10k bodies | F4 milestone |
| PS-2 | Debris migration: `ObjectCreationList` `GenericDebris` on Bepu | Debris toss and settle visually equivalent; headless run-and-assert passes with loose bounds | F5 entry |
| PS-3 | Airborne dynamics: toss, fall, bounce, fall damage | Gaps 1, 3, 4, 7 of `docs/system-investigations/physics-system.md` closed by replacement; fall damage reachable | Extends F5 |
| PS-4 | Unit push-apart contacts | Two colliding units exchange momentum and separate; pathfinding stays authoritative | With F6 |
| PS-5 | Ballistics decision point | Choose analytic arcs (keep `BezierProjectileBehavior`) or simulated ballistics; record as a Decision | Optional |

PS-0 is not gated on Track P, A1, or B1: no new runtime dependency, `OpenSage.Game` internal
refactor only. PS-1 onward follows F4's dependencies (headless host A1, benchmark harness B1).

---

## Project layout

```
src/OpenSage.Physics/
  OpenSage.Physics.csproj
  IPhysicsWorld.cs
  IPhysicsBody.cs
  PhysicsBodyDescription.cs
  TerrainPhysicsVertex.cs
  Bepu/BepuPhysicsWorld.cs
  Bepu/BepuPhysicsBody.cs
  Legacy/LegacyPhysicsBody.cs
```

Dependencies: `BepuPhysics`, `BepuUtilities` (2.4.0), `OpenSage.Mathematics`. Referenced by
`OpenSage.Game`, which owns the adapter and the backend policy.

---

## Validation

- Unit tests: adapter semantics against `GameData` gravity conversion, fall-damage threshold
  computable (the current `HeightToSpeed` NaN disappears with real gravity magnitude), bounce
  restitution mapping.
- P0 harness: run-and-assert scenarios per migrated category (debris settle bounds, fall-damage
  outcome).
- ReplaySketch screenshot mode for visual verification of each phase.
- Benchmark (B1 harness): `Timestep` cost at 100/1k/10k bodies, terrain chunk rebuild cost,
  broadphase with a full-map chunk set.
- Stability probe: repeated headless runs of the PS-1 scene assert loose settle bounds
  (settle height, displacement radius); cross-run divergence is acceptable and documented when
  large.

---

## Non-goals

- Replacing locomotor steering or pathfinding; they keep applying forces through the seam.
- Any determinism guarantee, including single-machine run-to-run identity.
- Per-infantry character controllers (revisit after PS-4).
- Bepu 2.5.0-beta features.

---

## Task card index

Full cards are written when a phase's executable specification is needed; landed phases record
their exit evidence here instead.

| Card | Description | Status |
|---|---|---|
| `WI-PS-01` | Seam interfaces and legacy backend carve-out (PS-0) | Complete: `src/OpenSage.Physics` seam + `LegacyPhysicsBody`; physics tests pass; impulse capture trajectories bit-identical before/after |
| `WI-PS-02` | Bepu backend and test scene (PS-1) | Complete: `BepuPhysicsWorld` + `PhysicsStack` built-in scenario; loose settle bounds, stability probe, and ms/Timestep at 100/1k/10k bodies pass headlessly (windowed parity run pending, environment cannot present) |
| `WI-PS-03` | Benchmark scenarios | Partial: ms/Timestep measured in-scenario (0.03/0.1/3.1 ms at 100/1k/10k); B1-harness scenarios (chunk rebuild cost, full-map broadphase) not started |
| `WI-PS-04` | Debris migration (PS-2) | Not started |
| `WI-PS-05` | Airborne dynamics (PS-3) | Not started (a minimal `UpdateBepu` adapter path exists for whole-run backend selection via `CaptureProfile.PhysicsBackend`) |
| `WI-PS-06` | Unit push-apart (PS-4) | Not started |
| `WI-PS-07` | Ballistics decision memo (PS-5) | Not started |
