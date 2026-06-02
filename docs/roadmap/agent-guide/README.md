# Agent Guide — incremental roadmap iteration

> A parallel, agent-focused companion to the [roadmap](../README.md). The roadmap says what
> to build and why; this guide says how a low-effort automated agent picks up a small, bounded
> slice and finishes it safely — without needing a high-effort model to re-reason the whole design.
>
> Audience: cheap/fast automated agents (and the orchestrators that dispatch them). If you are a
> human or a high-effort model doing strategic design, work in the [roadmap docs](../README.md)
> instead and leave task cards behind here for cheaper agents to execute. When you change the plan
> itself, follow the [planning-agent protocol](../MAINTAINING.md) to keep all parallel docs in sync.

---

## Why this exists

The roadmap phases (P, A, B, C) are large and design-heavy. Executing them end-to-end needs strong
reasoning. But most phases decompose into many small, mechanical, low-ambiguity steps —
cataloging code, replicating an existing pattern, adding a test, a narrow refactor. Those steps
don't need a high-effort model if they are written down precisely. This guide is where they are
written down, as task cards.

A good task card is self-contained: a cheap agent should be able to execute it by reading the card
plus the exact files it names — no roadmap-wide context required.

---

## The iteration loop

Every agent working from this guide follows the same loop:

1. Pick a card. Take one card from [`cards/`](cards/) whose Status is `Ready` and whose
   listed prerequisites are met. Do one card at a time.
2. Read only what the card names. The card lists the exact files / line ranges and the roadmap
   phase it serves. Read those. Do not go spelunking the whole codebase.
3. Make the change. Keep the diff small and surgical — scoped to the card's "Files likely to
   change". If the work turns out larger or more ambiguous than the card implies, stop and report
   (see step 6) rather than guessing.
4. Validate. Run the card's validation commands (and the [standard checks](#standard-validation)).
   The change is not done until they pass.
5. Update status. Set the card's Status to `Done` (or `Blocked` with a reason). Update the
   matching phase row's Status in the [track doc](../README.md#tracks-at-a-glance) if the card
   completes a phase, and tick the matching item(s) in the
   [implementation checklist](../implementation-checklist.md).
6. Report / hand off. Write a one-paragraph result to the shared [`.exchange`](#coordination)
   file at the repo root (what changed, validation result, any follow-up cards to create). This lets
   the next agent or a human continue without re-discovering your work.

> Golden rule: a card is a contract. If you can't satisfy it within its stated scope and
> guardrails, mark it `Blocked` and explain why — don't expand scope to force it through.

---

## Picking work without the planning DB

The roadmap's SQL todo list lives in an orchestrator's session and is not in the repo, so agents
in fresh sessions can't see it. Rely only on repo artifacts:

- [`cards/`](cards/) — the executable units of work, each with a `Status`.
- The Status column in each [track document](../README.md#document-map) (`Not started` /
  `In progress` / `Done`) — the authoritative phase state.

If no `Ready` card matches a phase you've been asked to advance, your job may be to write new
cards (using the [template](task-card-template.md)) by decomposing that phase — a small,
high-leverage task in itself.

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
  flag it for a high-effort model / the [open-questions log](../open-questions.md) — don't guess.
- ReplaySketch is the primary test/validation tool. Prefer it (and the
  [P0 command harness](../track-p-playable-baseline.md)) over inventing one-off harnesses.

---

## Coordination

Agents share the repo-root `.exchange` file to pass state across sessions and concurrent runs
(it is gitignored). Append a short, dated entry when you finish or block a card: card id, what
changed, validation result, and any follow-up cards to create. Read it before starting so you don't
duplicate in-flight work.

---

## Files in this guide

| File | Purpose |
|---|---|
| [`README.md`](README.md) | This loop + conventions (you are here). |
| [`task-card-template.md`](task-card-template.md) | Copy this to author a new card. |
| [`cards/`](cards/) | The executable task cards, one bounded unit of work each. |

## How to maintain this guide

- When a roadmap phase is ready to be chipped at, decompose it into cards here (don't wait for a
  full deep dive — small investigation/prep cards are valuable early).
- Keep each card small enough for a low-effort model — if a card needs deep reasoning, split it
  or escalate it to a roadmap deep dive instead.
- Keep card Status and the roadmap phase Status in sync.
- Cards reference the roadmap for why; never duplicate roadmap rationale here — link to it.
