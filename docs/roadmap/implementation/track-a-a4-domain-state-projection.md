# Track A · A4-4: Domain state projection

> Audience: human overview above the divider; agent-context detail below it.

Implements checklist item A4-4 of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a4-4-domain-state-projection.md](../deep-dives/a4-4-domain-state-projection.md),
including its [architecture diagrams](../deep-dives/a4-4-domain-state-projection.md#8-architecture-diagrams).

## Scope

Add an optional `IDomainStateProjection` beside `ISimulationDomainModel`: a versioned schema,
keyed multi-field state records, delta diff/apply, a separate `*.domainstate.bin` artifact, and a
schema-driven integer conservation check. The RTS recording path and its JSON stay unchanged.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| A4-4a | [`WI-A4-42`](../agent/cards/WI-A4-42-domain-state-contracts.md) | Schema, field, record contracts and validation. |
| A4-4b | [`WI-A4-43`](../agent/cards/WI-A4-43-domain-state-frames.md) | Frame persistence, diff/apply, reconstruction, conservation residuals. |
| A4-4c | [`WI-A4-44`](../agent/cards/WI-A4-44-domain-state-recorder.md) | `DomainStateRecorder`, artifact, `RunRecording` and `RunOutput` wiring. |
| A4-4d | [`WI-A4-45`](../agent/cards/WI-A4-45-transport-state-projection.md) | Transport-graph reference projection and conservation test. |

Order: 42, 43, 44, 45. `WI-W0-02` depends on 42; `WI-W0-07` depends on 44.

---

## Agent context

## File map

| Card | New files | Edited files |
|---|---|---|
| `WI-A4-42` | `src/Panopticon.Simulation/Simulation/State/` contracts | none |
| `WI-A4-43` | `State/` frame, delta, conservation types | none |
| `WI-A4-44` | `State/DomainStateRecorder.cs`, artifact writer and loader | `Recording/RunRecording.cs`, `RunOutput.cs` |
| `WI-A4-45` | none | `Simulation/Transport/TransportGraphDomainModel.cs` |

## Invariants

- `RunRecording.Attach` creates a `DomainStateRecorder` only when the host's domain implements
  `IDomainStateProjection`; the artifact is written only when `domainStateArtifactPath` is set.
- New `RunOutput` fields use `JsonIgnore(Condition = WhenWritingNull)`, so RTS run-output JSON is
  byte-identical to the pre-A4-4 output.
- Conserved fields are `Integer`; `DomainConservation.Residuals` reads only the schema.
- The recorder samples on `WorldStateSampler.FrameSampled`, so paused frames are skipped.

Code hooks: see the deep dive's
[appendix](../deep-dives/a4-4-domain-state-projection.md#appendix-current-state-hooks-agent-context).
