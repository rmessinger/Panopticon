# Task Card `WI-C10d-04`: Compressed model proof

> Audience: agent. Visual and corpus validation of compressed AoE3 models.

| Field | Value |
|---|---|
| Card ID | `WI-C10d-04` |
| Roadmap phase | [Track C · C10d](../../track-c-external-assets.md) |
| Status | `Blocked` |
| Type | `validation` |
| Effort | `M` |
| Prerequisites | `WI-C10d-03` |
| Diagrams | [C10d class and sequence diagrams](../../deep-dives/c10-aoe3de-granny-models.md#c10d-decompression-architecture) |

## Goal

Import and animate the Falconet and validate real compressed variants through existing proof tools.

## Context to read (and only this)

- `src/Panopticon.Tools.TestHarness/AGENTS.md`
- `Aoe3GrannyProofScenario.cs` and `docs/screenshots.md`.
- Deep dive C10c results and section 7.

## Steps

1. Replace the expected Falconet compression failure with import and animation assertions.
2. Capture the Falconet playing `falconet_limber` and available type-4 content.
3. Replace the redcoat material overlay with the actual variant model when available.
4. Keep accepted scales, materials, and grounded perspective comparison behavior unchanged.
5. Publish uniquely versioned stills and motion evidence, then inspect every changed proof.
6. Record corpus counts, exclusions, validation, and evidence in the deep dive and PR.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Aoe3GrannyProofScenario.cs`
- Granny/AoE3 interactive tests.
- Deep dive, track status, checklist, and indexes.

## Acceptance criteria

- [ ] Falconet imports and plays `falconet_limber` with coherent geometry.
- [ ] Available type-4 content is validated, or its corpus absence is recorded.
- [ ] Real redcoat mesh replaces the mismatched overlay where supported.
- [ ] Existing musketeer and horse proofs remain visually correct.
- [ ] Evidence and validation are linked in the open group-4 PR.

## Validation

Run the proof under Xvfb, review captures, run Granny/AoE3 and non-interactive suites, build,
format, and brace checks. Stop on determinism deviations and preserve artifacts.

## Out of scope / guardrails

No proprietary SDK, committed game assets, merges, or unrelated rendering changes.

## On completion

Set Status to `Done`, propagate phase status, and append a dated `.exchange` entry.
