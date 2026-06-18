# Maintaining the roadmap — planning-agent protocol

> Part of the [Large-Scale Simulation Roadmap](README.md). The single, authoritative checklist for
> agents (or humans) who iterate on the plan itself — adding/changing/reordering phases, recording
> decisions, introducing seams, writing deep dives. The roadmap is a set of parallel documents that
> must move together; this protocol says exactly what to propagate after every plan change so they
> never drift.
>
> Audience: roadmap planning agents (high-effort, editing the design). This is distinct from the
> [agent-guide](agent-guide/), which is for execution agents chipping at bounded task cards. If you
> are executing a card, follow the agent-guide; if you are changing the plan, follow this.

---

## The parallel documents (and their source-of-truth role)

Each artifact owns one view. A plan change usually touches several. Know which owns what:

| Document | Owns (source of truth for) |
|---|---|
| [`track-*.md`](README.md#document-map) (P, A, B, C) | Phase definitions, goals, Milestone (definition-of-done), Status, dependencies, per-phase notes. |
| [`implementation-checklist.md`](implementation-checklist.md) | The flat, granular change list — concrete items per phase, each with an acceptance criteria (`AC:`) sub-bullet. |
| [`roadmap-tree.md`](roadmap-tree.md) | The visual hierarchy (mindmap) + dependency flow (LR stages). |
| `roadmap-tree.html` | Interactive viewer — auto-syncs (reads the `.md` live). No manual edit. |
| [`open-questions.md`](open-questions.md) | The decision log (every item a settled Decision + rationale). |
| [`seams.md`](seams.md) | The seam registry (structural + A4 experiment seams). |
| [`deep-dives/`](deep-dives/) | Per-phase detailed designs. |
| [`current-state.md`](current-state.md) | Snapshot of the code the roadmap assumes. |
| [`agent-guide/`](agent-guide/) | Task cards + the execution-agent workflow. |
| [`README.md`](README.md) | Index: doc map, deep-dive table, at-a-glance, guiding principles. |
| Copilot memories | Durable decisions/conventions (via `store_memory`, repository scope). |
| Session `plan.md` | The orchestrator's working notes — session-local, not in the repo. |

---

## Propagation matrix — what to update for each kind of change

Find your change type; update every box listed. Then run the [validation](#validation-every-time).

| You changed… | Must also update |
|---|---|
| Add a phase / sub-phase | track doc table (incl. a Milestone definition-of-done) + notes · implementation-checklist (new items, each with an `AC:` sub-bullet — concrete, or `TBD` + rough template if not yet definable) · roadmap-tree mindmap · roadmap-tree LR flow (place by dependency depth + edges) · README at-a-glance · its deps in the tree/checklist ordering |
| Rename / renumber a phase | every doc above plus every inbound link/anchor; search the whole `docs/roadmap/` tree for the old id · agent-guide cards referencing it · SQL todo ids (if the orchestrator tracks them) |
| Remove a phase | delete from track doc, checklist, tree (both diagrams), README; fix anything that depended on it |
| Change a dependency / ordering | track doc "Depends on" · roadmap-tree LR edges (+ re-stage if depth changed) · checklist ordering |
| Change a phase's Status | track doc Status · tick/untick checklist items · matching agent-guide card Status · README [Progress](#progress) tables |
| Make a decision (resolve an open question) | open-questions.md (write as Decision + rationale) · thread into the affected phase/deep dive · `store_memory` (repository scope) · README guiding principle if it's a principle |
| Introduce / change a seam | seams.md registry · the A4 notes in track-a · any deep dive that uses it |
| Write a deep dive | add `deep-dives/<name>.md` · link from the phase row · README Deep dives table · mark the phase ⮕ in the roadmap-tree mindmap |
| Phase ready for cheap agents | decompose into [agent-guide cards](agent-guide/) (template) · keep card Status ↔ phase Status |
| Code drifts from assumptions | refresh current-state.md (it underpins every phase) |
| Add/rename/remove any doc | README doc map + (if a deep dive) the Deep dives table · this file's parallel-documents table |

---

## Ordered checklist (run after every plan change)

1. Edit the owning doc(s) for your change (see the matrix).
2. Propagate to every other box the matrix lists — do not stop at the first doc.
3. Decisions → memory. If you settled a question or set a convention, call `store_memory`
   (repository scope) and ensure `open-questions.md` shows it as a Decision.
4. Re-validate the tree. If you touched `roadmap-tree.md`, confirm both Mermaid diagrams still
   render (see commands) — a parse error breaks the viewer.
5. Check links. New/renamed files or anchors → verify every reference resolves.
6. Consistency sweep. Confirm the [invariants](#consistency-invariants) hold.
7. Update the session `plan.md` (if you're an orchestrator with one) — but never rely on it as
   shared truth; it isn't in the repo.
8. Hand off via repo-root `.exchange` if other agents are involved.

---

## Consistency invariants (must always hold)

- Every phase appears in all four phase views: its track doc table, the
  implementation-checklist, the roadmap-tree mindmap, and the roadmap-tree LR flow — and in
  the README at-a-glance.
- Dependencies agree across the track-doc "Depends on" column, the roadmap-tree LR edges, and the
  checklist ordering.
- open-questions.md has no un-resolved items unless genuinely open; resolved items are
  Decisions and are threaded into their phase.
- Every deep dive is linked from its phase row and the README Deep dives table, and its phase
  is marked ⮕ in the mindmap.
- Card Status ↔ phase Status ↔ checklist ticks do not contradict each other.
- Every checklist item has an `AC:` sub-bullet (concrete, or `TBD` + rough template). Tighten
  `TBD` ACs into concrete, verifiable ones as the phase's deep dive lands.
- No broken internal links or anchors anywhere under `docs/roadmap/`.

---

## Validation (every time)

```sh
# From docs/roadmap/ — both Mermaid diagrams must report ✅
npx -y @mermaid-js/mermaid-cli@latest -i roadmap-tree.md -o /tmp/rt.svg && rm -f /tmp/rt*.svg rt-*.svg

# Quick internal-link existence check for a doc you edited (adjust the file)
grep -oE '\]\(([a-zA-Z0-9_./#-]+)\)' README.md
```

This is documentation only — no `dotnet` build/test is required for plan changes (that's the
[execution agents'](agent-guide/README.md#standard-validation) job when they implement a card).

---

## Documentation style

- No markdown emphasis (no bold `**...**` or italic `*...*` / `_..._`) anywhere in roadmap docs.
- Use plain prose. Structure through headers, tables, lists, and `` `code spans` `` only.
- Fenced code blocks for shell commands and code examples are fine.

---

## Why this matters

The roadmap's value is that the strategic view, the granular change list, the visual tree, the
decision log, and the agent task cards all tell the same story. A change recorded in only one of
them silently rots the others. This protocol is the contract that keeps them coherent.
