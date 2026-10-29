# Task Card `WI-C10c-03`: Animfile resolver: models, variants, clips, attachments

| Field | Value |
|---|---|
| Card ID | `WI-C10c-03` |
| Roadmap phase | [Track C · C10c](../../track-c-external-assets.md): AoE3 DE skinned animation |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `M` |
| Prerequisites | `WI-C10a-02` |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`Aoe3AnimfileResolver` turns a protounit name and a tech set into one model path, named clip paths, and bone attachments.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` section 5.
- `XmbDocument` from `WI-C10a-02`.

## Steps

1. Read `Data\protoy.xml.XMB`, find the unit, and read its `animfile` XML (XMB or plain).
2. Resolve `<component>` `logic` variants (tech and age conditions) to exactly one `GrannyModel` path per request; ties and unknown conditions fail with unit and condition.
3. Collect `<anim>` entries to `GrannyAnim` paths by anim name, and attachments as `frombone`/`tobone` pairs with their model paths; `definebone` entries are recorded.
4. Return `Aoe3UnitVisual`; tests cover Musketeer age 4, Hussar, and Frigate.

## Files likely to change

- `src/Panopticon.Simulation/Content/Importing/Granny/Aoe3AnimfileResolver.cs`, `Aoe3UnitVisual.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/Aoe3AnimfileResolverTests.cs` (new)

## Acceptance criteria

- [ ] Musketeer age 4 resolves `musketeer_4age.gr2` and the section 5 clips.
- [ ] Variant resolution is deterministic for a given tech set.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
AOE3DE_PATH="/mnt/pit2/Network Storage/Transfer/AoE3DE" dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~Aoe3"
```

## Out of scope / guardrails

- Gameplay import of AoE3 units (Track D).
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10c-04` to `Ready` if `WI-C10c-01`, `WI-C10c-02`, and `WI-C10b-04` are done.
- Tick C10c in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
