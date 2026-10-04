# Panopticon Copilot Instructions

Panopticon is a C# (.NET 8) simulation framework built on the SAGE game engine (Command & Conquer: Generals, Battle for Middle-earth, etc.). See `docs/roadmap/README.md` for the project vision and roadmap.

## Response Style

Keep responses concise: lead with the outcome, skip step-by-step narration of routine work, and don't restate the plan or the user's request back to them. Reserve longer explanations for genuinely complex or ambiguous decisions.

## Build, Test & Format

```sh
# Build
dotnet build src

# Run all non-interactive tests (no game install required for mocked tests)
dotnet test src --filter Category!=Interactive

# Run a single test by name
dotnet test src --filter "FullyQualifiedName~MyTestName"

# Check formatting (enforced by CI)
dotnet format src --verify-no-changes

# Fix formatting
dotnet format src

# Run the game (requires game files)
# Set env vars: CNC_GENERALS_PATH, CNC_GENERALS_ZH_PATH, BFME_PATH, BFME2_PATH, BFME2_ROTWK_PATH
dotnet run --project src/Panopticon.Launcher
```

Tests that need actual game files installed are annotated with `[GameFact(SageGame.X)]`. These are skipped in CI. Tests without game files extend `MockedGameTest`.

"Skipped in CI" means CI runners have no game install (or GPU), not that these tests are unrunnable locally. Before assuming an install- or GPU-dependent test cannot run in the current environment, check: a game install via its env var (e.g. `CNC_GENERALS_PATH`) or `GameInstallation.FindAll`; a usable graphics device via `vulkaninfo --summary` (or D3D11 on Windows / Metal on macOS); and a non-snap .NET via `which dotnet`. Dev and agent environments frequently have all three, in which case `[GameFact]` tests, the TestHarness (windowed and `--headless`), and the Unit Validation Ground run normally.

CI (`CI`, `Debug Build`, `Format`) only runs on pushes to `main`, not on branches or pull requests, to conserve Actions usage. Before pushing any commit, run the build, the non-interactive test suite, and the format check locally (the three commands above) and fix any failures — this is the only verification those changes get before landing on `main`.

## Architecture

### Project Layout

| Project | Purpose |
|---|---|
| `Panopticon.Simulation` | Core engine: game loop, asset management, game logic, scripting, GUI |
| `Panopticon.Rendering` | Veldrid-based graphics abstraction (D3D11, OpenGL, Metal, Vulkan) |
| `Panopticon.FileFormats.*` | Parsers for `.big`, `.w3d`, `.ini`, `.map`, `.wnd`, `.apt`, etc. |
| `Panopticon.Mathematics` | Math types and utilities |
| `Panopticon.IO` | Virtual file system over `.big` archives |
| `Panopticon.Mods.*` | Per-game definitions (Generals, ZeroHour, BFME, BFME2, BuiltIn) |
| `Panopticon.Launcher` | Entry point / launcher |
| `Panopticon.Tools.ReplaySketch.*` | Primary scenario-scripting tool: author and replay ordered command sequences against the engine |
| `Panopticon.Tools.TestHarness` | Windowed visual-validation tool: renders a scripted scenario against a real game installation |
| `Panopticon.Tools.WorldBuilder.*` | Map/scenario editor |
| `Panopticon.Tools.BigEditor` | Inspector for `.big` archive files |
| `Panopticon.Tools.Sav2Json` | Converts `.sav` save files to JSON |
| `Panopticon.Simulation.Tests` | Unit and integration tests (xUnit) |

### Key Patterns

#### IGameDefinition / SageGame

Each supported game implements `IGameDefinition` in `Panopticon.Mods.*`. It specifies registry keys for auto-detection, Steam install info, asset load strategies, GUI source, and scripting tick rate. The `SageGame` enum is the canonical game identifier used throughout the codebase (e.g., `SageGame.CncGenerals`). `[AddedIn(SageGame.X)]` attributes mark fields/properties introduced in specific games beyond Generals.

#### AssetStore

`AssetStore` is the central asset registry. Assets are stored as `ScopedSingleAsset<T>` (singletons like `GameData`, `AudioSettings`) or `ScopedAssetCollection<T>` (named collections like `ObjectDefinition`). Scopes are pushed/popped to support base-game + mod layering.

#### INI Parsing

Game data is read from `.ini` files via `IniParser` + `IniParseTable<T>`. Each parseable type provides a static `IniParseTable<T>` mapping INI field name strings to delegate callbacks. Block parsers (`IniParser.BlockParsers.cs`) handle nested blocks.

#### Module System (GameObject)

`ObjectDefinition` uses a module-based composition pattern. Each module (Draw, Body, Update, Behavior, etc.) derives from `ModuleData` / `BehaviorModule`. `ModuleKinds` flags and `ModuleInheritanceMode` control how modules are inherited from base object definitions. New modules must be registered in the module parse tables.

#### GameSystem

Engine subsystems extend `GameSystem` and self-register into `IGame.GameSystems` on construction. They receive `OnSceneChanging()` / `OnSceneChanged()` lifecycle calls. The `Subsystem` enum describes logical load groups (Core, Audio, Terrain, etc.).

#### StatePersister (Save/Load)

Save and load use a single abstract `StatePersister` base class. The same `Persist*` method calls handle both reading and writing depending on `StatePersistMode`. `PersistVersion()` must be called first in each `Persist` method to handle versioned save data.

#### File System

Game files may be loose on disk or inside `.big` archives. `Panopticon.IO` provides a unified `FileSystem`/`FileSystemEntry` abstraction over both.

## Coding Style

Follows the [dotnet/corefx style](docs/coding-style.md):

- Allman braces (each brace on its own line)
- 4 spaces, no tabs
- Private/internal fields: `_camelCase`; static: `s_camelCase`; thread-static: `t_camelCase`
- Public fields: `PascalCase` (no prefix); used sparingly
- Always specify visibility (`private`, `public`, etc.) — visibility is the first modifier
- `System.*` using directives first, then others alphabetically
- `var` only when the type is unambiguous from the right-hand side
- `nameof(...)` over string literals wherever possible
- No `this.` unless required for disambiguation
- Comment only what isn't obvious from the code; don't add comments that just restate what a line does
- `.editorconfig` at repo root enforces formatting; run `dotnet format src` before committing

## Testing and Visual Validation

`Panopticon.Tools.ReplaySketch` is the primary tool for scripting and replaying ordered command sequences against the engine. Prefer it for testing game logic over one-off harnesses.

`Panopticon.Tools.TestHarness` is the primary tool for visual validation of the renderer. It opens a real game window (Metal on macOS, Direct3D 11 on Windows, Vulkan on Linux) and requires a game installation.

```sh
dotnet run --project src/Panopticon.Tools.TestHarness
```

Configuration is in `TestHarness.json` (output directory). Set `GamePath` if auto-detection fails.

To capture frames for automated visual validation, use `FrameCapturer` in `Panopticon.Simulation`, or run the headless test `DefaultScenario_CapturesFrames_ForVisualValidation`. Captured frames land in `$TMPDIR/panopticon_frames/<test_name>_<timestamp>/` alongside a `validation_guide.md` listing per-frame checks. See `src/Panopticon.Tools.TestHarness/AGENTS.md` for the full agent validation workflow, common failure modes, and shader subsystem triage hints.

## Roadmap and Agent Task Cards

The long-term plan lives under `docs/roadmap/`. `docs/roadmap/README.md` is the index; per-track docs are `track-p-*.md`, `track-a-*.md`, `track-b-*.md`, and `track-c-*.md`. When changing the plan, follow `docs/roadmap/agent/MAINTAINING.md` to propagate changes across all parallel docs. Documentation generally (including the roadmap) follows the concise style in `CONTRIBUTING.md`.

For incremental, low-effort implementation work, use the agent task-card system in `docs/roadmap/agent/`:

- Cards live in `docs/roadmap/agent/cards/` named `<PHASE>-<NN>-<slug>.md`.
- Each card is self-contained: exact files to read, ordered steps, acceptance criteria, and validation commands.
- Pick a card with Status `Ready`, execute it, then mark it `Done` or `Blocked` and append a result entry to `.exchange`.
- The template for new cards is `docs/roadmap/agent/task-card-template.md`.

If a phase has no ready cards but needs advancing, decompose it into new cards using the template rather than executing the phase open-endedly.

## Agent Communication

Agents may use the file `.exchange` in the repository root to communicate with each other across sessions and concurrently. This file is excluded from version control (`.gitignore`). Use it to share state, pass results, or coordinate work between parallel or sequential agent sessions.

## PR Guidelines

- All changes via PR; each commit must build
- PR descriptions use atomic bullets, not prose paragraphs: one fact per bullet; one top-level bullet per card or change area; a short Validation list at the end
- Split semicolon chains and multi-sentence blobs into separate bullets or nested sub-lists (total verbosity stays roughly the same as paragraph text)
- "Rebase and Merge" is the default merge strategy
- Do not mix unrelated changes in one PR
- Address review feedback in new commits; squash only when requested
