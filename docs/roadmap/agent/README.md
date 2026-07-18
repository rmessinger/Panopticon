# Agent Guide: incremental roadmap iteration

> A parallel, agent-focused companion to the [roadmap](../README.md). The roadmap states what
> to build and why; this guide states how a low-effort automated agent picks up a small, bounded
> slice and completes it safely without requiring a high-effort model to re-reason the whole design.
>
> Audience: agent-focused workflow. Human readers wanting a project overview should start with the
> [roadmap README](../README.md) and [track docs](../README.md#document-map).

---

## Why this exists

Roadmap phases (P, A, B, C) are large and design-heavy. Execution at that scale requires strong
reasoning. Most phases decompose into many small, mechanical, low-ambiguity steps: cataloging
code, replicating an existing pattern, adding a test, or applying a narrow refactor. Those steps do
not require a high-effort model when they are written down precisely. This guide stores them as task
cards.

A good task card is self-contained: a cheap agent can execute it by reading the card and the exact
files it names, without roadmap-wide context.

---

## The iteration loop

Every agent working from this guide follows the same loop:

1. Pick a card. Take one card from [`cards/`](cards/) whose Status is `Ready` and whose
   listed prerequisites are met. Do one card at a time.
2. Read only what the card names. The card lists the exact files / line ranges and the roadmap
   phase it serves. Read those. Do not go spelunking the whole codebase.
3. Make the change. Keep the diff small and surgical: scoped to the card's "Files likely to
   change". If the work turns out larger or more ambiguous than the card implies, stop and report
   (see step 6) rather than guessing.
4. Validate. Run the card's validation commands (and the [standard checks](#standard-validation)).
   The change is not done until they pass.
5. Update status. Set the card's Status to `Done` (or `Blocked` with a reason). Update the
   matching phase row's Status in the [track doc](../README.md#tracks-at-a-glance) if the card
   completes a phase, and tick the matching item(s) in the
   [implementation checklist](../implementation-checklist.md).
6. Report / hand off. Write a one-paragraph result to the shared [`.exchange`](#coordination)
   file at the repo root (what changed, validation result, and any follow-up cards to create). This
   lets the next agent or a human continue without re-discovering the work.

> Golden rule: a card is a contract. If a card cannot be satisfied within its stated scope and
> guardrails, it is marked `Blocked` with an explanation rather than expanded to force completion.

---

## Picking work without the planning DB

The roadmap's SQL todo list lives in an orchestrator's session and is not in the repo, so agents
in fresh sessions can't see it. Rely only on repo artifacts:

- [`cards/`](cards/): the executable units of work, each with a `Status`.
- The Status column in each [track document](../README.md#document-map) (`Not started` /
  `In progress` / `Done`): the authoritative phase state.

If no `Ready` card matches a phase that needs advancement, a new card can be written (using the
[template](task-card-template.md)) by decomposing that phase into a small, high-leverage task.

---

## Standard validation

These apply to every code-change card unless the card says otherwise (run from the repo root):

| Check | Command | Must |
|---|---|---|
| Build | `dotnet build src` | succeed |
| Tests | `dotnet test src --filter Category!=Interactive` | pass (no new failures) |
| Format | `dotnet format src --verify-no-changes` | report no changes (run `dotnet format src` to fix) |

Investigation-only cards (read-only, produce notes) skip build/test but must still leave their
output where the card specifies.

---

## Guardrails (do not violate)

- Always-shippable. Every change must leave `dotnet build src` green and the existing games
  playable. No half-applied refactors.
- Small diffs, one card. One card per commit/PR. Don't bundle unrelated changes.
- Stay in scope. Touch only the files the card names (plus the obviously-required neighbours).
  Surprises → mark `Blocked`, report, stop.
- Follow the house style. [`docs/coding-style.md`](../../coding-style.md) and the root
  `.editorconfig`; `dotnet format src` enforces it. Allman braces, 4 spaces, `_camelCase` private
  fields, `nameof` over string literals.
- Never edit generated files. Anything under `obj/`, `bin/`, or marked generated is off-limits.
- Don't invent design. If a card requires a design decision the roadmap hasn't made, stop and
  flag it for a high-effort model / the [open-questions log](../open-questions.md). Don't guess.
- ReplaySketch is the primary test/validation tool. Prefer it (and the
  [P0 command harness](../track-p-playable-baseline.md)) over inventing one-off harnesses.

---

## Coordination

Agents share the repo-root `.exchange` file to pass state across sessions and concurrent runs
(it is gitignored). A short, dated entry is appended when a card is completed or blocked: card id,
what changed, validation result, and any follow-up cards to create. The file is read before work
starts to avoid duplicate in-flight work.

---

## Files in this guide

| File | Purpose |
|---|---|
| [`README.md`](../README.md) | This loop and its conventions. |
| [`task-card-template.md`](task-card-template.md) | Copy this to author a new card. |
| [`cards/`](cards/) | The executable task cards, one bounded unit of work each. |

## How to maintain this guide

- When a roadmap phase is ready to be chipped at, ensure the phase's deep-dive `Architecture
  Diagrams` section contains a class diagram and at least one sequence diagram grounded in the
  actual types and flows. Replace any placeholder stubs before writing the first card. This is
  the gate; cards written without it lack the structural reference agents need to stay in scope.
- Decompose the phase into cards here (don't wait for a full deep dive: small
  investigation/prep cards are valuable early, but the diagrams should still precede executable
  cards).
- Keep each card small enough for a low-effort model. If a card needs deep reasoning, split it
  or escalate it to a roadmap deep dive instead.
- Keep card Status and the roadmap phase Status in sync.
- Cards reference the roadmap for why; never duplicate roadmap rationale here: link to it.
