# Task Card `WI-A5-06`: Ground A5 architecture and artifact contracts

| Field | Value |
|---|---|
| Card ID | `WI-A5-06` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): Scenario authoring and visualization |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | Not applicable: design card that replaces the [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) placeholders. |

## Goal

Ground the scenario, portable run bundle, indexed replay, matrix comparison, and T1-to-T2
inspection contracts in the landed A1/P0 implementation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a5-visualization.md`.
- `docs/roadmap/deep-dives/a1-headless-host.md` and `p0-playable-baseline-harness.md`.
- `src/OpenSage.Game/RunOutput.cs` and `Recording/`.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`, `ScenarioMatrixRunner.cs`.
- `src/OpenSage.Tools.ReplaySketch.Core/Model/ReplayScenario.cs`, `ScenarioMatrix.cs`.

## Steps

1. Correct §5/§8 to the landed T0 metrics, T1 signature trees, T2 triggered detail, and separate
   snapshot/delta state artifact.
2. Specify scenario and run-bundle versions, relative paths, event format, compatibility checks,
   and frame-zero reconstruction.
3. Specify capture-from-run as a deliberately lossy projection of recorded scenario-initializable
   fields, plus matrix binding syntax and the A4 domain-view contract.
4. Replace §8 with grounded class and sequence diagrams for author, run, load/seek/render/export,
   matrix comparison, and divergence detail.
5. Create and index `implementation/track-a-a5-scenario-visualization.md`.

## Files likely to change

- `docs/roadmap/deep-dives/a5-visualization.md`
- `docs/roadmap/implementation/track-a-a5-scenario-visualization.md` (new)
- `docs/roadmap/implementation/README.md`
- `docs/roadmap/track-a-foundations.md`

## Acceptance criteria

- [ ] Diagram and tier terminology matches current code.
- [ ] All artifact schemas and compatibility failures are explicit.
- [ ] Capture, matrix, domain projection, and T2 inspection contracts are fixed.
- [ ] Dependent cards name exact planned symbols and files.

## Validation commands

```sh
cd docs/roadmap
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/a5-visualization.md -o /tmp/a5.svg
rm -f /tmp/a5*.svg
```

## Out of scope / guardrails

No source changes or UI mockups.

## On completion

- Set Status above to `Done` or `Blocked`.
- Remove the A5 diagram `†`.
- Append a result line to `.exchange`.
