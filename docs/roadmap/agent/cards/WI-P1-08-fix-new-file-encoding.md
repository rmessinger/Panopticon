# Task Card: `WI-P1-08` — Restore UTF-8 BOM on new P1 files (format gate)

| Field | Value |
|---|---|
| Card ID | `WI-P1-08` |
| Roadmap phase | [Track P · P1 — Skirmish setup & player relationships](../../track-p-playable-baseline.md) |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | [P1 Architecture Diagrams](../../deep-dives/p1-skirmish-setup.md#8-architecture-diagrams) |

## Goal

Fix the file encoding of the new/modified P1 source files so that
`dotnet format src --verify-no-changes` passes. They were committed without the UTF-8
byte-order mark (BOM) the repo's `.editorconfig` `charset` rule enforces, which fails the CI
format gate (`error CHARSET: Fix file encoding`). This regression currently fails the acceptance
criteria of every other P1 card.

## Context to read (and only this)

- `src/OpenSage.Game/Network/SkirmishSlot.cs:1` — a correctly-encoded sibling file; its first
  three bytes are `EF BB BF` (UTF-8 BOM). Use it as the reference encoding.
- `.editorconfig` (repo root) — the `charset = utf-8-bom` rule that the format gate enforces.

The offending files (all start with bytes `75 73 69` = `usi…`, i.e. **no BOM**):

- `src/OpenSage.Game/Network/SkirmishSlotDefaults.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerManagerTests.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerOwnershipTests.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerRelationshipTests.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/Services/PlatformPaths.cs`

## Steps

1. Run `dotnet format src --verify-no-changes` and confirm the `CHARSET` errors for the five
   files above.
2. Let the formatter fix them in place: `dotnet format src` (this rewrites the files with the
   correct `utf-8-bom` encoding). Alternatively, prepend the BOM bytes `EF BB BF` to each file.
3. Re-run `dotnet format src --verify-no-changes` and confirm it now reports no `CHARSET`
   errors.
4. Do **not** change any code content — encoding only. Confirm `git diff --stat` shows only the
   five files and that their textual content is otherwise unchanged.

## Files likely to change

- `src/OpenSage.Game/Network/SkirmishSlotDefaults.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerManagerTests.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerOwnershipTests.cs`
- `src/OpenSage.Game.Tests/Logic/PlayerRelationshipTests.cs`
- `src/OpenSage.Tools.ReplaySketch.Core/Services/PlatformPaths.cs`

## Acceptance criteria

- [ ] All five files begin with the UTF-8 BOM (`EF BB BF`).
- [ ] `dotnet format src --verify-no-changes` reports no `CHARSET` errors.
- [ ] `dotnet build src` and `dotnet test src --filter Category!=Interactive` still pass.
- [ ] No textual code changes — encoding only.

## Validation commands

```sh
dotnet format src --verify-no-changes
dotnet build src
dotnet test src --filter Category!=Interactive
```

## Out of scope / guardrails

- Do not touch any other files flagged by the formatter for unrelated rules (e.g. `IDE1006`
  naming warnings in `ReplaySketchLog.cs` / `MapMetadataService.cs` — those predate P1).
- Encoding only; do not reformat or edit code.

## On completion

- Set Status to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`.

## Result

Fixed via `dotnet format src` (rewrote the five files with the UTF-8 BOM, no other content
changes — confirmed via `git diff`, only the BOM byte was added). `dotnet format src
--verify-no-changes` no longer reports any `CHARSET` errors. `dotnet build src` and
`dotnet test src --filter Category!=Interactive` pass.
