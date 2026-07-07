# Task Card: `WI-A4-02` — AI policy-selection config + factory

| Field | Value |
|---|---|
| Card ID | `WI-A4-02` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md) — Experiment framework seams & harness, AI decision policy seam |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-01` (Done) — `IAIDecisionPolicy`/`RuleBasedAIDecisionPolicy` must exist first |
| Diagrams | [Pluggable AI §8](../../deep-dives/pluggable-ai.md#8-architecture-diagrams) |

## Goal

Add a policy-identifier config value alongside the existing skirmish/`PlayerAIType` slot
configuration, and a small factory that resolves it to an `IAIDecisionPolicy`, defaulting to
`RuleBasedAIDecisionPolicy` so no existing skirmish setup changes behavior. This is the
"config-selected, no caller changes" mechanism the deep-dive §2.1 describes and that the future
neural-network policy (`WI-A4-03`) plugs into without touching `Player`/`AIPlayer` again.

## Context to read (and only this)

- `docs/roadmap/deep-dives/pluggable-ai.md` §2.1 — selection design.
- `src/OpenSage.Game/Logic/Player.cs:763-777` — current `SkirmishAIPlayer` vs. `AIPlayer` vs.
  `null` selection based on `isHuman`/`isSkirmish`/`side`, in `Player.CreateNew(...)`.
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs`, `RuleBasedAIDecisionPolicy.cs`,
  `IAIDecisionPolicy.cs` (all added by `WI-A4-01`) — the interface/constructor shape to plug the
  factory output into.
- Look for how other config-selected seams in this codebase already do this (e.g. search for
  existing `*Type` enum + factory pairs referenced from `seams.md`'s Experiment seams table) to
  match the established pattern rather than inventing a new one.

## Steps

1. Add an `AIDecisionPolicyType` (or similarly named) enum with at least `RuleBased` (default).
   Do not add a `NeuralNetwork` member yet — `WI-A4-03` adds that when the implementation exists.
2. Add a small factory (e.g. `AIDecisionPolicyFactory.Create(AIDecisionPolicyType, Player owner)`
   returning `IAIDecisionPolicy`) that currently only produces `RuleBasedAIDecisionPolicy`.
3. Thread the policy-identifier value through wherever skirmish slot config already flows into
   `Player.CreateNew(...)` (map/skirmish settings), defaulting to `RuleBased` so behavior is
   unchanged for every existing scenario/map/test.
4. Wire `Player.CreateNew(...)` to call the factory and pass the resulting policy into
   `SkirmishAIPlayer`'s (or `AIPlayer`'s) constructor instead of `SkirmishAIPlayer` constructing
   its own `RuleBasedAIDecisionPolicy` internally (adjust `WI-A4-01`'s wiring accordingly, without
   changing its persistence behavior).
5. Add a headless test proving: (a) default config yields `RuleBasedAIDecisionPolicy` behavior
   identical to pre-`WI-A4-01` skirmish AI, (b) the factory is the only place that maps identifier
   → implementation (no caller-side `if`/`switch` on policy type elsewhere).
6. Run `dotnet format src` and confirm `--verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyType.cs` (new)
- `src/OpenSage.Game/Logic/AI/AIDecisionPolicyFactory.cs` (new)
- `src/OpenSage.Game/Logic/Player.cs`
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs` (constructor signature change only)
- Test file under `src/OpenSage.Game.Tests/Logic/AI/` (new)

## Acceptance criteria

- [ ] A config value selects the policy; default is `RuleBased` and matches pre-existing
      skirmish AI behavior exactly (no test regressions).
- [ ] Exactly one factory resolves the identifier to an implementation; no other call site
      branches on policy type.
- [ ] No change to `Player.CreateNew(...)`'s existing `isHuman`/`isSkirmish`/`side` branching for
      *which* AI class (`SkirmishAIPlayer`/`AIPlayer`/`null`) is created — only which policy that
      class is handed.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~AIDecisionPolicy"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not implement a neural-network policy or add a `NeuralNetwork` enum member with no backing
  implementation — enum members must correspond to real, working implementations.
- Do not change which slots get an `AIPlayer` at all (human vs. AI slot assignment is a P1
  concern, not this card's).
- If the existing skirmish slot config storage format is ambiguous or would require a save-format
  version bump to add the identifier, stop and mark `Blocked` — flag it in `.exchange` for a
  design decision rather than guessing at a save-compat shortcut.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.
- Follow-up: `WI-A4-03` (first neural-network policy) becomes `Ready` once this card is `Done`.
