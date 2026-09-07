# Task Card `WI-A4-17`: Ground the hierarchical-AI design

| Field | Value |
|---|---|
| Card ID | `WI-A4-17` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md): A4-3 Decentralized hierarchical AI seam |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-A4-04` |
| Diagrams | Not applicable: design card that replaces the [A4 hierarchy §9](../../deep-dives/a4-decentralized-hierarchical-ai.md#9-architecture-diagrams) placeholders. |

## Goal

Resolve the current squad source, commander ranking, cadence, persistence, and soldier-actuation
ambiguities so A4-3a through A4-3f can be implemented mechanically.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md`.
- `src/OpenSage.Game/Logic/Team.cs`, `TeamFactory.cs`, `TeamTemplate.cs`.
- `src/OpenSage.Game/Logic/SelectionSystem.cs`, `Player.cs`, and `GameLogic.cs`.
- `src/OpenSage.Game/Logic/AI/AIUpdateStateMachine.cs` and its state classes.
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs` and `RuleBasedAIDecisionPolicy.cs`.

## Steps

1. Verify whether simulation-owned `Team` data can supply durable squads; do not use
   presentation-owned selection state.
2. Fix commander scoring, tie-breaking, membership lifecycle, per-rank cadence, and save ownership.
3. Resolve whether leaf decisions execute inside `AIUpdateStateMachine` or are returned by the
   top-level adapter; keep exactly one actuation path.
4. Replace §9 with diagrams grounded in current and explicitly named planned types.
5. Update the sub-phase text and open-question decisions.

## Files likely to change

- `docs/roadmap/deep-dives/a4-decentralized-hierarchical-ai.md`
- `docs/roadmap/open-questions.md`
- `docs/roadmap/implementation/track-a-a4-hierarchical-ai.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every ambiguity above has one documented decision.
- [ ] Diagrams show one order-actuation owner and all three cadences.
- [ ] Planned types map to exact current integration symbols.
- [ ] A4-3a through A4-3d have no unresolved implementation choices.

## Validation commands

```sh
cd docs/roadmap
npx -y @mermaid-js/mermaid-cli@latest -i deep-dives/a4-decentralized-hierarchical-ai.md -o /tmp/a4-hierarchy.svg
rm -f /tmp/a4-hierarchy*.svg
```

## Out of scope / guardrails

Do not decide per-rank network shapes or co-evolution; later cards own those contracts.

## On completion

- Set Status above to `Done` or `Blocked`.
- Remove the hierarchy diagram `†`.
- Append a result line to `.exchange`.
