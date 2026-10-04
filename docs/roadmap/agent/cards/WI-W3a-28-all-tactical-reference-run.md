# Task Card `WI-W3a-28`: Run all-tactical reference seeds

| Field | Value |
|---|---|
| Card ID | `WI-W3a-28` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W3a-01`, `WI-W3a-15`..`WI-W3a-26`, `WI-W3a-31`, `WI-W2-13`, `WI-A1-24` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-01`, `WI-W3a-15` through `WI-W3a-26`, `WI-W3a-31`, `WI-W2-13`,
and `WI-A1-24` are Done.

Add and run the Sedan all-tactical reference family for four seeds over the full 60 h window.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §10-§11 and §12.
- `docs/roadmap/deep-dives/w2-fidelity-promotion.md` §3 and §7.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §6-§7.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §11.
- `docs/roadmap/deep-dives/a1-6-operational-recording.md` §4-§6.

## Steps

1. Add Sedan reference run wiring with `AllTacticalActivationPolicy` from frame zero.
2. Require the operator geospatial payload and a Generals install before execution.
3. Run four seeds from 31 August 00:00 through 2 September 12:00 local mean time.
4. Record in `Operational` mode; the operational stream and checkpoints are the primary product.
5. Rebuild at least three sampled tactical intervals per seed (early, middle, late) from their
   checkpoints and check conservation on the rebuilt frames.
6. Stop and preserve artifacts on a checkpoint restore failure per the repository rule.

## Files likely to change

- Western Front Sedan experiment files
- Western Front reference-run tests
- Run-output artifact configuration

## Acceptance criteria

- [ ] Four reference seeds complete the 60 h window.
- [ ] Every run uses all-tactical activation from frame zero.
- [ ] Product records contain operational state layers, rollups, metrics, and checkpoints.
- [ ] Sampled tactical intervals rebuild from checkpoints and conserve.
- [ ] Standard validation passes.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet build src
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet test src --filter "FullyQualifiedName~SedanAllTacticalReferenceRun"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Do not lower seed count or switch activation policy for speed without marking the card Blocked.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
