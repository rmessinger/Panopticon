# Task Card `WI-H2a-15`: Read the bounded EE2 DDF slice

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-H2a-15` |
| Roadmap phase | [H2a](../../track-h-storage-formats.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-H2a-03`, `WI-H2a-18` |
| Diagrams | [Architecture gate](../../deep-dives/h2a-unit-content-import.md#architecture-diagrams) |

## Goal

Read the reviewed EE2 records and their complete inherited dependency closure from the operator installation.

## Context to read (and only this)

- H2a deep dive: selected unit, source files, parser subset, limits, and archive precedence.
- `docs/system-investigations/EE2_UNIT_VISUAL_PIPELINE.md`.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs`: operator install/archive mounting pattern.
- Card 03's diagnostic/provenance contracts; exact ZIP filesystem path named by card 02.

## Steps

1. Add the bounded reader in the reviewed format-layer location, keeping source syntax internal to the EE2 adapter.
2. Parse required record names, scalars, blocks, arrays, comments, references, and parent declarations with source positions.
3. Test synthetic syntax, duplicate records, malformed/truncated input, nesting/size limits, and selected real-install closure.

## Files likely to change

- Proposed EE2 DDF reader files in the format-layer project selected by card 02.
- Synthetic reader tests in that project's test suite.
- H2a source evidence for the real-install closure.

## Acceptance criteria

- [ ] Reviewed gameplay and visual records parse without guessing absent values.
- [ ] Unsupported syntax/fields are classified with source positions; required gameplay cannot silently vanish.
- [ ] Synthetic tests are install-free; real-install coverage records exact source records and archive precedence.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

Card 02 supplies the exact explicit real-install test command before Ready.

## Out of scope / guardrails

No full EE2 database parser, extraction/redistribution of game assets, or runtime module construction. M2 expands this reader rather than replacing it.

## On completion

Update Status and H2a-15, reconcile card 16, and append validation to `.exchange`.
