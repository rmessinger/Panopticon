# Task Card `WI-C10d-01`: Decompressor provenance gate

> Audience: agent. Read-only source review before decompressor implementation.

| Field | Value |
|---|---|
| Card ID | `WI-C10d-01` |
| Roadmap phase | [Track C · C10d](../../track-c-external-assets.md): Granny section decompression |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C10b-01` |
| Diagrams | Review establishes the source and integration boundaries for subsequent diagrams. |

## Goal

Record file-level provenance and license terms for candidate Oodle1 and BitKnit2 implementations. Approve only demonstrably reusable code; otherwise record the blocker before implementation.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md`, sections 6 and 7.
- `src/Panopticon.FileFormats.Granny/GrannyFile.cs` and existing section tests.
- Candidate upstream decompressor files, their history, notices, and license files.

## Steps

1. Pin candidate repositories and revisions, then trace each relevant file's origin and terms.
2. Separate repository-wide licensing from file-level exceptions and proprietary dependencies.
3. Record permitted implementation sources, required attribution, supported platforms, and unresolved claims.
4. Open source-edit cards only after approval and grounded class/sequence diagrams.

## Files likely to change

- This card.
- `docs/roadmap/deep-dives/c10-aoe3de-granny-models.md`.
- `docs/roadmap/open-questions.md`.
- Track C resources and implementation-plan indexes.

## Acceptance criteria

- [x] Source revisions and file-level provenance are recorded.
- [x] A clear approval or blocker is recorded for each codec.
- [x] Required notices and proprietary-code exclusions are explicit.

## Validation

Read-only review. Verify cited upstream files and revisions; no build required.

## Out of scope / guardrails

- No decompressor code is copied or ported before approval.
- No proprietary Granny SDK source or binaries are used.

## On completion

- Set Status to `Done` or `Blocked`, with the result in the deep dive.
- Append a dated result to `.exchange`.
