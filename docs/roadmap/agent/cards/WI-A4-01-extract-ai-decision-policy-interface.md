# Task Card: `WI-A4-01` — Extract `IAIDecisionPolicy` seam, `SkirmishAIPlayer` as reference policy

| Field | Value |
|---|---|
| Card ID | `WI-A4-01` |
| Roadmap phase | [Track A · A4](../../track-a-foundations.md) — Experiment framework seams & harness, AI decision policy seam |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none (P2's `SkirmishAIPlayer` state machine already lands the reference behavior) |
| Diagrams | [Pluggable AI §8](../../deep-dives/pluggable-ai.md#8-architecture-diagrams) |

## Goal

Introduce the `IAIDecisionPolicy` / `AIDecisionContext` seam described in
[deep-dives/pluggable-ai.md §2](../../deep-dives/pluggable-ai.md#2-interface-shape-design-not-implementation),
and refactor `SkirmishAIPlayer`'s existing state machine to be the reference implementation
(`RuleBasedAIDecisionPolicy`) behind it — with **no observable behavior change**.

## Context to read (and only this)

- `docs/roadmap/deep-dives/pluggable-ai.md` §§1-2, 5 — the seam design this card implements.
- `docs/roadmap/deep-dives/pluggable-ai.md` §8 — architecture/sequence diagrams to follow exactly.
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs` — current host: `Update()`, `OnDecisionTick()`
  (`protected virtual`), `EnqueueOrder(s)`, `DispatchOrders(...)`, `Persist(...)`. Preserve
  `Persist` byte-for-byte; do not touch save-format fields.
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs` — reference state machine:
  `OnDecisionTick()` calls `RunEconomyAndBaseBuildout()` → `RunUnitProduction()` →
  `RunMilitaryBehavior()`, reading `Owner.BankAccount`, `Owner.OwnedObjects`, `Owner.Enemies`,
  `Game.AssetStore.AIData`.
- `src/OpenSage.Game/Logic/Player.cs:763-777` — where `SkirmishAIPlayer` vs. `AIPlayer` is
  constructed; do not change this selection logic in this card (that is `WI-A4-02`).
- Existing P2 tests that must keep passing unmodified in behavior:
  `src/OpenSage.Game.Tests/Logic/AI/SkirmishAIEconomyBuildoutTests.cs`,
  `SkirmishAIMilitaryBehaviorTests.cs`, `SkirmishAIUnitProductionTests.cs`,
  `SkirmishAIPlayerTickTests.cs` (find via glob under `src/OpenSage.Game.Tests/Logic/AI/`).

## Steps

1. Add `src/OpenSage.Game/Logic/AI/IAIDecisionPolicy.cs` defining the interface exactly as
   shaped in the deep-dive §2 (one method: `DecideNextOrders(AIDecisionContext context)` returning
   `IReadOnlyList<Order>`).
2. Add `src/OpenSage.Game/Logic/AI/AIDecisionContext.cs` as a `readonly struct` wrapping the
   read-only state today's `SkirmishAIPlayer` methods read (`Owner`, `OwnerPlayerIndex`,
   `DecisionTickCount`, plus any other accessor the extracted methods need — mirror what they
   already read, add nothing new).
3. Change `AIPlayer.OnDecisionTick()` from a `protected virtual` hook to a dispatcher that, if an
   injected `IAIDecisionPolicy` is present, builds an `AIDecisionContext` and calls
   `EnqueueOrders(policy.DecideNextOrders(context))`. Keep it `protected virtual` still overridable
   for now so no caller breaks, but wire the default path through the policy.
4. Add `src/OpenSage.Game/Logic/AI/RuleBasedAIDecisionPolicy.cs` implementing
   `IAIDecisionPolicy` by moving `RunEconomyAndBaseBuildout`, `RunUnitProduction`,
   `RunMilitaryBehavior` (and their private helpers) out of `SkirmishAIPlayer` into this new
   class, adapted to read from `AIDecisionContext` instead of directly from `Owner`/`Game`.
5. Update `SkirmishAIPlayer` to construct a `RuleBasedAIDecisionPolicy` and pass it to the base
   `AIPlayer` (constructor parameter or settable property — match whatever is least invasive to
   `Player.cs`'s construction call). `SkirmishAIPlayer.DecisionTickCount` must still increment
   exactly as before.
6. Do not change `Persist(...)` on either class. If the moved methods reference any state that
   was previously an instance field of `SkirmishAIPlayer` (e.g. `_loggedMissingBuildListWarning`),
   move the field to `RuleBasedAIDecisionPolicy` alongside the method, not to `AIDecisionContext`.
7. Run `dotnet format src` on new/changed files; confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/OpenSage.Game/Logic/AI/IAIDecisionPolicy.cs` (new)
- `src/OpenSage.Game/Logic/AI/AIDecisionContext.cs` (new)
- `src/OpenSage.Game/Logic/AI/RuleBasedAIDecisionPolicy.cs` (new)
- `src/OpenSage.Game/Logic/AI/AIPlayer.cs`
- `src/OpenSage.Game/Logic/AI/SkirmishAIPlayer.cs`

## Acceptance criteria

- [ ] `IAIDecisionPolicy`/`AIDecisionContext` exist exactly matching the shape in
      pluggable-ai.md §2 (one decision method, read-only context).
- [ ] `SkirmishAIPlayer`'s economy/production/military logic is unchanged in behavior — every
      existing P2 test (`SkirmishAIEconomyBuildoutTests`, `SkirmishAIMilitaryBehaviorTests`,
      `SkirmishAIUnitProductionTests`, `SkirmishAIPlayerTickTests`) passes with no test edits.
- [ ] `AIPlayer.Persist(...)` and `SkirmishAIPlayer.Persist(...)` (if any) byte layout is
      unchanged — no new persisted fields, no reordering.
- [ ] Actuation is unchanged: orders still flow through `AIPlayer.EnqueueOrder(s)` →
      `DispatchOrders(...)` → `OrderProcessor.Process(...)`.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~SkirmishAI"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add policy-selection config or a factory (that is `WI-A4-02`).
- Do not implement a neural-network policy here (that is `WI-A4-03`).
- Do not change `Player.CreateNew(...)`'s `SkirmishAIPlayer` vs. `AIPlayer` selection.
- Do not change any persisted save-format field, order, or default.
- If the extraction requires reading state `AIDecisionContext` does not obviously expose,
  extend the context minimally to mirror it — do not reach back into `Owner`/`Game` directly
  from `RuleBasedAIDecisionPolicy` methods once extracted.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Update the A4 phase's AI-decision-policy sub-note in [`track-a-foundations.md`](../../track-a-foundations.md)
  if this card changes its state meaningfully (phase-level Status stays `Not started` — A4 covers
  more than AI).
- Append a result line to repo-root `.exchange`.
- Follow-up cards to create/unblock: `WI-A4-02` (policy-selection config + factory) becomes
  `Ready` once this card is `Done`.
