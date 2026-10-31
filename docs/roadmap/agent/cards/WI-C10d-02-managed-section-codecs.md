# Task Card `WI-C10d-02`: Managed section codecs

> Audience: agent. CPU-only Oodle1 and BitKnit2 decoders.

| Field | Value |
|---|---|
| Card ID | `WI-C10d-02` |
| Roadmap phase | [Track C · C10d](../../track-c-external-assets.md) |
| Status | `Done` |
| Type | `implementation` |
| Effort | `L` |
| Prerequisites | `WI-C10d-01` |
| Diagrams | [C10d class and sequence diagrams](../../deep-dives/c10-aoe3de-granny-models.md#c10d-decompression-architecture) |

## Goal

Decode compression types 2 and 4 in managed code with exact output sizes and bounded reads.

## Context to read (and only this)

- Deep dive sections 6 and 7 and the linked diagrams.
- Approved pinned upstream sources from `WI-C10d-01`.
- `src/Panopticon.FileFormats.Granny/` and existing Granny tests.

## Steps

1. Preserve required upstream notices alongside derived code.
2. Implement `Oodle1Decoder` with the three dictionary stop offsets.
3. Implement `BitKnit2Decoder` with bounded output and overlapping backreferences.
4. Add synthetic valid-stream and malformed-input tests for both codecs.
5. Compare local game section output with approved independent decoders without committing assets.

## Files likely to change

- `src/Panopticon.FileFormats.Granny/Oodle1Decoder.cs`
- `src/Panopticon.FileFormats.Granny/BitKnit2Decoder.cs`
- Codec supporting types and notices in the same project.
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/`

## Acceptance criteria

- [x] Both codecs run without native libraries on Linux.
- [x] Synthetic valid streams decode to known bytes; malformed streams fail contextually.
- [x] Output lengths and backreference ranges are validated.
- [x] Independent decoder comparison passes on available compressed sections.
- [x] Required attribution is retained; no proprietary SDK material is used.

## Validation

Targeted codec tests, `dotnet build src`, formatting, and brace checks. Preserve artifacts and
stop on suspected determinism deviations.

## Out of scope / guardrails

- No importer, rendering, scale, or material changes.
- No game assets or proprietary binaries in the repository.

## On completion

Set Status to `Done`, record validation, and append a dated `.exchange` entry.

Validation: 27 codec tests pass, including independent section-output hashes, malformed
streams, bounded padding, and termination cases. Oodle1 output matches blendergranny;
24 BitKnit2 sections from six files match pybg3. `dotnet build src` passes with existing
nullable warnings. Attribution and pinned reference revisions are in `CODEC-NOTICES.md`.
