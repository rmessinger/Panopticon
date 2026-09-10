# Task Card `WI-A4-16`: Integrate neural-policy training with headless matches

| Field | Value |
|---|---|
| Card ID | `WI-A4-16` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): AI-policy experiment follow-on |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A4-15` |
| Diagrams | [Pluggable AI §8](../../deep-dives/a4-pluggable-ai.md#8-architecture-diagrams) |

## Goal

Prove supplied genomes execute through the real headless policy path and produce a generation
report consumable by A5.

## Context to read (and only this)

- Neural evolution runner and policy configuration from `WI-A4-12` through `WI-A4-15`.
- `src/OpenSage.Tools.ReplaySketch.Core/HarnessRunner.cs`.
- Existing install-backed AI and scenario-matrix tests.
- `docs/roadmap/deep-dives/a5-visualization.md`: report consumer.

## Steps

1. Add the real match evaluator adapter around the existing headless harness.
2. Add a non-interactive plumbing test using a controlled evaluator and full configuration path.
3. Add an install-backed small-population smoke test over a short match.
4. Assert generated report identity, genome selection, seeds, and outcomes.

## Files likely to change

- `src/OpenSage.Tools.ReplaySketch.Core/`
- `src/OpenSage.Tools.ReplaySketch.Tests/`

## Acceptance criteria

- [ ] Neural weights reach the selected player through the factory.
- [ ] Match outcomes and T0 metrics feed the scorer.
- [ ] A generation report is written even when one evaluation fails, with explicit failure state.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~NeuralPolicyEvolution"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~NeuralPolicyEvolution&Category=Interactive"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not tune for policy quality or add A5 UI.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
