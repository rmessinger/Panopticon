# Task Card `WI-A1-04`: Data-only asset loading without a graphics device

| Field | Value |
|---|---|
| Card ID | `WI-A1-04` |
| Roadmap phase | [Track A · A1](../../track-a-foundations.md): Headless simulation host, sub-phase A1-2 |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A1-03` (blocked on it: the §5 diagrams must be grounded before A1 code lands) |
| Diagrams | [A1 §5](../../deep-dives/a1-headless-host.md#5-architecture-diagrams) |

## Goal

Let `AssetStore` and `ContentManager` load logic-relevant asset data (object, weapon, and locomotor
INI, map data) with no `GraphicsDevice` present, closing blocker H2 from
[a1-headless-host.md §2.2](../../deep-dives/a1-headless-host.md#22-data-only-asset-loading), by
generalizing the `OnDemandAssetLoadStrategy.None` path that `MockedGameTest.TestGame` already uses.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a1-headless-host.md`: the `asset-data` rows of the
  `WI-A1-01` audit. These are the exact call sites to make optional.
- `docs/roadmap/deep-dives/a1-headless-host.md` §2.2 and §5.
- `src/Panopticon.Simulation/Content/AssetStore.cs:170-200`: the constructor's graphics parameters.
- `src/Panopticon.Simulation/Content/OnDemandAssetLoadStrategy.cs`: the `None` strategy.
- `src/Panopticon.Simulation/Content/ContentManager.cs`: the load entry points.
- `test/Panopticon.Simulation.Tests/MockedGameTest.cs`: the existing null-device construction to mirror.

## Steps

1. For each `asset-data` call site in the audit, make the graphics dependency optional rather than
   removing it: accept a nullable graphics load context and skip GPU upload when it is absent.
2. Do not change behavior when a real device is present. Every existing caller keeps its current
   construction and its current eager or on-demand upload behavior.
3. Add a test under `test/Panopticon.Simulation.Tests/Content/` that builds an `AssetStore` with a null
   graphics load context and `OnDemandAssetLoadStrategy.None`, then asserts that at least one
   `ObjectDefinition`, one `WeaponTemplate`, and one `LocomotorTemplate` resolve. Use the mocked
   path so it runs under `Category!=Interactive`; do not require a game install.
4. Assert in the same test that requesting a texture or model asset under the `None` strategy fails
   or returns null in the way the existing code already does. Do not change that behavior here.
5. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `src/Panopticon.Simulation/Content/AssetStore.cs`
- `src/Panopticon.Simulation/Content/ContentManager.cs`
- `src/Panopticon.Simulation/Content/OnDemandAssetLoadStrategy.cs`
- `test/Panopticon.Simulation.Tests/Content/` (new test file)

## Acceptance criteria

- [ ] An `AssetStore` constructs and resolves object, weapon, and locomotor definitions with no
      `GraphicsDevice` and no graphics load context.
- [ ] The new test passes under `dotnet test src --filter Category!=Interactive` with no game
      install.
- [ ] No existing test is edited to accommodate the change.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not introduce the null-device abstraction itself; this card only makes the dependency
  optional at the asset-data boundary.
- Do not touch the logic tick or `Game.cs`. Those are `WI-A1-05`.
- Do not change any persisted save-format field or INI parse table.
- If making a dependency optional would require changing render-path behavior, stop and mark
  `Blocked` rather than widening the change.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick A1-2 in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Follow-up cards: none new.
