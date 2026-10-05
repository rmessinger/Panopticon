# Task Card `WI-H2a-17`: Prove mixed-source units and hand off M2

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-17` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-H2a-14`, `WI-H2a-16` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Prove a real imported EE2 unit fights beside Generals through native engine behavior and document the M2 expansion boundary.

## Context to read (and only this)

- H2a deep dive: reviewed mixed-source scenario and publication/failure assertions.
- `src/Panopticon.Tools.TestHarness/ValidationGroundHost.cs`, `ValidationGroundRunner.cs`.
- `docs/roadmap/deep-dives/p9-unit-validation-ground.md`, `docs/screenshots.md`.
- Cards 14/16's landed tests and exact ReplaySketch/P9 integration seams identified by card 02.
- `docs/roadmap/track-m-mass-battle.md`: M2 scope.

## Steps

1. Add the shared-layer content inputs to an existing P0/P9 scenario with one EE2 unit and Generals references.
2. Assert source-derived health/combat values and spawn/move/attack/damage/die outcomes; verify idle/move/attack/death visual bindings in a windowed proof.
3. Include an unsupported-mechanic failure and a colliding-name fixture; verify no partial content and unaffected Generals definitions.
4. Record installation prerequisites, evidence paths, capabilities, conversions, and exact M2 follow-ups; reconcile existing M2 cards if present.

## Files likely to change

- Existing P9 content-input seam named by card 02; proposed mixed-source scenario in `src/Panopticon.Tools.TestHarness/`.
- Proposed `test/Panopticon.Tools.ReplaySketch.Tests/UnitContentImportIntegrationTests.cs`.
- `docs/roadmap/deep-dives/h2a-unit-content-import.md` and M2 handoff/resource entries.
- Any touched ReplaySketch UI follows its local AGENTS.md and remains a thin Core-backed shell.

## Acceptance criteria

- [ ] Real EE2 and Generals definitions coexist and use the same resolver/materializer/publication path.
- [ ] Source values and explicit runtime conversions are recorded; combat changes actual health and causes native death.
- [ ] Negative imports leave no content residue; required visual states are demonstrated.
- [ ] Missing operator installs block the real proof rather than count as a pass.
- [ ] Standard validation passes; ReplaySketch changes have screenshot-mode captures for the affected panel and the map panel.
- [ ] All H2a cards are complete before H2a is marked Done and M2 is unblocked.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~UnitContentImportIntegrationTests"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Set `EMPIRE_EARTH_II_PATH` to the operator's install for the real proof. Card 02 supplies exact scenario and screenshot commands before Ready.

## Out of scope / guardrails

No 1000-unit battle, aircraft expansion, balancing, or new harness. Keep proprietary assets out of commits. Preserve artifacts and stop on determinism-test deviations under repository policy.

## On completion

Update Status and H2a-17. Mark H2a Done only after checking all cards/checklist items; update README progress and M2 handoff. Append validation and remaining M2 work to `.exchange`.
