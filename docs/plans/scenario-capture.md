# Generalized Scenario Capture Plan

Status: planned; last updated 2026-08-27.

## Problem and approach

`EngagementCaptureRunner` currently combines scenario setup, attack-specific execution,
camera fitting, frame capture, manifest generation, and engagement validation. Its options,
manifest, annotator, exit conditions, and built-in scenario assume an attacker, a target, and
a projectile.

Replace the hard-coded core with a compact, declarative capture profile and a generic runner.
The profile describes a small set of actors, a camera fixed after setup, frame-scheduled
commands, tracked state, and success expectations. The existing `EngagementCapture` entry
point and configuration remain supported through an adapter that builds an equivalent generic
profile.

## Design

### Capture profile

Add the serializable profile model and validation in
`OpenSage.Tools.ReplaySketch.Core`, alongside the existing scenario models and loaders:

- Scenario metadata: name, map, fixed-timestep duration, settle frames, capture stride, output
  directory, and HUD visibility.
- Players: stable string IDs, faction/player settings, and starting money. Actor ownership and
  commands reference these IDs, never raw `Scene3D.Players` indices. Reserve a `civilian` owner
  for non-retaliating or neutral subjects.
- Actors: stable ID, definition name, owner ID, world position, facing, label/role, tracking
  flag, and optional health override.
- Camera:
  - Explicit mode: configured look-at point and height.
  - Auto-fit mode: a one-time fit over selected actors plus optional framing points for build
    sites or expected trajectories.
  - Both modes apply height before look-at, verify the configured field projects inside the
    safe viewport, and never move the camera after capture starts.
- Commands scheduled by capture-relative logic frame, executed in declaration order before
  that frame's simulation tick:
  - `Attack`: source actor and target actor, using direct weapon targeting to preserve the
    engagement runner's stationary framing behavior.
  - `Construct`: builder actor, building definition, position, facing, and result actor ID.
  - `ApplyImpulse`: actor and mass-independent delta velocity. The executor converts it to
    `PhysicsBehavior.ApplyForce(deltaVelocity * Mass)`.
- Expectations and stop conditions: projectile observed, actor damaged or destroyed,
  construction progressed or completed, and minimum actor displacement. Failures are explicit
  rather than allowing an idle capture to look successful.

Movement, arbitrary engine orders, camera animation, branching, and general scripting remain
outside the first command set. ReplaySketch remains the tool for larger command sequences.

### Runtime

Create a generic `ScenarioCaptureRunner` in `OpenSage.Tools.TestHarness`. Separate profile
loading and validation, scene setup, command execution, camera setup, state observation, and
artifact writing.

- Resolve player IDs to actual `Player` objects. Use `Scene3D.GetPlayerIndex` when creating
  orders.
- Spawn setup actors at terrain height, update colliders immediately, disable AI, use a fixed
  logic timestep, and make victory inert.
- Keep setup timing deterministic. Camera fitting renders without consuming a variable number
  of logic ticks. Any required warm-up is a fixed part of the settle period.
- For construction, verify the builder has `IBuilderAIUpdate`, verify funds, select exactly that
  builder through the public selection system, and dispatch `Order.CreateBuildObject`. Capture
  the synchronously-created object by owner, definition, and requested position and bind it to
  the command's result ID. Fail if construction never advances or completes when required.
- For impulses, reject actors without `PhysicsBehavior`, record the starting position, and fail
  an unmet displacement expectation. The example uses an object and impulse direction that
  motive-force filtering does not suppress.
- Step one logic frame at a time. Execute due commands, update simulation, then render and
  capture according to the configured stride. Record both capture index and absolute and
  capture-relative logic frames.
- Clean only artifacts owned by the capture runner in the selected output directory.

### Manifest and annotation

Evolve `manifest.json` additively so `scripts/annotate_engagement_gif.py` continues to work
unchanged:

- Retain `ViewportWidth`, `ViewportHeight`, `Frames`, `Actors`, `Projectiles`, and the existing
  actor fields and roles.
- Add schema version, scenario name, logic-frame metadata, stable actor ID, world position,
  velocity, construction state/progress, and command/expectation results.
- Keep projectiles as transient records. Dynamically constructed objects appear in `Actors`
  after their result ID is bound.
- Add `scripts/annotate_capture_gif.py` for generic cropped GIFs and labels without
  engagement-specific target-health overlays. Keep the existing engagement annotator as the
  specialized compatibility path.

### Compatibility and examples

- Keep `BuiltInScenario.EngagementCapture`, `EngagementCaptureOptions`, its default output
  directory, CLI, specialized annotator, and exit behavior.
- Reduce `EngagementCaptureRunner` to an adapter that translates legacy options into a generic
  profile containing attacker and target actors, one `Attack` command, target-destroyed stop
  behavior, and the projectile expectation.
- Add `BuiltInScenario.ScenarioCapture`. For this scenario, `ScenarioPath` and `--scenario` load
  a capture profile rather than a replay profile.
- Add checked-in TestHarness profiles for:
  - A dozer constructing one building in a compact field, tracked from placement through
    completion.
  - A physics-enabled object receiving an impulse and moving through a pre-framed field.
- Update `TestHarness.json`, `src/OpenSage.Tools.TestHarness/AGENTS.md`, and
  `docs/screenshots.md` with the generic invocation, profile contract, examples, manifest
  fields, and failure behavior. Historical investigation documents remain unchanged because
  the engagement compatibility path stays valid.

## Implementation sequence

1. Define the capture profile, polymorphic command and expectation models, JSON loader, and
   structural validation in ReplaySketch.Core.
2. Implement deterministic scene setup, fixed and auto-fit camera setup, command scheduling,
   dynamic actor binding, observation, expectations, and additive manifest output in the
   generic TestHarness runner.
3. Adapt `EngagementCaptureOptions` and `EngagementCaptureRunner` to the generic runner while
   preserving current defaults, artifact shape, CLI, annotator, and exit semantics.
4. Add construction and physics capture profiles plus the generic GIF annotator.
5. Add pure tests for profile serialization, validation, scheduling, camera framing inputs,
   manifest compatibility, and legacy profile conversion.
6. Add serial real-install tests for construction progress/completion and impulse displacement.
   Run the engagement and both new profiles headlessly and inspect their PNGs and manifests.
7. Update TestHarness configuration and capture documentation. Run the repository build,
   non-interactive tests, and format check.

## Acceptance criteria

- The existing `EngagementCapture` command still produces an engagement that the existing
  annotator can consume.
- A JSON capture profile produces a fixed-camera construction sequence and binds the newly
  created building to a stable tracked ID.
- A JSON capture profile applies a mass-independent impulse and records observable position and
  velocity changes.
- The camera is configured once before capture, covers the declared small field, and remains
  unchanged for every recorded frame.
- Only the three documented commands are accepted. Invalid references, definitions, owners,
  builders, physics targets, camera fields, and unmet expectations fail with actionable errors.
- Manifests contain sufficient generic state for construction and physics analysis without
  removing fields required by the engagement annotator.
- Targeted tests, `dotnet build src`, `dotnet test src --filter Category!=Interactive`, and
  `dotnet format src --verify-no-changes` pass.

## Notes

- Construction duration includes builder approach plus `ObjectDefinition.BuildTime`. Capture
  stride prevents long builds from producing excessive PNG counts.
- Auto-fit uses an N-subject bounding field rather than the current two-point span. Explicit
  framing points cover destinations and trajectories not represented by initial actor
  positions.
- Direct `Attack` intentionally bypasses the normal `AttackObject` movement order. This
  preserves the existing close-up engagement behavior and is capture-specific.
