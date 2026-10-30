# Task Card `WI-C12c-06`: SHOGUN 2 animated squad proof

| Field | Value |
|---|---|
| Card ID | `WI-C12c-06` |
| Roadmap phase | [Track C · C12c](../../track-c-external-assets.md): SHOGUN 2 animation and composite soldiers |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C12c-04`, `WI-C12c-05` |
| Diagrams | [C12 architecture diagrams](../../deep-dives/c12-total-war-shogun2-models.md#11-architecture-diagrams) |

## Goal

The `Shogun2Proof` scenario gains an animated stage: a seeded yari ashigaru squad renders with distinct variants playing `yari_charge`, and the horse plays an idle, with no missing targets and deterministic captures.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` sections 7, 11 (composite sequence).
- `src/Panopticon.Tools.TestHarness/Shogun2ProofScenario.cs` from `WI-C12b-05`.
- `docs/screenshots.md`.

## Steps

1. Add an animated stage: resolve clips with `Shogun2AnimationResolver`, compose 16 soldiers with `Shogun2UnitVariantComposer`, materialize, and play `yari_charge` beside a Generals Ranger; add the horse body playing its idle.
2. Capture fixed times (for example 0 s, 0.5 s, 1 s) and a GIF; report missing-target count, distinct compositions, and composition time per soldier.
3. Run twice with the same seed and confirm identical compositions and captures.
4. Save captures to the screenshot viewer under versioned names and record links in deep dive section 7.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Shogun2ProofScenario.cs`
- `docs/roadmap/deep-dives/c12-total-war-shogun2-models.md` section 7 (results)

## Acceptance criteria

- [ ] The scenario exits 0 with missing-target count 0.
- [ ] At least two distinct soldier compositions appear in the capture.
- [ ] Two same-seed runs match.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test test/Panopticon.Simulation.Tests --filter "Category!=Interactive"
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
SHOGUN2_PATH="/mnt/pit2/Network Storage/Transfer/Shogun 2 Complete Edition" CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" dotnet run --project src/Panopticon.Tools.TestHarness -- --built-in-scenario Shogun2Proof --headless
```

## Out of scope / guardrails

- Formation movement or simulation units (Track M owns battle use).
- No SHOGUN 2 asset or derived binary is committed; synthetic fixtures are generated in tests.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- Tick C12c in the checklist and update the README Track C progress row.
- Append a result line to `.exchange`.
