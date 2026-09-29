# Task Card `WI-W3a-22`: Transcribe the orders timeline

| Field | Value |
|---|---|
| Card ID | `WI-W3a-22` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W3a-09`, `WI-W3a-10` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-09` and `WI-W3a-10` are Done.

Create the committed Sedan orders timeline JSON with sources and confidence for every order and command change.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §8.2-§8.3 and §12.
- Public-domain sources named in W3a §5.1: Moltke and Forbes 1893, Clarke's Prussian General Staff translation, and Hooper 1887.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §4.1-§4.4.

## Steps

1. Define order entries with id, reported time, issuer key, recipient key, objective, target, method, source, and confidence.
2. Add objective kinds `retire-on`, `cross-at`, and `assemble` beside W1 objectives.
3. Add command-change entries for MacMahon to Ducrot and Ducrot to Wimpffen on 1 September.
4. Record all times in Sedan local mean time, UTC+00:19:48, with source basis notes.
5. Exclude capitulation and ceasefire orders.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Data/Sedan/SedanOrdersTimeline.json`
- Western Front orders timeline validation tests

## Acceptance criteria

- [ ] Every order and command change has a source and confidence.
- [ ] Issuer and recipient keys resolve against the order-of-battle datasets.
- [ ] New objective kinds validate with target and method fields.
- [ ] Capitulation and ceasefire orders are absent.

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~SedanOrdersTimeline"
```

## Out of scope / guardrails

No emergent capitulation or ceasefire logic. No unsourced timeline entries.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
