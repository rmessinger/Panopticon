# Task Card `WI-W3a-09`: Transcribe the French order of battle

| Field | Value |
|---|---|
| Card ID | `WI-W3a-09` |
| Roadmap phase | [Track W · W3a](../../track-w-western-front.md): Sedan 1870 |
| Status | `Draft` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-W3a-08` |
| Diagrams | [W3a §13](../../deep-dives/w3a-sedan-1870.md#13-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3a-08` is Done.

Create the committed French Sedan order-of-battle JSON from public-domain primary sources.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3a-sedan-1870.md` §5.1-§5.3 and §12.
- Public-domain sources named in W3a §5.1: Moltke and Forbes 1893, Clarke's Prussian General Staff translation, and Hooper 1887.
- `docs/roadmap/deep-dives/w0-charter-fixture.md` §3.

## Steps

1. Transcribe I, V, VII, and XII corps, reserve cavalry, army reserve artillery, and the Sedan garrison.
2. Include fortress pieces and about 70 mitrailleuses with formation or fortress holdings.
3. Record nominal strength and estimated 31 August field strength, including Beaumont losses where estimated.
4. Add initial assembly areas as lat/lon polygons with source citations.
5. Add `source` and `confidence` values of `stated`, `derived`, or `estimated` to every entry.

## Files likely to change

- `src/Panopticon.Simulation.WesternFront/Data/Sedan/FrenchOrderOfBattle.json`
- Western Front order-of-battle dataset tests

## Acceptance criteria

- [ ] Every French battalion, battery, and squadron has a public-domain primary source.
- [ ] Modern secondary sources are used only as cross-checks, not as cited data sources.
- [ ] Totals match W3a §5.2 within stated confidence.
- [ ] Assembly-area polygons include source citations.

## Validation commands

```sh
dotnet test src --filter "FullyQualifiedName~FrenchSedanOrderOfBattle"
```

## Out of scope / guardrails

No uncited modern secondary-source transcription. No invented lower-echelon entries beyond template-owned expansion.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
