# Task Card `WI-P6-06`: Ground the jet crash lifecycle

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-P6-06` |
| Roadmap phase | [P6 corrective follow-up](../../track-p-playable-baseline.md#phase-resources) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Deliverable populates the [jet lifecycle design gate](../../plans/macos-battle-artifacts.md#jet-lifecycle-design-gate) |

## Goal

Define a bounded, source-grounded implementation contract that replaces perpetual
flight of dead jets with a crash and final removal.

## Context to read (and only this)

- [Evidence and reproduction contract](../../plans/macos-battle-artifacts.md).
- `src/Panopticon.Simulation/Logic/Object/Behaviors/JetSlowDeathBehavior.cs`
- `src/Panopticon.Simulation/Logic/Object/Behaviors/SlowDeathBehavior.cs`
- `src/Panopticon.Simulation/Logic/Object/GameObject.cs`: `Die` and module dispatch.
- `src/Panopticon.Simulation/Logic/Object/Update/AIUpdate/JetAIUpdate.cs`
- `src/Panopticon.Simulation/Logic/Object/Locomotor.cs`: dead-locomotor behavior.
- `src/Panopticon.Simulation/Logic/Object/Behaviors/PhysicsBehavior.cs`: physics and contact ownership.
- `test/Panopticon.Simulation.Tests/Logic/Object/Update/JetAIUpdateTests.cs`
- `test/Panopticon.Simulation.Tests/Logic/Object/Update/JetAIUpdateAttackTests.cs`
- Real installation: Aurora/Raptor/MiG object definitions, locomotors, jet death FX and OCLs.

## Steps

1. Reproduce airborne lethal damage with the plan's Quad Cannon/Aurora scene. Track
   logical death separately from the dying object's presence, altitude, and removal.
2. Trace the inherited module factory, death flags, movement after death, airfield
   cleanup, and physics integration. Record exact existing types and extension points.
3. Define airborne and grounded death states, FX/OCL ordering, terrain-contact
   detection, roll/pitch/fall-rate interpretation, and delay-unit conversions from
   source data. Consult original/reference behavior where available; mark unresolved
   semantics explicitly instead of inventing them.
4. Document logical-death notification versus final wreck removal, once-only cleanup,
   legacy/Bepu compatibility, and required checkpoint state. Populate class and sequence
   diagrams in the plan with existing and proposed types clearly distinguished.
5. Reconcile WI-P6-07's file list, tests, and scope. Promote it to `Ready` only with
   a resolved contract and populated diagrams. Split additional cards if the runtime
   work is no longer a small bounded change. WI-G6-03 must also be `Done` before
   WI-P6-07 is executable.

## Files likely to change

- `docs/roadmap/plans/macos-battle-artifacts.md`
- `docs/roadmap/agent/cards/WI-P6-07-implement-jet-crash-lifecycle.md`
- This card, matching checklist item, and plan status table.

## Acceptance criteria

- [ ] Reproduction records both logical death and the lingering renderable object.
- [ ] Every crash field has an evidenced interpretation or an explicit blocker.
- [ ] Class and sequence diagrams cover airborne death, grounded death, impact, final delay, and removal.
- [ ] Ownership, airfield, physics, and checkpoint responsibilities have named owners.
- [ ] WI-P6-07 is reconciled: `Ready` when both prerequisites are done, `Draft` while the clamp is pending, or `Blocked` with a concrete design reason.

## Validation commands

Read-only source/data audit and TestHarness reproduction. No source edits or build
are required for this investigation. Use the existing capture/profile machinery;
temporary profiles and generated images remain outside the repository.

## Out of scope / guardrails

- No runtime implementation, recoloring of wreck textures, or broad death-system refactor.
- A capture's `ActorDestroyed` result alone is not proof of final object removal.
- Do not assume every dark jet is alive or that every negative-alpha template executes.

## On completion

- Set Status to `Done`, update the plan table and corrective checklist item.
- Append the design result and blockers to `.exchange`; retain the shipped P6 milestone.
