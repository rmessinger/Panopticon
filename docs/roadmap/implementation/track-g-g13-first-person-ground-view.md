# G13 Implementation: First-Person Ground View

> Audience: agent. Card sequence and evidence for [G13](../deep-dives/g13-first-person-ground-view.md).

## Scope

A hotkey in RTS mode enters an avatar drop mode; a valid ground click descends to eye height and
the operator walks the terrain in first person, then returns to the restored RTS camera.
Presentation only. Source of truth:
[design and diagrams](../deep-dives/g13-first-person-ground-view.md#5-architecture-diagrams).

## Card sequence

| Card | Sub-phase | Deliverable | Depends on |
|---|---|---|---|
| [WI-G13-01](../agent/cards/WI-G13-01-view-mode-seam-and-relative-mouse.md) | G13-1 | `ViewModeController`, tactical snapshot, relative mouse, hotkey, input suppression | G2, G3 |
| [WI-G13-02](../agent/cards/WI-G13-02-avatar-drop-mode.md) | G13-2 | `AvatarDropOrderGenerator`, drop validator, pin marker | WI-G13-01 |
| [WI-G13-03](../agent/cards/WI-G13-03-avatar-ground-locomotion.md) | G13-3 | `AvatarLocomotion`, ground query, G10-derived scale | WI-G13-01 |
| [WI-G13-04](../agent/cards/WI-G13-04-ground-level-projection-and-hud.md) | G13-4 | Eye-height projection, culling, cascades, `HudVisibility`, crosshair and compass | WI-G13-02, WI-G13-03 |
| [WI-G13-05](../agent/cards/WI-G13-05-sky-and-distance-fog.md) | G13-4 | `SkyRenderer`, shared fog include | WI-G13-04 |
| [WI-G13-06](../agent/cards/WI-G13-06-transitions-goldens-and-cost.md) | G13-5 | Descent and ascent, `FirstPersonView` scenario, goldens, B1 cost, GIF | WI-G13-05 |

`WI-G13-02` and `WI-G13-03` may run in parallel after `WI-G13-01`. Groups on
`staging/track-g-g13`: group 1 `WI-G13-01`..`03`, group 2 `WI-G13-04`..`05`, group 3 `WI-G13-06`.

## Results

Pending.
