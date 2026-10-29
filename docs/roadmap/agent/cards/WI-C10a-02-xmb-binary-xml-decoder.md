# Task Card `WI-C10a-02`: XMB binary XML decoder

| Field | Value |
|---|---|
| Card ID | `WI-C10a-02` |
| Roadmap phase | [Track C · C10a](../../track-c-external-assets.md): AoE3 DE archives and textures |
| Status | `Done` |
| Type | `source-edit` |
| Effort | `S` |
| Prerequisites | `WI-C10a-01` for the interactive check only; the decoder itself has no prerequisite |
| Diagrams | [C10 architecture diagrams](../../deep-dives/c10-aoe3de-granny-models.md#9-architecture-diagrams) |

## Goal

`XmbDocument.Read(stream)` turns AoE3 DE `X1`/`XR` version 8 binary XML into an `XDocument`.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md` sections 1, 5, and 7.
- `src/Panopticon.FileFormats.Bar/` from `WI-C10a-01`.

## Steps

1. Add `XmbDocument` in `Panopticon.FileFormats.Bar`: `X1` header, `XR` root, version 8, element-name and attribute-name tables (UTF-16), then the node tree with text, attributes, and children.
2. Accept an `alz4`-wrapped stream transparently by peeking the magic.
3. Throw with offset and expected tag on a bad header, version, or index.
4. Install-free tests from a hand-built minimal XMB; Interactive test decodes `Data\protoy.xml.XMB`, finds the Musketeer unit and its `animfile`, and decodes `Render\materialdefs\default.materialdef.XMB`.

## Files likely to change

- `src/Panopticon.FileFormats.Bar/XmbDocument.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/XmbDocumentTests.cs` (new)

## Acceptance criteria

- [ ] Synthetic XMB round-trips element names, attributes, text, and nesting.
- [ ] Interactive: `protoy.xml.XMB` decodes; Musketeer's `animfile` value is recorded in the deep dive.
- [ ] Every `.XMB` in `Data.bar` decodes without exceptions (count recorded).
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

- Interpreting protoy or animfile semantics (`WI-C10c-03`).
- Writing XMB.
- No AoE3 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Set `WI-C10b-03` to `Ready` if `WI-C10b-02` and `WI-C10a-03` are done; set `WI-C10c-03` to `Ready`.
- Tick C10a in the checklist when its last card is done, and update the README progress row.
- Append a result line to `.exchange`.
