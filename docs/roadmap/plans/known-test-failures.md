# Known test failure remediation

> Part of the [Large-Scale RTS Roadmap](../README.md).
>
> Audience: human + agent.
>
> Planning snapshot: 2026-10-18. Status: proposed, implementation not started.

## Scope and evidence

Retire the eight recurring non-interactive simulation-test failures without reducing coverage or weakening assertions. No phase dependencies or statuses change.

Historical evidence: `/tmp/opencode/a5-maincheck-full.log` records eight failures, 2339 passes, and eight skips. `/tmp/opencode/m4-g6-full.log` records the same failure set. These local logs are not a fresh run of current `main`; step 1 establishes the current baseline. The older [underbody handoff](../../handoff-underbody-verification.md) lists seven of these failures, omitting the parking test.

## Failure inventory

Test paths below are relative to `test/Panopticon.Simulation.Tests/`.

| ID | Test | Recorded symptom | Investigation target |
|---|---|---|---|
| TF-01 | `Data/InstallationLocator/SteamLocatorTests.FindInstallations_FindsGeneralsOnThisMachine` | Steam discovery returns no installations. | `GameFact` permits any Generals installation, not necessarily a Steam installation. The test also assumes exactly one result. |
| TF-02 | `Data/Ani/CurFileTests.CanReadAniCursors` | No `.cur` files found. | Corpus availability and extension-specific prerequisites in `InstalledFilesTestData`. |
| TF-03 | `Data/Map/BseFileTests.CanRoundtripBases` | No `.bse` files found. | Corpus availability and extension-specific prerequisites in `InstalledFilesTestData`. |
| TF-04 | `Data/Wnd/WndFileTests.CanReadWndFiles` | Null reference parsing `Window/ControlBar.wnd`. | `WndParser.ParseDrawDataItem` dereferences `_assetStore` for named images; verify parser-only test setup and intended asset-resolution contract. |
| TF-05 | `Data/Scb/ScbFileTests.CanRoundtripScbFiles` | `Data/Scripts/MultiplayerScripts.scb`: expected 6915 bytes, wrote 6908. | Reader/writer field preservation and string encoding. Cause unconfirmed. |
| TF-06 | `Data/Map/MapFileTests.CanRoundtripMaps` | `maps/deepdivide/deepdivide.map`: expected 2908861 bytes, wrote 2908978. | Chunk-level roundtrip diff, including fixed-length strings and unknown data. Cause unconfirmed. |
| TF-07 | `Logic/Object/Behaviors/ParkingPlaceBehaviorTests.ParkingPlaceBehavior_ReportLanding` | Third landing clearance returns runway 0 instead of -1 while both landing slots are occupied. | Runway reservation and release semantics in `ParkingPlaceBehaviour`; distinguish fixture assumptions from a behavior defect. |
| TF-08 | `Tools/ReplaySketch/P2ScenarioMatrixTests.Matrix_AllCells_AIPlaysLegalGame` | Replay parser indexes the final element of an empty chunk list. | Empty replay export from the matrix scenario and `ReplayFile`'s empty-stream contract. AI assertions have not yet been reached. |

Additional failures observed at baseline commit `82b280417f53f039914611930c1aa039d2fd9b3d` (2026-10-30), both reproduced in isolation:

| ID | Test | Recorded symptom | Investigation target |
|---|---|---|---|
| TF-09 | `FX/TerrainScorchDataTests.InstalledMaps_RecordScorchProducers` | Missing `maps/heavyfire-preview/heavyfire-preview.map`. | Broken installed-corpus symlink to `/tmp/opencode/m0-maps/Command & Conquer Generals/Maps/heavyfire/heavyfire.map`; prerequisite-blocked on this host. |
| TF-10 | `Tools/ReplaySketch/P6cSpecialPowerTests.HijackerUpdate_HijacksEnemyVehicleOnContact` | Vehicle owner remains USA (player 2), expected GLA (player 4), after 300 movement frames. | Contact-hijack movement and ownership transfer; cause unconfirmed. |

## Machine-readable baseline

[`test/known-failures.json`](../../../test/known-failures.json) is the machine-readable baseline, derived from baseline TRX runs. Test names match TRX `UnitTestResult.testName` values verbatim, including runner-specific display formatting. This document remains the remediation plan.

## Execution order

### 1. Establish a reproducible baseline

- Record commit, SDK, OS, installation paths, enabled game definitions, and test command.
- Run the full non-interactive suite with a unique results directory and TRX output. Compare exact failing test names with the inventory; investigate additions separately.
- Reproduce each failure in isolation. Record assertion, input path, first failing matrix cell, and stack trace.
- Preserve failure artifacts under unique directories in `/tmp/opencode/test-failure-remediation/`. Installed assets remain local and uncommitted.
- Before running roundtrip tests together, isolate their working directories or give diagnostics unique per-test paths. `TestUtility` currently overwrites `original.bin` and `serialized.bin`.
- Gate: a current failure manifest distinguishes reproduced, resolved, and prerequisite-blocked entries. Historical totals are not acceptance criteria.

### 2. Correct installation and corpus assumptions: TF-01 through TF-03

- Exercise Steam discovery against controlled temporary Steam library/VDF layouts through the existing seams, or add the smallest required discovery seam. Cover valid, absent, multiple-library, and multiple-installation cases.
- Keep any real-machine Steam smoke test explicitly conditional on Steam-specific prerequisites. A non-Steam Generals installation must not make it eligible.
- Add small synthetic `.cur` and `.bse` fixtures for unconditional parser/roundtrip coverage. Audit existing fixtures before adding new ones.
- Keep installed-corpus tests separate. Missing optional corpus produces an explicit prerequisite skip, not a silent return. If a configured corpus is expected to contain matching files, zero matches remains an actionable failure.
- Gate: synthetic tests pass without a game installation; applicable installed-corpus tests actually process files. New skips report their missing prerequisite.

### 3. Repair parsing and serialization contracts: TF-04 through TF-06

- TF-04: establish whether WND parsing requires an asset store. Supply a valid store when required, or support unresolved image names explicitly if parser-only use is supported. Do not suppress missing-image errors with a blanket null conditional. Cover `NoImage`, named images, and missing references.
- TF-05/TF-06: capture original and serialized bytes, first differing offset, chunk/field boundaries, and second-serialization results before selecting a fix.
- Preserve unknown fields, padding, and source encodings where the format requires lossless roundtripping. Add focused synthetic regressions for the diagnosed differences, then rerun the installed corpus.
- If canonicalization is intentional, document and obtain agreement on the format contract before replacing byte equality with targeted semantic and serialization-stability assertions. Do not set `skipRoundtripEqualityTest` merely to pass tests; length equality is still enforced by that helper.
- Gate: affected fixtures and installed corpora meet their agreed contracts; neighboring format tests remain green.

### 4. Resolve aircraft landing reservations: TF-07

- Trace spawn, taxi, departure, inbound, landing, and landed transitions against the failing fixture and current aircraft lifecycle callers.
- Fix reservation overwrite if production behavior violates the contract. Update the fixture only if an intentional lifecycle change explains the discrepancy, with replacement coverage for the original safety property.
- Cover full runways, repeated requests, departure/landing coexistence, cancellation or destruction, and reservation release.
- Gate: no simultaneous landing reservations share a runway; parking and aircraft lifecycle tests pass. Validate a live multi-aircraft landing sequence if runtime behavior changes, following [screenshot guidance](../../screenshots.md).

### 5. Unblock the P2 matrix: TF-08

- Preserve the generated replay and scenario for the first failing cell before the test's `finally` cleanup deletes its temporary directory.
- Trace `HarnessRunner.RunAndAssert`, replay export, and `ReplayFile.FromFileSystemEntry`. Decide whether a zero-order replay is valid or indicates a missing required bootstrap order.
- Add parser/writer coverage for empty-order, one-order, malformed/truncated, and inconsistent-timecode inputs. Invalid replays must produce descriptive format errors, not collection-index exceptions.
- Correct the exporter or parser contract, then execute all 18 faction/map cells. Investigate any newly reached AI failures separately; retain building, unit, attack-order, and nonnegative-money assertions.
- Gate: every matrix cell reaches and passes its assertions. An empty successful report or parser guard alone does not close this item.

### 6. Integration and closeout

- Implement bounded changes as separate commits, retaining per-item evidence and regression tests. No fixes are authorized by this planning document alone.
- Rerun targeted tests, then the complete non-interactive solution suite with the same installation configuration as step 1.
- Run build, format verification, and the brace-newline check for source changes. ReplaySketch changes also require screenshot-mode verification with the map panel intact.
- Report final pass/fail/skip counts and remaining prerequisite exclusions. Replace recurring known-failure claims with a link to the closeout evidence rather than rewriting historical results.
- Gate: no remaining reproduced inventory failures, no new failures, and no added unconditional skips or weakened assertions.

## Validation commands

Use a new results path for each run. The example Generals installation is this host's existing installation.

```sh
CNC_GENERALS_PATH='/home/rmessinger/dev/Command and Conquer Generals' \
  dotnet test src --filter 'Category!=Interactive' \
  --logger trx --results-directory /tmp/opencode/test-failure-remediation/baseline-01

dotnet build src
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

TF-01 through TF-08 are ordinary failures unless new evidence identifies a determinism-enforcing assertion. Same-seed, golden-baseline, or run-output/replay byte-identity failures require preserving artifacts and stopping for operator determination under the repository validation policy. The existing MAP/SCB format roundtrip checks are not simulation determinism gates.
