# Task Card `WI-C8b-02`: Capture the static BFME-family proof

| Field | Value |
|---|---|
| Card ID | `WI-C8b-02` |
| Roadmap phase | [Track C · C8b](../../track-c-external-assets.md): BFME-family static meshes |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `M` |
| Prerequisites | `WI-C8b-01` |
| Diagrams | [C8 architecture diagrams](../../deep-dives/c8-sage-family-w3d.md#9-architecture-diagrams) |

## Goal

Add a TestHarness `SageW3dProof` scenario that imports the section 5 proof models from BFME-family installs, loads their textures through the per-game texture resolvers, and captures them in bind pose beside a Generals Ranger.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c8-sage-family-w3d.md` sections 2, 5, and the proof sequence diagram.
- `src/Panopticon.Tools.TestHarness/Ee2NifProofScenario.cs` (`RunCore` from line 427) and `Ee2TexcacheTextureInstaller.cs`: scenario shape, texture registration, `StandaloneModelCapture`.
- `src/Panopticon.Simulation/Content/OnDemandAssetLoadStrategy.cs` (`CreateW3d`, `CreateTextureLoader`) and `src/Panopticon.Simulation/Content/IPathResolver.cs` (`PathResolvers`).
- `docs/screenshots.md`: capture and sharing recipe.

## Steps

1. Add `SageTextureInstaller.Install(Game game, FileSystem sourceFileSystem, OnDemandAssetLoadStrategy strategy, IEnumerable<string> names)`: for names not already in `game.AssetStore.Textures`, load through `strategy.CreateTextureLoader()` with an `AssetLoadContext` over the source file system, then `Textures.Add`. Return the count; report names that resolve nowhere.
2. Add `SageW3dProofScenario` (`--built-in-scenario SageW3dProof`) registered like `Ee2NifProof`. For each of `BFME_PATH`, `BFME2_PATH`, `BFME2_ROTWK_PATH` that is set, mount the install (RotWK over BFME2) and use that game definition's `CreateAssetLoadStrategy()` resolvers. Missing variables skip that game with an info line.
3. Import the section 5 models (BFME `gumaarms_skn`, `mumumakil_skn`, `gusiegtreb_skn`; BFME2 `gumaarms_skn`, `aucorship`, `aucrshp_s1_skn`; RotWK `kuorcrider_skn`, `kutsling_skn`), install textures from material texture properties and texture names, materialize, and capture each beside the Generals Ranger at front and three-quarter views.
4. Add installed tests (`SageW3dProofImportTests`, gated per game) asserting each proof model imports, every material resolves to `NormalMapped`, `Simple`, or fixed function, and every texture name resolves.
5. Run headless, write captures to the shared viewer directory under unique versioned names, and inspect: upright, textured, normal-mapped detail on BFME2 and RotWK models, plausible scale against the Ranger. Record scale ratios; do not change scale.

## Files likely to change

- `src/Panopticon.Tools.TestHarness/SageW3dProofScenario.cs`, `SageTextureInstaller.cs` (new)
- `src/Panopticon.Tools.TestHarness/BuiltInScenario.cs`, `Program.cs`, `Panopticon.Tools.TestHarness.csproj` if a BFME mod reference is needed
- `test/Panopticon.Simulation.Tests/Content/Importing/SageW3dProofImportTests.cs` (new)

## Acceptance criteria

- [ ] Installed tests pass for each available game.
- [ ] Captures show all eight models textured in bind pose beside the Ranger.
- [ ] Capture paths, the Ranger model name, and scale ratios are recorded in `.exchange`.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
CNC_GENERALS_PATH="/path/to/Command and Conquer Generals" \
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet run --project src/Panopticon.Tools.TestHarness -- --headless --built-in-scenario SageW3dProof
BFME_PATH="/path/to/BFME" BFME2_PATH="/path/to/BFME2" BFME2_ROTWK_PATH="/path/to/BFME2XP" \
  dotnet test src --filter "FullyQualifiedName~SageW3dProof"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No animation. That is `WI-C8c-04`.
- No team-color work. That is `WI-C8b-03`.
- Do not boot a BFME `Game`; the Generals `Game` renders the models.
- Do not commit game files, extracted textures, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C8b-03` to `Ready`.
- Append a result line to `.exchange`.
