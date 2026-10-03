# Task Card `WI-W3a-30`: Publish Sedan evidence

| Field | Value |
|---|---|
| Card ID | `WI-W3a-30` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W3a-28`, `WI-W3a-29`, `WI-W3a-27` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-28`, `WI-W3a-29`, and `WI-W3a-27` are Done.

Create the measure-only Sedan evidence report with provenance, visual links, and the W3a divergence table.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §10-§11 and §12.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §7-§8.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §11.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §7.

## Steps

1. Collect conservation, reconstruction (including verified A1-6 tactical reconstructions),
   determinism, order metrics, and run-family error artifacts.
2. Add A5 visual references for regions, cells, features, nodes, links, and source ids.
3. Populate the W3a §11 divergence table from measured outputs and historical reference values.
4. Record source provenance and run metadata for each table value.
5. State explicitly that historical values are reference points, not pass/fail thresholds.

## Files likely to change

- Western Front Sedan evidence report files
- Run-output report generation files
- Optional A5 report snapshot references

## Acceptance criteria

- [ ] Report records all W3a §11 evidence categories.
- [ ] Divergence table includes measured values, historical references, and provenance.
- [ ] No pass/fail threshold is set against history.

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~SedanEvidenceReport"
```

## Out of scope / guardrails

No retuning to match history. No new scenario behavior changes.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
