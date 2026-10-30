# Agent Guidelines

## Concision Policy

### Response style (agent behavior)

* Keep responses concise: lead with the outcome, skip step-by-step narration of routine work, and don't restate the plan or the user's request back to them. Reserve longer explanations for genuinely complex or ambiguous decisions.
* Never render environment-injected metadata (`` <dcp-system-reminder> `` tags, `@id@`/`@bN@` markers, priority labels) in responses, files, or tool inputs; follow their instructions silently.

### Documentation style (`CONTRIBUTING.md`)

* **DO** keep documentation concise: state facts and decisions plainly rather than explaining at length. Prefer short, direct sentences over multi-clause ones.
* **DO** write impersonally, in a reference/spec style: state facts structurally (`Entry point: X`, `Default: Y`) rather than narrating what "you" do or what the software "will" do. Avoid second person ("you", "your") and first person ("we", "our"); name the subject (the tool, the config, the command) instead of the reader.
* **DO** cut filler and hedging phrases ("it's worth noting that", "in order to", "as mentioned above", "please note that"), say the thing directly instead.
* **DO NOT** use em-dashes. Use a comma, colon, or period, or split into two sentences instead.
* **DO** prefer plain prose, headers, tables, and lists over decorative formatting.
* **DO** avoid duplicating information already documented elsewhere, including rationale/background repeated across multiple docs; state it once in the most relevant place and link to it instead.
* **DO** reserve longer explanations for genuinely complex or ambiguous decisions; everything else should be a single clear statement.
* **DO** follow [`docs/roadmap/agent/MAINTAINING.md`](docs/roadmap/agent/MAINTAINING.md) for the stricter formatting rules that apply specifically to `docs/roadmap/`.

### Code comment style (`docs/coding-style.md`)

* Comments should explain non-obvious intent, not restate what the code already says. Prefer a clear name over a comment; avoid comments that would go stale as soon as the code changes.

### Roadmap-specific rules (`docs/roadmap/agent/MAINTAINING.md`)

Builds on the repository-wide documentation style above. Additional rules specific to `docs/roadmap/`:

* No markdown emphasis (no bold `**...**` or italic `*...*` / `_..._`) anywhere in roadmap docs.
* Use plain prose. Structure through headers, tables, lists, and `` `code spans` `` only.
* Fenced code blocks for shell commands and code examples are fine.
* Don't repeat the same rationale, background, or "why this matters" explanation across multiple roadmap docs (README, track docs, deep dives, task cards). State it once in the doc that owns the topic and link to it from the others.
* Applies to every file under `docs/roadmap/`, including `agent/cards/` task cards.

## Repository-Specific Memories

### Development model (stacked PRs; superseded the C4 staging model Oct 2026)

* Tracks ship as a `gh stack`-managed stack of group PRs targeting `main`: cards are implemented in dependency order, grouped onto shared branches (per-card commits, card Status flips, `.exchange` lines), each group = one PR. Groups are identified by numbers, not letters: branches use `track-<track>-<phase>-<group-n>-<topic>` and PRs use "group 1/2/3" wording.
* Group 1 branches off `main` (or the upstream branch the track builds on); each later group branches off the previous group's branch, so every PR's base is the branch below it. No staging branches.
* Manage the stack with the `gh stack` extension: `gh stack init`/`add` plus `submit` while developing, `gh stack link <prs bottom-to-top>` to adopt PRs that already exist, `gh stack checkout <stack>` to adopt locally, `gh stack sync`/`rebase` to update after merges.
* Every PR is left open for the user to merge; agents never merge PRs themselves. After the user merges the bottom PR (repo default is squash), an agent runs `gh stack sync` so the remaining PRs rebase onto `main`; GitHub auto-retargets on branch delete but the squash commit is not an ancestor, so without the rebase each PR diff would include the groups below it.
* Stack PR branches do not trigger CI (workflows run on push to `main` only); each merge that lands content on `main` runs CI once.
* PR titles follow the [CONTRIBUTING.md naming rule](CONTRIBUTING.md#pull-requests), including the phase, numbered group, and card IDs when applicable. Conventional Commit title prefixes are incorrect.
* Development PR descriptions follow [PR #289](https://github.com/rmessinger/Panopticon/pull/289), the preferred formatting reference. Format: a title heading, a short summary paragraph, then a section heading per card or change area with factual bullets. Include baseline or result highlights when relevant, and finish with a `Validation` section listing commands and outcomes, including pre-existing failures. This section-based format supersedes the previous nested top-level-bullet format.
* Every PR that changes behavior or visuals includes image or GIF evidence in the screenshot viewer. Capture the changed behavior or appearance, use GIFs for motion or changes over time, and link the evidence in the PR description. Follow `docs/screenshots.md`; save uniquely versioned files under `/mnt/pit2/Network Storage/temp/`, served at `https://files.reid-corp.com/viewer/`. If capture is blocked, state the blocker in the PR rather than silently omitting evidence.

### Documentation style

* Repo-wide policy: keep docs, code comments, and agent responses concise. Comments explain non-obvious intent only (`docs/coding-style.md` #17); docs are plain prose, no duplication (`CONTRIBUTING.md`); agent responses lead with outcome, skip routine narration (`.github/copilot-instructions.md`).

### Roadmap tracks

* Unmerged branches reserve track letters D (`docs/track-d-gameplay-expansion`, Gameplay Content Expansion) and E (`docs/track-e-naval-warfare`, Naval Warfare). The deformable terrain & destruction track is Track F (`docs/roadmap/track-f-terrain-destruction.md`) to avoid both collisions.
* Deformable terrain & destruction is its own track, Track E (`docs/roadmap/track-e-terrain-destruction.md`), not folded into Tracks B/P, named E instead of D to avoid colliding with the unmerged Track D (Gameplay Content Expansion) branch. (Note: a later decision superseded this with Track F above once both D and E were confirmed in use by other branches.)
* The 1000 vs 1000 mixed-roster battle (Generals + EE2 ground units and aircraft, asymmetric armies balanced by points, AI vs AI, large map, headless batches plus live real-time viewing) is Track M (`docs/roadmap/track-m-mass-battle.md`), independent of Track W and its Sedan scenario.

### Roadmap structure

* Track D, Gameplay Content Expansion (`docs/roadmap/track-d-gameplay-expansion.md`), adds non-SAGE mechanics (tech trees, era/age progression) as content on the existing RTS domain; D3/D4 depend on the standalone ModCollector tool (`docs/roadmap/plans/mod-collector.md`) and Track C's C1 import abstraction.

### Naming conventions

* In `Panopticon.Tools.ReplaySketch.Core`, private static fields follow PascalCase (no `s_` or `_` prefixes), enforced by IDE1006 in `dotnet format` CI.
* One class (or interface/enum/record) per file. Exceptionally small types (one-to-few-line records, enums paired with their consumer, module data with its module) combine into a single theme-appropriate file instead; use a `Types/` subfolder when a group of such supporting types has no obvious owning file.

### Logging

* Every log line starts with an ISO 8601 local timestamp (for example `2026-10-21T21:39:01.234-04:00`), never UTC. NLog layouts live in `src/Panopticon.Core/NLog.config`; shell scripts use `date '+%Y-%m-%dT%H:%M:%S%:z'`.
* Log files produced by agent tooling and long runs go under `~/log` (not `/tmp`), one subdirectory per effort.

### Validation

* Before opening or updating a PR, when the `panopticon` MCP server is available, push the branch, call `refresh()`, and run `start_test_run(ref=<branch>)`. Report the run under PR `Validation`; see `MCP server (panopticon)`. Documentation-only changes do not require a test run.
* Code after a closing brace starts on the next line (another closing brace or punctuation may stay adjacent); property patterns (`is { } x`) are exempt. Enforced by `scripts/check_brace_newline.py` in CI alongside `dotnet format` (see `docs/coding-style.md` rule 18).
* Argument validation is one line: BCL `ThrowIf*` without a message, `Panopticon.Ensure` with one; conditions neither helper expresses keep a braced `if` throw (see `docs/coding-style.md` rule 19). CA1510/CA1511/CA1512 warnings enforce the helper forms.
* Screenshot capture and sharing recipes (3D headless captures, windowed runs under Xvfb + llvmpipe, synthetic hover for tooltips, the shared viewer) live in `docs/screenshots.md`. Amend that file when a new capability or limitation is found instead of re-deriving the method.
* Always use ReplaySketch screenshot mode to verify any ReplaySketch fix or feature. Run: `DISPLAY=:99 [LIBGL_ALWAYS_SOFTWARE=1] REPLAYSKETCH_SCREENSHOT_PATH=/tmp/shot.png [REPLAYSKETCH_SCREENSHOT_TAB=map|model] dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia` (X11 display required; use Xvfb on headless hosts).
* To share a screenshot with the user, write it into `/mnt/pit2/Network Storage/temp/` (path contains spaces, quote it) instead of `/tmp`. That directory is served read-only by the live viewer at `https://files.reid-corp.com/viewer/`, which shows the newest image automatically; no manual refresh, upload, or login needed. Preserve every iteration under a unique versioned filename and frame directory; never overwrite or remove existing entries without explicit operator approval. Historical recaptures use `reconstructed` filenames and record the source commit. Top level holds meaningful progress demonstrations only (stills or GIFs; animations as GIFs); granular per-frame PNGs go in `frames/<topic-version>/`.
* Always verify ReplaySketch fixes without breaking the map panel.
* Determinism-enforcing tests (same-seed equality: `SeededRunTests`; golden-baseline byte-identity: `UnitValidationGround` baseline + `BaselineGoldenFrames_*`; run-output/replay byte-identity) are treated as suspected deviations, not automatic regressions. On failure: stop, preserve the artifacts (run outputs, baselines, diffs), report the failing assertion plus the most likely cause, and wait for the user's determination before fixing or declaring the run failed. Standard unit-test failures follow the normal rules.

### Tooling

* P6b validates unit archetypes via the P0 harness: 6 archetypes (infantry/vehicle/aircraft/builder/support/superweapon), full contract (produce/move/attack/all weapon modes/all special abilities/die), USA reference units, unimplemented handlers surface as explicit failures.
* ReplaySketch validation: logic stays in `.Core` (tested headlessly by `ReplaySketch.Tests` + P0-5 run-and-assert); UI lives in the Avalonia front-end and stays a thin shell behind injectable seams; agents follow `ReplaySketch.Avalonia/AGENTS.md` (P0-1b/1c/7).

### MCP server (panopticon)

* When available, the `panopticon` MCP server reads a repository mirror at pushed refs and runs tests. Local uncommitted work is not visible. Mirror lag is up to 5 minutes; call `refresh()` after pushing. Server availability is not required on other hosts or cloud agents.
* Prefer `get_card`, `card_status`, and `latest_in_stack` for card Status, prerequisites, and stack position. Use `list_branches` for branch heads, `read_file` / `grep` with `ref` for pushed-branch inspection, `find_symbol` for C# declarations and signatures, and `find_diagrams` for Mermaid blocks. Read tools with a `ref` parameter default to `origin/main`; inspect the returned SHA and fetch time.
* `start_test_run(ref, filter)` runs `dotnet test src` at a pushed commit, with default filter `Category!=Interactive`. It returns a `run_id` immediately. One run is allowed host-wide; if rejected, use the reported active `run_id` or wait, never retry in a loop. Poll `get_test_run` every 30 to 60 seconds; a warm-cache full run takes about 4 minutes. `list_test_runs` lists recent runs.
* Failure classification compares TRX `testName` verbatim against `test/known-failures.json` at the tested ref. PR `Validation` sections cite the run id, totals, and `new_failures` (or none). Report `manifest_not_executed` as untested, not resolved. Status `error` with `test_host_crash` means truncated totals: rerun or report the error, never count it as a pass.
* `suspected_deviation` follows the determinism policy in `Validation`. Confirm `resolved_known` entries represent actual fixes before removing them from `test/known-failures.json` in the same PR. Adding manifest entries requires operator approval.
* TRX and logs live under `/home/rmessinger/dev/reidtv/data/panopticon-mcp/runs/<run_id>/` and are pruned automatically. Outside the host, quote `get_test_run` output instead of assuming artifact access.
* Local `dotnet test` remains the inner-loop tool; the MCP run is the pre-PR check for code changes.

### Environment

* A complete C&C Generals install lives at `/home/rmessinger/dev/Command and Conquer Generals/` (sibling of the Panopticon repo). The launcher is `Generals.exe` (capital G; Linux FS is case-sensitive). Set `CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals"` so `EnvironmentInstallationLocator` (env var `<IDENTIFIER>_PATH`, Generals identifier `cnc_generals`) finds it for P0 harness / TestHarness runs.
* Game assets ship as `.big` archives at the install root: `INI.big` (game data INI), `W3D.big` (3D models), `Textures.big`, `Terrain.big`, `maps.big`, `Audio.big` / `AudioEnglish.big`, `Speech.big` / `SpeechEnglish.big`, `Music.big`, `Window.big`, `shaders.big`, plus `Patch.big` / `PatchINI.big` overrides. A loose `Data/` tree holds cursors, movies, scripts. Panopticon reads `.big` files directly via `BigFileSystem`; no extraction step is required.
* Install probing requires `generals.exe` and `INI.big` at the root (`GeneralsDefinition.Probe`). `GameInstallation.CreateFileSystem()` builds a `CompositeFileSystem(DiskFileSystem(root), BigFileSystem(root))`, so loose disk files override archive entries of the same path. `SkudefReader` falls back to the literal `add-bigs-recurse .` directive for Generals (no `.skudef` shipped), recursing all `.big` files under the root in a fixed load order.
* All entry points resolve a `GameInstallation` then `new Game(installation, ...)`: the Launcher (`src/Panopticon.Launcher/Program.cs`), TestHarness (`src/Panopticon.Tools.TestHarness/Program.cs`, `--game-path` overrides `CNC_GENERALS_PATH`), ReplaySketch (`GeneralsInstallationService.TryGetInstallation`, cached per session), and `GameFixture` tests (`InstalledFilesTestData.GetInstallation`). Tests use the real install, not a shipped fixture; only small synthetic `.big` / `.map` / `.sav` samples exist under `test/Panopticon.Simulation.Tests/Data/.../Assets/` for isolated format tests.
* Interactive ReplaySketch tests (`Panopticon.Tools.ReplaySketch.Tests`: `OrderTypeCoverageTests`, `HarnessRunnerInteractiveTests`, `HandleResolutionInteractiveTests`) boot the real game via `GeneralsInstallationService.TryGetInstallation`, so they need the same `CNC_GENERALS_PATH`. Without it every locator returns empty (`EnvironmentInstallationLocator`, `RegistryInstallationLocator`, `SteamInstallationLocator`) and the test throws `Generals installation not found for Interactive test.` at the `TryGetInstallation` guard. Run them prefixed `CNC_GENERALS_PATH="..." dotnet test ...`; exclude them from routine non-interactive runs with `--filter "Category!=Interactive"`.

### Simulation domain model

* P3 `GameOutcome` has `OutcomeStatus` (InProgress/SuddenDeath/Decided) and `OutcomeReason` (Eliminated/Surrendered/ScoreDecision); mutual elimination leads to `SuddenDeath` continuation; time-limit leads to `ScoreDecision` with a configurable score metric.
