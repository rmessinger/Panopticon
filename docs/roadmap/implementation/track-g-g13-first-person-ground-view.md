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
| [WI-G13-01](https://projects.reid-corp.com/work_packages/591) | G13-1 | `ViewModeController`, tactical snapshot, relative mouse, hotkey, input suppression | G2, G3 |
| [WI-G13-02](https://projects.reid-corp.com/work_packages/592) | G13-2 | `AvatarDropOrderGenerator`, drop validator, pin marker | WI-G13-01 |
| [WI-G13-03](https://projects.reid-corp.com/work_packages/593) | G13-3 | `AvatarLocomotion`, ground query, G10-derived scale | WI-G13-01 |
| [WI-G13-04](https://projects.reid-corp.com/work_packages/594) | G13-4 | Eye-height projection, culling, cascades, `HudVisibility`, crosshair and compass | WI-G13-02, WI-G13-03 |
| [WI-G13-05](https://projects.reid-corp.com/work_packages/595) | G13-4 | Skybox renderer (faces, model, gradient fallback), horizon-tinted fog, `TacticalSky` | WI-G13-04 |
| [WI-G13-07](https://projects.reid-corp.com/work_packages/1003) | G13-4 | Original sky sources: BFME `SkyboxSettings` and Generals face sets | WI-G13-05 |
| [WI-G13-06](https://projects.reid-corp.com/work_packages/596) | G13-5 | Descent and ascent, `FirstPersonView` scenario, goldens, B1 cost, GIF | WI-G13-05, WI-G13-07 |

`WI-G13-02` and `WI-G13-03` may run in parallel after `WI-G13-01`. Stack groups: group 1
`WI-G13-01`..`03`, group 2 `WI-G13-04`, `WI-G13-05`, `WI-G13-07`, group 3 `WI-G13-06`. Sky packs
(`WI-G12-08`, `WI-G12-09`) plug into the `WI-G13-05` resolver from the G12 stack.

## Results

Pending.
