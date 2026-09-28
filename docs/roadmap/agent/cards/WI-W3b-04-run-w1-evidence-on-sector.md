# Task Card `WI-W3b-04`: Run W1 evidence on generated sector

| Field | Value |
|---|---|
| Card ID | `WI-W3b-04` |
| Roadmap phase | [Track W · W3b](../../track-w-western-front.md): Somme sector |
| Status | `Draft` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-W3b-03`, W1 evidence cards |
| Diagrams | [W3b §5](../../deep-dives/w3b-somme-sector.md#5-architecture-diagrams) |

## Goal

Draft: flip to Ready once `WI-W3b-03` and W1 evidence cards are Done.

Run the W1 conservation, reconstruction, fixed-seed, and metrics evidence against a generated-sector fixture.

## Context to read (and only this)

- `docs/roadmap/deep-dives/w3b-somme-sector.md` §3.
- `docs/roadmap/deep-dives/w1-aggregate-sector.md` §11.

## Steps

1. Create a small synthetic B4b payload fixture suitable for CI.
2. Run W1 scenario setup on the generated topology.
3. Reuse W1 evidence assertions.
4. Record generated topology counts in test output.

## Files likely to change

- Western Front evidence tests
- Synthetic B4b payload test fixture

## Acceptance criteria

- [ ] W1 evidence passes on generated topology.
- [ ] The test fixture remains small and committed source data is synthetic.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneratedSectorW1Evidence"
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No operator Somme payload requirement in CI.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Update the roadmap phase Status if this card completes it.
- Append a result line to `.exchange`.
- List any follow-up cards to create here:
  - none
