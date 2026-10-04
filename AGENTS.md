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

### Development model (standard since C4)

* Track C (and later tracks) ship through phase staging branches (`staging/track-cX`): cards are implemented in dependency order, grouped onto shared branches (per-card commits, card Status flips, `.exchange` lines), each group = one stacked PR into the staging branch. Every PR (group PRs into staging and the final staging-to-main PR) is left open for the user to merge; agents never merge PRs themselves. Staging PR branches do not trigger CI (workflows run on push to `main` only); one final staging-to-main PR runs CI once per phase.
* PR descriptions use atomic bullets, not prose paragraphs. Format: a one- or two-sentence top-level summary first; then one top-level bullet per card or change area, each fact as an indented sub-bullet under it; a final `Validation` top-level bullet with indented result entries. One fact per bullet; split semicolon chains and multi-sentence blobs into separate sub-bullets. Total verbosity stays roughly the same as paragraph text.

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

### Validation

* Code after a closing brace starts on the next line (another closing brace or punctuation may stay adjacent); property patterns (`is { } x`) are exempt. Enforced by `scripts/check_brace_newline.py` in CI alongside `dotnet format` (see `docs/coding-style.md` rule 18).
* Argument validation is one line: BCL `ThrowIf*` without a message, `Panopticon.Ensure` with one; conditions neither helper expresses keep a braced `if` throw (see `docs/coding-style.md` rule 19). CA1510/CA1511/CA1512 warnings enforce the helper forms.
* Screenshot capture and sharing recipes (3D headless captures, windowed runs under Xvfb + llvmpipe, synthetic hover for tooltips, the shared viewer) live in `docs/screenshots.md`. Amend that file when a new capability or limitation is found instead of re-deriving the method.
* Always use ReplaySketch screenshot mode to verify any ReplaySketch fix or feature. Run: `DISPLAY=:99 [LIBGL_ALWAYS_SOFTWARE=1] REPLAYSKETCH_SCREENSHOT_PATH=/tmp/shot.png [REPLAYSKETCH_SCREENSHOT_TAB=map|model] dotnet run --project src/Panopticon.Tools.ReplaySketch.Avalonia` (X11 display required; use Xvfb on headless hosts).
* To share a screenshot with the user, write it into `/mnt/pit2/Network Storage/temp/` (path contains spaces, quote it) instead of `/tmp`, e.g. `REPLAYSKETCH_SCREENSHOT_PATH="/mnt/pit2/Network Storage/temp/shot.png"`. That directory is served read-only by the live viewer at `https://files.reid-corp.com/viewer/`, which shows the newest image automatically; no manual refresh, upload, or login needed. Overwrite the same file for iteration snapshots so the viewer swaps instantly. Top level holds meaningful progress demonstrations only (stills or GIFs; animations as GIFs); granular per-frame PNGs go in `frames/<topic>/`.
* Always verify ReplaySketch fixes without breaking the map panel.
* Determinism-enforcing tests (same-seed equality: `SeededRunTests`; golden-baseline byte-identity: `UnitValidationGround` baseline + `BaselineGoldenFrames_*`; run-output/replay byte-identity) are treated as suspected deviations, not automatic regressions. On failure: stop, preserve the artifacts (run outputs, baselines, diffs), report the failing assertion plus the most likely cause, and wait for the user's determination before fixing or declaring the run failed. Standard unit-test failures follow the normal rules.

### Tooling

* P6b validates unit archetypes via the P0 harness: 6 archetypes (infantry/vehicle/aircraft/builder/support/superweapon), full contract (produce/move/attack/all weapon modes/all special abilities/die), USA reference units, unimplemented handlers surface as explicit failures.
* ReplaySketch validation: logic stays in `.Core` (tested headlessly by `ReplaySketch.Tests` + P0-5 run-and-assert); UI lives in the Avalonia front-end and stays a thin shell behind injectable seams; agents follow `ReplaySketch.Avalonia/AGENTS.md` (P0-1b/1c/7).

### Environment

* A complete C&C Generals install lives at `/home/rmessinger/dev/Command and Conquer Generals/` (sibling of the Panopticon repo). The launcher is `Generals.exe` (capital G; Linux FS is case-sensitive). Set `CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals"` so `EnvironmentInstallationLocator` (env var `<IDENTIFIER>_PATH`, Generals identifier `cnc_generals`) finds it for P0 harness / TestHarness runs.
* Game assets ship as `.big` archives at the install root: `INI.big` (game data INI), `W3D.big` (3D models), `Textures.big`, `Terrain.big`, `maps.big`, `Audio.big` / `AudioEnglish.big`, `Speech.big` / `SpeechEnglish.big`, `Music.big`, `Window.big`, `shaders.big`, plus `Patch.big` / `PatchINI.big` overrides. A loose `Data/` tree holds cursors, movies, scripts. Panopticon reads `.big` files directly via `BigFileSystem`; no extraction step is required.
* Install probing requires `generals.exe` and `INI.big` at the root (`GeneralsDefinition.Probe`). `GameInstallation.CreateFileSystem()` builds a `CompositeFileSystem(DiskFileSystem(root), BigFileSystem(root))`, so loose disk files override archive entries of the same path. `SkudefReader` falls back to the literal `add-bigs-recurse .` directive for Generals (no `.skudef` shipped), recursing all `.big` files under the root in a fixed load order.
* All entry points resolve a `GameInstallation` then `new Game(installation, ...)`: the Launcher (`src/Panopticon.Launcher/Program.cs`), TestHarness (`src/Panopticon.Tools.TestHarness/Program.cs`, `--game-path` overrides `CNC_GENERALS_PATH`), ReplaySketch (`GeneralsInstallationService.TryGetInstallation`, cached per session), and `GameFixture` tests (`InstalledFilesTestData.GetInstallation`). Tests use the real install, not a shipped fixture; only small synthetic `.big` / `.map` / `.sav` samples exist under `test/Panopticon.Simulation.Tests/Data/.../Assets/` for isolated format tests.
* Interactive ReplaySketch tests (`Panopticon.Tools.ReplaySketch.Tests`: `OrderTypeCoverageTests`, `HarnessRunnerInteractiveTests`, `HandleResolutionInteractiveTests`) boot the real game via `GeneralsInstallationService.TryGetInstallation`, so they need the same `CNC_GENERALS_PATH`. Without it every locator returns empty (`EnvironmentInstallationLocator`, `RegistryInstallationLocator`, `SteamInstallationLocator`) and the test throws `Generals installation not found for Interactive test.` at the `TryGetInstallation` guard. Run them prefixed `CNC_GENERALS_PATH="..." dotnet test ...`; exclude them from routine non-interactive runs with `--filter "Category!=Interactive"`.

### Simulation domain model

* P3 `GameOutcome` has `OutcomeStatus` (InProgress/SuddenDeath/Decided) and `OutcomeReason` (Eliminated/Surrendered/ScoreDecision); mutual elimination leads to `SuddenDeath` continuation; time-limit leads to `ScoreDecision` with a configurable score metric.
