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

1. Pick a ticket. Take one OpenProject ticket from project Panopticon whose status permits starting
   work and whose `follows` prerequisites have reached a completed status. Do one ticket at a time.
   If the ticket's description was written against types its prerequisites have not implemented
   yet, reconcile the description with the landed code before starting. Current status names:
   `op_list_statuses`.
2. Read only what the ticket names. The ticket description lists the exact files / line ranges and
   the roadmap phase it serves. Read those. Do not go spelunking the whole codebase.
3. Make the change. Keep the diff small and surgical: scoped to the ticket's "Files likely to
   change". If the work turns out larger or more ambiguous than the ticket implies, stop and report
   (see step 6) rather than guessing.
4. Validate. Run the ticket's validation commands (and the [standard checks](#standard-validation)).
   The change is not done until they pass.
5. Update status. Set the ticket's status to a completed status, or to a status that marks it
   blocked with a reason, using `op_update_work_package`. Update the matching phase row's Status in the
   [track doc](../README.md#tracks-at-a-glance) if the ticket completes a phase, and tick the
   matching item(s) in the [implementation checklist](../implementation-checklist.md).
6. Record the result. Add an OpenProject completion comment with
   `op_add_work_package_comment` (see [Recording results](#recording-results)). This lets the next
   agent or a human continue without re-discovering the work.

> Golden rule: a ticket is a contract. If a ticket cannot be satisfied within its stated scope and
> guardrails, it is set to a status that marks it blocked, with an explanation, rather than expanded
> to force completion.

---

## Picking work without the planning DB

The roadmap's SQL todo list lives in an orchestrator's session and is not in the repo, so agents
in fresh sessions can't see it. Rely only on OpenProject and repo artifacts:

- OpenProject project Panopticon (id 4): the source of truth for ticket status, prerequisites, and
  descriptions. Query with `op_list_work_packages` and `op_get_work_package`.
- [`cards/`](cards/): one link stub per ticket (canonical card ID, title, ticket URL). Stubs carry
  no status.
- The Status column in each [track document](../README.md#document-map) (`Not started` /
  `In progress` / `Done`): the authoritative phase state.

If no startable ticket matches a phase that needs advancement, a new ticket can be created (see
[Ticket workflow](#ticket-workflow) and the [template](task-card-template.md)) by decomposing that
phase into a small, high-leverage task.

---

## Ticket workflow

OpenProject is the canonical ticket system. Project: Panopticon (id 4),
https://projects.reid-corp.com/projects/panopticon. Work package URL:
`https://projects.reid-corp.com/work_packages/<id>`.

| Action | Tool |
|---|---|
| Find the ticket for a card ID (exact match) | `op_find_card_work_package(card_id, project_id?)` |
| Create a ticket | `op_create_work_package` |
| Read a ticket | `op_get_work_package(wp_id)` |
| Change status | `op_update_work_package` |
| Add a prerequisite | `op_create_work_package_relation(wp_id, to_id, type="follows")` |
| List prerequisites | `op_list_work_package_relations(wp_id)` |
| Record completion | `op_add_work_package_comment(wp_id, comment)` |
| List statuses | `op_list_statuses` |

- `op_update_work_package` requires the current `lock_version`, which `op_get_work_package` returns for the same ticket. Read the ticket immediately before updating it.
- Ticket status, prerequisites, and completion records live only in OpenProject. Stub files carry no status and never mirror it.
- Create a ticket only after `op_find_card_work_package` finds none for the card ID. Never overwrite an existing ticket or reuse a card ID that belongs to another ticket; a collision is reported.
- `get_card(card_id, ref)` and `card_status(track, ref)` are deprecated frozen Markdown snapshots. Use them only to inspect source or history at an explicit ref. Their payload may be stale, so never use them for current status.
- A card file created on an unmerged branch has no ticket. After that branch merges, look up its ID with `op_find_card_work_package`, create the ticket if none exists, and replace the card file with a link stub in a follow-up change.

## Standard validation

These apply to every code-change ticket unless the ticket says otherwise (run from the repo root):

| Check | Command | Must |
|---|---|---|
| Build | `dotnet build src` | succeed |
| Tests | `dotnet test src --filter Category!=Interactive` | pass (no new failures) |
| Format | `dotnet format src --verify-no-changes` | report no changes (run `dotnet format src` to fix) |

### Standing playability regression (P7)

The P7 end-to-end regression suite is the standing "is the game still playable?" gate. It runs
six end-state scenarios (player eliminated, player surrenders, AI surrenders, team win, mutual
elimination, sudden death, score decision) through the P0 harness, each asserting its terminal
`GameOutcome` plus the universal invariants (money never negative, no unit duplication, AI made
progress). Run it with:

```sh
dotnet test src --filter "FullyQualifiedName~P7_ScenarioMatrixTests"
```

The P6b archetype-contract scenarios are the companion gate for per-unit contracts:

```sh
dotnet test src --filter "FullyQualifiedName~P6bScenarioMatrixTests"
```

Both run under `dotnet test src --filter Category!=Interactive` (no game install required).

Investigation-only cards (read-only, produce notes) skip build/test but must still leave their
output where the card specifies.

---

## Guardrails (do not violate)

- Always-shippable. Every change must leave `dotnet build src` green and the existing games
  playable. No half-applied refactors.
- Small diffs, one card. One card per commit/PR. Don't bundle unrelated changes.
- Stay in scope. Touch only the files the card names (plus the obviously-required neighbours).
  Surprises → set the ticket to a status that marks it blocked, report, stop.
- Follow the house style. [`docs/coding-style.md`](../../coding-style.md) and the root
  `.editorconfig`; `dotnet format src` enforces it. Allman braces, 4 spaces, `_camelCase` private
  fields, `nameof` over string literals.
- Never edit generated files. Anything under `obj/`, `bin/`, or marked generated is off-limits.
- Don't invent design. If a card requires a design decision the roadmap hasn't made, stop and
  flag it for a high-effort model / the [open-questions log](../open-questions.md). Don't guess.
- ReplaySketch is the primary test/validation tool. Prefer it (and the
  [P0 command harness](../track-p-playable-baseline.md)) over inventing one-off harnesses.

---

## Recording results

A completed or blocked ticket records its result as an OpenProject comment through
`op_add_work_package_comment`: date, what changed, validation outcome, and follow-up tickets. The
status change is made separately with `op_update_work_package`. The card `Status` field and the
`Result:` line are retired. The repo-root `.exchange` file is a frozen, read-only archive of
pre-retirement results and is not written to.

---

## Files in this guide

| File | Purpose |
|---|---|
| [`README.md`](../README.md) | This loop and its conventions. |
| [`task-card-template.md`](task-card-template.md) | Procedure and stub format for a new ticket. |
| [`cards/`](cards/) | Link stubs, one per OpenProject ticket: card ID, title, ticket URL. |

## How to maintain this guide

- When a roadmap phase is ready to be chipped at, ensure the phase's deep-dive `Architecture
  Diagrams` section contains a class diagram and at least one sequence diagram grounded in the
  actual types and flows. Replace any placeholder stubs before a source-edit ticket is made startable.
  A read-only audit/design ticket may be made startable when its deliverable supplies the evidence or
  diagrams needed to open this gate.
- Decompose the phase into OpenProject tickets, one link stub per ticket in [`cards/`](cards/)
  (don't wait for a full deep dive: small investigation/prep tickets are valuable early, but the
  diagrams should still precede executable tickets).
- Keep each ticket small enough for a low-effort model. If a ticket needs deep reasoning, split it
  or escalate it to a roadmap deep dive instead.
- Phase Status stays in the track doc. Ticket status changes go to OpenProject and are never
  mirrored into stubs.
- Ticket descriptions reference the roadmap for why; never duplicate roadmap rationale there: link
  to it.
