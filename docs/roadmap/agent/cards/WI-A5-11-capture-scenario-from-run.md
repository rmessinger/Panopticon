# Task Card `WI-A5-11`: Capture a scenario from a recorded frame

| Field | Value |
|---|---|
| Card ID | `WI-A5-11` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-1 capture-from-run |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A5-03`, `WI-A5-08`, `WI-A5-09` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Project the recorded scenario-initializable fields from a reconstructed frame into a new scenario
artifact. The result is a documented lossy projection, not a continuation-equivalent save.

## Context to read (and only this)

- Capture semantics in the A5 implementation plan.
- Versioned `ReplayScenario` from `WI-A5-03`.
- Indexed run-state cursor from `WI-A5-08`.
- Bundle reader from `WI-A5-09`.

## Steps

1. Add the capture service and projection result diagnostics.
2. Reconstruct frame N and project definition, owner, position, health, and player resources.
3. Apply the documented timeline rebasing rule.
4. Preserve domain, topology, parameters, seed policy, stop conditions, and probes.
5. Add run-to-capture-to-serialize-to-reload tests over the fields present in
   `WorldStateSummary`; identify omitted transient/module state in capture metadata.

## Files likely to change

- New service under `src/Panopticon.Tools.ReplaySketch.Core/Services/`
- Scenario model extensions if required by the design
- New ReplaySketch tests

## Acceptance criteria

- [ ] Definition, owner, position, health, max health, and player money match frame N.
- [ ] Timeline commands follow the documented rebase/drop rule.
- [ ] The captured scenario serializes and validates.
- [ ] Reloading the captured artifact reproduces the projected initial fields; no behavioral
      continuation equivalence is claimed.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~ScenarioCaptureFromRun"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not claim or synthesize orientation, upgrades, veterancy, queues, teams, module state, or other
fields absent from `WorldStateSummary`.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
