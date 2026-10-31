# Task Card `WI-C10d-03`: Compressed reader integration

> Audience: agent. Route compressed Granny sections through the managed codecs.

| Field | Value |
|---|---|
| Card ID | `WI-C10d-03` |
| Roadmap phase | [Track C · C10d](../../track-c-external-assets.md) |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-C10d-02` |
| Diagrams | [C10d class and sequence diagrams](../../deep-dives/c10-aoe3de-granny-models.md#c10d-decompression-architecture) |

## Goal

Assemble compressed section buffers and auxiliary tables before the existing relocation and
type-tree pipeline, preserving uncompressed behavior.

## Context to read (and only this)

- Deep dive section 6 and the linked diagrams.
- `GrannyFile.cs`, `GrannySection.cs`, and codec APIs.
- Existing Granny section, relocation, and type-tree tests.

## Steps

1. Retain and validate section stop offsets and compressed file ranges.
2. Dispatch types 0, 2, and 4; reject unsupported types contextually.
3. Decode BitKnit2 relocation and marshalling streams with validated size prefixes.
4. Reuse relocation and type-tree logic over the assembled output.
5. Add compressed reader tests and verify uncompressed models remain unchanged.

## Files likely to change

- `src/Panopticon.FileFormats.Granny/GrannyFile.cs`
- `src/Panopticon.FileFormats.Granny/GrannySection.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/Aoe3/`

## Acceptance criteria

- [ ] Valid compressed data and auxiliary tables reach the existing type-tree reader.
- [ ] Corrupt sizes, tables, offsets, and unsupported codecs name the file and section.
- [ ] Uncompressed reader tests remain passing.
- [ ] Available compressed model and animation files parse without native dependencies.

## Validation

Targeted Granny tests and local install corpus checks; standard build, format, and brace checks.

## Out of scope / guardrails

No changes to model scale, texture mapping, or runtime animation conventions.

## On completion

Set Status to `Done`, record validation, and append a dated `.exchange` entry.
