# Track X: Miscellaneous Fixes and Tweaks

> Part of the [roadmap](README.md).
> Audience: human.

Open-ended queue for bounded fixes and presentation tweaks. Each phase has its own milestone and dependencies; the track has no terminal completion milestone. New entries receive the next unused phase number. Work already owned by another track stays there.

## Phases

| Phase | Goal | Milestone | Status | Depends on |
|---|---|---|---|---|
| X1. Imported-unit hit feedback | Bind explicit target-side damage effects and improve the mixed-source duel capture. | Rebel hits produce visible effects on the tank's Rebel-facing surface; a labeled, uncropped capture shows target impacts and damage on both units, with unchanged combat results. | Done | H2a, P9 |
| X2. Capacity-probe graphics resource fixes | Bound upload temporaries and reduce measured combat FX/model recreation. | Repeated uploads have bounded temporary ownership; a measured recreation owner reuses compatible resources with lifecycle tests, before/after memory and frame-time evidence, and preserved visuals. | Not started | M1 |
| X3. Cursor modernization | Evaluate open-source replacements for the original cursor set and their high-DPI loader requirements. | A comparison contact sheet and animated GIF show licensed candidate cursors next to the originals at 1x and 2x; a recorded loader assessment names any required code change, with the original set kept as the default. | Not started | — |
| X4. ReplaySketch TabControl shell | Replace the hand-built tab strip in the ReplaySketch `MainWindow` with an Avalonia `TabControl`. | All five tabs switch through one `TabControl` with their selection side effects and screenshot mode intact; the Model-after-Harness stacked-frame bug is gone, and captures of every tab show unchanged content. | Not started | — |

## Phase resources

| Phase | Design | Diagrams | Cards |
|---|---|---|---|
| X1 | [Hit feedback](deep-dives/x1-imported-unit-hit-feedback.md) | [Architecture](deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) | [WI-X1-01..07](implementation/track-x-x1-hit-feedback.md) |
| X2 | [Capacity-probe fixes](implementation/track-x-x2-capacity-probe-fixes.md) | [Upload lifecycle](agent/cards/WI-X2-01-bound-upload-resource-lifetimes.md#architecture-diagrams); reuse diagrams gated on profiling | [WI-X2-01..02](implementation/track-x-x2-capacity-probe-fixes.md) |
| X3 | [Cursor modernization](implementation/track-x-x3-cursor-modernization.md) | Loader diagrams gated on `WI-X3-01` | [WI-X3-01](implementation/track-x-x3-cursor-modernization.md) |
| X4 | [TabControl shell](implementation/track-x-x4-replaysketch-tabcontrol.md) | [Architecture](implementation/track-x-x4-replaysketch-tabcontrol.md#architecture-diagrams) | [WI-X4-01](implementation/track-x-x4-replaysketch-tabcontrol.md) |

X1 consumes the published M2 tank row, not the unresolved M2 aircraft or infantry contracts. `WI-M2-03` is a card-level prerequisite. X1 does not close M2 or introduce an EE2 effects importer.
