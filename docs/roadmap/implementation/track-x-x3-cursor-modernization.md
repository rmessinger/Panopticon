# X3: Cursor modernization

> Audience: human + agent.
> Phase and dependencies: [Track X](../track-x-fixes-tweaks.md). No dependencies. Sharp cursors at non-unity scale interact with Track G G1; opt-in presets follow the G2 `Faithful`/`Enhanced` convention.

Architecture: none yet. Loader diagrams are required before any source-edit card becomes `Ready`, grounded by `WI-X3-01`.

| Card | Status | Scope | Prerequisites |
|---|---|---|---|
| [WI-X3-01](../agent/cards/WI-X3-01-cursor-theme-examples.md) | Ready | Evaluate Bibata and game-icons.net cursors, produce comparison examples, assess the loader | none |

## Starting point

- The original set is 52 `.ani` files in the install's loose `Data/Cursors/` tree, mapped from `MouseCursor` INI blocks.
- Loose disk files override archive entries, so same-named `.ani`/`.cur` replacements load without code changes.
- `Cursor` builds SDL hardware cursors from one image per frame. At non-unity `CursorScale`, it resizes that image with ImageSharp, which blurs 32 px originals.
- Desktop cursor themes cover only pointer, busy, and scroll roles. RTS action cursors need a separate source.

## Recommended path under evaluation

| Role | Source | License |
|---|---|---|
| Pointer, scroll, animated busy | Bibata Cursor, Windows build | GPL-3.0 |
| Action and invalid-action | game-icons.net SVGs, rasterized | CC BY 3.0 |

## WI-X3-01 results

Pending.
