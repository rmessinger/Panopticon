# Task Card `WI-C7b-05`: Capture the static SC2 proof models

| Field | Value |
|---|---|
| Card ID | `WI-C7b-05` |
| Roadmap phase | [Track C · C7b](../../track-c-external-assets.md): StarCraft II static M3 models |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C7b-04` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Import the three proof units from the install in installed tests, and capture them textured in bind pose beside a Generals Ranger through a TestHarness `Sc2M3Proof` scenario.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 6, 9, and 13.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs` and its registration in `BuiltInScenario.cs` and `Program.cs:244-246`: scenario shape, env-var gate, capture output.
- `test/Panopticon.Simulation.Tests/Content/Importing/Ee2NifImportTests.cs`: installed-test pattern.
- `docs/screenshots.md`: capture and sharing recipe.

## Steps

1. Add `Sc2M3ImportTests` (`Category=Interactive`, `STARCRAFT_II_PATH`): for each proof path, assert vertex counts (Marine 1,678, Zealot 2,619 after skipping the glow batch, Zergling 775), sub-object counts, material texture bindings, and no exceptions. Assert the Zealot diagnostic lists the skipped batch 0 (material 2, additive blade glow).
2. Add `Sc2M3ProofScenario` (`--built-in-scenario Sc2M3Proof`): mount `CascFileSystem` from `STARCRAFT_II_PATH`, import the three models, and place them in a row beside the Generals `AIRngr_SKN` Ranger (or the nearest Generals infantry model if that name differs; record which). Render front and three-quarter views.
3. Run it headless and write captures to the shared viewer directory. Inspect: units upright, facing +X like the Ranger, similar height (Marine about 8.8 units), textures correct with normal-mapped detail and no inverted lighting.
4. If scale or facing looks wrong, stop and report with captures. Do not change the section 9 constants.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/Sc2M3ProofScenario.cs` (new), `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `src/Panopticon.Tools.TestHarness/Program.cs`, `src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj` if a reference is needed
- `test/Panopticon.Simulation.Tests/Content/Importing/Sc2M3ImportTests.cs` (new)

## Acceptance criteria

- [ ] Installed tests pass with the counts in step 1.
- [ ] Captures show all three units textured and upright, facing +X, beside the Ranger.
- [ ] Capture paths and the Ranger model name are recorded in `.exchange`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario Sc2M3Proof
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet test src --filter "FullyQualifiedName~Sc2M3"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not tune scale or axis constants. Escalate instead.
- No skinning or animation.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, set C7b Status to `Done` in the track doc, tick the checklist item, and change `WI-C7c-02` to `Ready` if `WI-C7c-01` is done.
- Append a result line to `.exchange`.
