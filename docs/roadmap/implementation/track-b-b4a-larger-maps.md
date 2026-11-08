# Implementation plan: Track B · B4a larger maps

> Audience: human + agent implementation plan for B4a larger-map work.

Source of truth: [B4a larger maps deep dive](../deep-dives/b4a-larger-maps.md) and
[Architecture Diagrams](../deep-dives/b4a-larger-maps.md#architecture-diagrams).

## Scope

B4a scales WorldBuilder, terrain runtime storage, and terrain rendering for very large maps. B4a
owns runtime chunk residency and paging. H3 owns on-disk encoding.

## Card sequence

| Card | Sub-phase | Output | Status | Prerequisites |
|---|---|---|---|---|
| [`WI-B4a-01`](https://projects.reid-corp.com/work_packages/351) | B4a-5 prep | 128-interval runtime chunk contract | Ready | none |
| [`WI-B4a-02`](https://projects.reid-corp.com/work_packages/352) | B4a-1 | Baseline and limits audit | Ready | A5-6 checklist item |
| [`WI-B4a-03`](https://projects.reid-corp.com/work_packages/353) | B4a-2 | Region-delta edit contract | Ready | A5-6 checklist item |
| [`WI-B4a-04`](https://projects.reid-corp.com/work_packages/354) | B4a-2 | Height brush without full clones | Draft | `WI-B4a-03` |
| [`WI-B4a-05`](https://projects.reid-corp.com/work_packages/355) | B4a-2 | Blend/material/mask deltas | Draft | `WI-B4a-03` |
| [`WI-B4a-06`](https://projects.reid-corp.com/work_packages/356) | B4a-3 | Large presets and 16-bit defaults | Ready | A5-6 checklist item |
| [`WI-B4a-07`](https://projects.reid-corp.com/work_packages/357) | B4a-4 | CDLOD design | Draft | `WI-B4a-01` |
| [`WI-B4a-08`](https://projects.reid-corp.com/work_packages/358) | B4a-4 | CDLOD selection | Draft | `WI-B4a-07` |
| [`WI-B4a-09`](https://projects.reid-corp.com/work_packages/359) | B4a-4 | Visible patch streaming | Draft | `WI-B4a-07`, `WI-B4a-08` |
| [`WI-B4a-10`](https://projects.reid-corp.com/work_packages/360) | B4a-5 | Runtime chunk store | Draft | `WI-B4a-01`, `WI-H3-01`, `WI-H3-02` |
| [`WI-B4a-11`](https://projects.reid-corp.com/work_packages/361) | B4a-6 | Large-map round-trip and play validation | Draft | `WI-B4a-10`, H6 |

## Acceptance by sub-item

| Sub-item | Acceptance criteria |
|---|---|
| B4a-1 | A measured audit identifies the first failing constraint among C1 through C6 and records A5-6 status. |
| B4a-2 | Height and layer edits store rectangle plus before and after arrays; height brush no longer clones the full grid. |
| B4a-3 | Large presets exist, have no hard cap, and default to 16-bit elevation where supported. |
| B4a-4 | CDLOD selects visible quadtree nodes with continuous morph and builds only visible terrain meshes. |
| B4a-5 | Runtime chunk store pages 128-interval chunks, preserves shared edges, and tracks dirty chunks. |
| B4a-6 | A synthetic large map saves, reloads, plays, and emits B1 measurements. |
| B4a-7 | Large object populations are bulk-placeable, region-editable, and rendered through scalable editor paths. |

## Dependency notes

A5-6 is not complete in the checklist. The only matching card found, `WI-A5-06`, is Ready and
covers A5 architecture grounding, not the full WorldBuilder baseline. B4a cards that touch
WorldBuilder list the A5-6 checklist item as a prerequisite until the coordinator adds or completes
the baseline card.
