# OpenSAGE Copilot Instructions

OpenSAGE is a C# (.NET 8) open-source reimplementation of the SAGE game engine (Command & Conquer: Generals, Battle for Middle-earth, etc.). It reads original game data files but contains no game assets.

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
dotnet run --project src/OpenSage.Launcher
```

Tests that need actual game files installed are annotated with `[GameFact(SageGame.X)]`. These are skipped in CI. Tests without game files extend `MockedGameTest`.

## Architecture

### Project Layout

| Project | Purpose |
|---|---|
| `OpenSage.Game` | Core engine: game loop, asset management, game logic, scripting, GUI |
| `OpenSage.Rendering` | Veldrid-based graphics abstraction (D3D11, OpenGL, Metal, Vulkan) |
| `OpenSage.FileFormats.*` | Parsers for `.big`, `.w3d`, `.ini`, `.map`, `.wnd`, `.apt`, etc. |
| `OpenSage.Mathematics` | Math types and utilities |
| `OpenSage.IO` | Virtual file system over `.big` archives |
| `OpenSage.Mods.*` | Per-game definitions (Generals, ZeroHour, BFME, BFME2, BuiltIn) |
| `OpenSage.Launcher` | Entry point / launcher |
| `OpenSage.Tools.ReplaySketch.*` | Primary scenario-scripting tool: author and replay ordered command sequences against the engine |
| `OpenSage.Tools.TestHarness` | Windowed visual-validation tool: renders a scripted scenario against a real game installation |
| `OpenSage.Tools.WorldBuilder.*` | Map/scenario editor |
| `OpenSage.Tools.BigEditor` | Inspector for `.big` archive files |
| `OpenSage.Tools.Sav2Json` | Converts `.sav` save files to JSON |
| `OpenSage.Game.Tests` | Unit and integration tests (xUnit) |

### Key Patterns

#### IGameDefinition / SageGame

Each supported game implements `IGameDefinition` in `OpenSage.Mods.*`. It specifies registry keys for auto-detection, Steam install info, asset load strategies, GUI source, and scripting tick rate. The `SageGame` enum is the canonical game identifier used throughout the codebase (e.g., `SageGame.CncGenerals`). `[AddedIn(SageGame.X)]` attributes mark fields/properties introduced in specific games beyond Generals.

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

Game files may be loose on disk or inside `.big` archives. `OpenSage.IO` provides a unified `FileSystem`/`FileSystemEntry` abstraction over both.

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

`OpenSage.Tools.ReplaySketch` is the primary tool for scripting and replaying ordered command sequences against the engine. Prefer it for testing game logic over one-off harnesses.

`OpenSage.Tools.TestHarness` is the primary tool for visual validation of the renderer. It opens a real game window (Metal on macOS, Direct3D 11 on Windows, Vulkan on Linux) and requires a game installation.

```sh
dotnet run --project src/OpenSage.Tools.TestHarness
```

Configuration is in `TestHarness.json` (output directory). Set `GamePath` if auto-detection fails.

To capture frames for automated visual validation, use `FrameCapturer` in `OpenSage.Game`, or run the headless test `DefaultScenario_CapturesFrames_ForVisualValidation`. Captured frames land in `$TMPDIR/opensage_frames/<test_name>_<timestamp>/` alongside a `validation_guide.md` listing per-frame checks. See `src/OpenSage.Tools.TestHarness/AGENTS.md` for the full agent validation workflow, common failure modes, and shader subsystem triage hints.

## Roadmap and Agent Task Cards

The long-term plan lives under `docs/roadmap/`. `docs/roadmap/README.md` is the index; per-track docs are `track-p-*.md`, `track-a-*.md`, `track-b-*.md`, and `track-c-*.md`. When changing the plan, follow `docs/roadmap/MAINTAINING.md` to propagate changes across all parallel docs. Documentation generally (including the roadmap) follows the concise style in `CONTRIBUTING.md`.

For incremental, low-effort implementation work, use the agent task-card system in `docs/roadmap/agent-guide/`:

- Cards live in `docs/roadmap/agent-guide/cards/` named `<PHASE>-<NN>-<slug>.md`.
- Each card is self-contained: exact files to read, ordered steps, acceptance criteria, and validation commands.
- Pick a card with Status `Ready`, execute it, then mark it `Done` or `Blocked` and append a result entry to `.exchange`.
- The template for new cards is `docs/roadmap/agent-guide/task-card-template.md`.

If a phase has no ready cards but needs advancing, decompose it into new cards using the template rather than executing the phase open-endedly.

## Agent Communication

Agents may use the file `.exchange` in the repository root to communicate with each other across sessions and concurrently. This file is excluded from version control (`.gitignore`). Use it to share state, pass results, or coordinate work between parallel or sequential agent sessions.

## PR Guidelines

- All changes via PR; each commit must build
- "Rebase and Merge" is the default merge strategy
- Do not mix unrelated changes in one PR
- Address review feedback in new commits; squash only when requested
