# Task Card `WI-A1-01`: Audit `GraphicsDevice` couplings on the logic and asset-data path

| Field | Value |
|---|---|
| Card ID | `WI-A1-01` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-1 |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Produce the authoritative list of every place the logic and asset-data path requires a
`GraphicsDevice` or a graphics resource, classified as `logic`, `asset-data`, or `presentation`,
so the null-device seam of [a1-headless-host.md §2.1](../../deep-dives/a1-headless-host.md#21-a-gpu-optional-graphics-seam)
can be scoped. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/deep-dives/a1-headless-host.md` §1 (blockers H1-H2), §2.1, §2.2, and the
  `Appendix: What already exists`: the design intent and the known couplings.
- `src/OpenSage.Game/Game.cs:510-600`: the constructor's device creation, `StandardGraphicsResources`,
  `ShaderSetStore`, `ShaderResourceManager`, `GraphicsLoadContext`, `AssetStore` construction.
- `src/OpenSage.Game/Content/AssetStore.cs:170-200`: the constructor's graphics parameters.
- `src/OpenSage.Game/Content/OnDemandAssetLoadStrategy.cs`: the existing `None` strategy.
- `src/OpenSage.Game/GameEngine.cs`: the `IGameEngine` seam and which services it exposes.
- `src/OpenSage.Game.Tests/MockedGameTest.cs`: the existing `GraphicsDevice = null` +
  `OnDemandAssetLoadStrategy.None` proof-of-concept, and exactly how far it gets.

## Steps

1. Grep `src/OpenSage.Game` for `GraphicsDevice`, `GraphicsLoadContext`, `ShaderSetStore`,
   `ShaderResourceManager`, and `StandardGraphicsResources`. Do not open files outside the hits and
   the context list above.
2. For each hit record: `file:line`, the owning type, and a classification of `logic` (reached from
   `GameLogic.Update` / `Scene3D.LogicTick` / scripting), `asset-data` (reached while parsing INI,
   map, or object-definition data), or `presentation` (reached only from render or audio paths).
3. For every `logic` and `asset-data` hit, record the minimal surface actually used, for example
   `ResourceFactory.CreateBuffer` or `IsUvOriginTopLeft`, not the whole device. This set is the
   candidate null-device interface.
4. Record what `MockedGameTest.TestGame` already avoids and where it would fail if it tried to run
   a full logic tick, based only on the files listed above. Do not run the game.
5. Create `docs/roadmap/implementation/track-a-a1-headless-host.md` following the structure of
   `docs/roadmap/implementation/track-g-g1-high-dpi.md` (human-facing summary above the `---`
   divider, agent-context detail below it) and write the table into a
   `## GraphicsDevice coupling audit` section below the divider.
6. Add a `## Track A · A1 : Headless simulation host` row group for the new plan in
   `docs/roadmap/implementation/README.md`, matching the existing table format.
7. If a coupling cannot be classified from the listed context, mark it `ambiguous` with a one-line
   reason rather than guessing.

## Files likely to change

- `docs/roadmap/implementation/track-a-a1-headless-host.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every `GraphicsDevice` / `GraphicsLoadContext` / `ShaderSetStore` / `ShaderResourceManager` /
      `StandardGraphicsResources` reference in `src/OpenSage.Game` appears in the table with a
      classification.
- [ ] The candidate null-device surface is stated as an explicit member list, not a prose summary.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not write the null-device interface or any code. This card only scopes it.
- Do not change `Game.cs`, `AssetStore.cs`, or the load strategy.
- Do not extend the audit to `OpenSage.Rendering` internals; classify them as `presentation` at the
  call boundary.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A1-1 in [`implementation-checklist.md`](../../implementation-checklist.md) only when both
  `WI-A1-01` and `WI-A1-02` are `Done` (A1-1 covers the whole audit).
- Append a result line to repo-root `.exchange`, including the count of `ambiguous` entries.
- Follow-up cards: none new; `WI-A1-03` consumes this table.
