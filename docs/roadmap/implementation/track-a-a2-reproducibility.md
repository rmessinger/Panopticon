# Track A · A2: Statistical validation

> Audience: human overview above the divider; agent-context detail below it.

Implements the A2 row of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a2-reproducibility.md](../deep-dives/a2-reproducibility.md), including its
[architecture diagrams](../deep-dives/a2-reproducibility.md#5-architecture-diagrams).

## Scope

Document the completed seed-control work and execution-variation audits. Seed selection is an
experiment control, not a reproducibility guarantee. The multi-sample metrics and per-run invariant
harness is `WI-A2-04`, out of scope here.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| audit | [`WI-A2-01`](../agent/cards/WI-A2-01-audit-randomness-sources.md) | Randomness source audit (below). |
| audit | [`WI-A2-02`](../agent/cards/WI-A2-02-audit-nonrandom-nondeterminism.md) | Non-random nondeterminism audit (below). |
| design | A2 deep dive | Seed-control design, statistical validation model, conflict-rule scope. |
| convert | [`WI-A2-03`](../agent/cards/WI-A2-03-route-randomness-through-seeded-source.md) | Seed `GameLogic.Random` from the run seed; convert the uncontrolled simulation-affecting sites. |
| harness | [`WI-A2-04`](../agent/cards/WI-A2-04-stability-verification-harness.md) | Multi-sample statistical harness. |

---

## Agent context

## Randomness source audit

Every `new Random(`, `Random.Shared`, `IRandom`, and `CreateRandom` site in `src/`, classified per
[`WI-A2-01`](../agent/cards/WI-A2-01-audit-randomness-sources.md).

### Engine (`src/Panopticon.Simulation`)

| Site | Classification | Simulation-affecting | Notes |
|---|---|---|---|
| `Scripting/LuaScriptEngine.cs:476` `new Random()` in `GetRandomNumber()` | uncontrolled | yes | Scripting path; comment in code already flags it for multiplayer sync. Converted by `WI-A2-03`. |
| `Game.cs:209` `new Random()` in `ParseReplayMetaToPlayerSettings` | uncontrolled | yes (initial conditions) | Random faction for replay slots recorded as random. Converted by `WI-A2-03` to the replay's recorded seed. |
| `Network/SkirmishGameSettings.cs:85` `Seed = Random.Shared.Next()` | seed origin | yes (is the seed) | Entropy source when the host does not pick a seed; keep. |
| `Game.cs:876` `seed: Environment.TickCount` for `StartSinglePlayerGame` | seed origin | yes (is the seed) | Entropy source for single player; keep. |
| `Network/SkirmishSlotDefaults.cs:33` `new Random(seed)` | seeded (setup) | yes (initial conditions) | Already seeded from `SkirmishGameSettings.Seed`. |
| `Logic/GameLogic.cs:64,71` `Random = game.CreateRandom()` | seeded-logic | yes | The seeded source `WI-A2-03` routes draws to. Finding: it is constructed as `SystemRandom` (seed 0) and is never `Initialize`d with the run seed today; the run seed only reaches replay metadata (`Game.StartGame`). `WI-A2-03` threads the seed through. |
| `Client/GameClient.cs:19,26` `Random = game.CreateRandom()` | seeded-client | no | Client-side visuals only (`SwayClientUpdate`); presentation. |
| `Graphics/ParticleSystems/ParticleSystemUtility.cs:9` static `Random(1000)` | uncontrolled (constant seed) | no | Callers are entirely within `Graphics/ParticleSystems` (particle emission visuals). Presentation-only; left alone. |
| `Audio/AudioSystem.cs:62` `new Random()` | uncontrolled | no | Audio variation. Presentation-only; left alone. |
| `Gui/Apt/ActionScript/Opcodes/Basic.cs:208` `new Random()` | uncontrolled | no | Apt UI action scripting. Presentation-only. |
| `Diagnostics/MainView.cs:177` `new Random()` | uncontrolled | no | Developer diagnostics. |
| `Input/InputLogger.cs` | n/a (no Random) | no | Logging timestamps only. |

Simulation-affecting `uncontrolled` count: 2 (both converted by `WI-A2-03`).

### Tools and tests

| Site | Classification | Simulation-affecting | Notes |
|---|---|---|---|
| `Tools/ReplaySketch.Core/HarnessRunner.cs:80` `new Random()` (`dispatchRng`) | uncontrolled (tool-side) | yes (drives order dispatch in harness runs) | Converted by `WI-A2-03` to the scenario seed. |
| `Tools/ReplaySketch.Core/HarnessRunner.cs:202` `new Random(0)` | seeded | no | Layout helper. |
| `Tools/ReplaySketch.Core/Services/TerrainValidator.cs:44` `new Random(0)` | seeded | no | Deterministic validation sampling. |
| `Tools/ReplaySketch.Core/Services/ReplayExporter.cs:29` `new Random()` | uncontrolled (tool-side) | no | Export bookkeeping. |
| `Tools/ReplaySketch.Core/Services/ReplayExporter.cs:131` `Random.Shared.Next()` | seed origin (tool-side) | yes (chooses the exported run's seed) | Entropy source for exported replays; keep. |
| `Benchmarks/*`, `*.Tests/*` | various | no | Benchmarks and tests are not simulation runs. |

### Seed origin and persistence

Origin: `SkirmishGameSettings.Seed` defaults to `Random.Shared.Next()`; the host UI can set it;
`SkirmishManager` passes it to `Game.StartSkirmishOrMultiPlayerGame`. Persistence: replay files
record it in `ReplayMetadata` (`SD`) and `ReplayHeader`; save files do not persist the seed or the
generator state (`GameLogic.Persist` does not touch `Random`). The seed reaches no consumer today:
`StartGame` uses it only to build replay metadata, so `GameLogic.Random` runs at its constructed
default regardless of the seed.

## Non-random nondeterminism audit

Logic path = code reachable from `GameLogic.Update`, `Scene3D.LogicTick`, or scripting
(`Game.Update`'s logic and scripting branches; see the
[A1 tick-path audit](track-a-a1-headless-host.md#tick-path-presentation-audit)).

| Site | Category | Reachable from tick | Affects simulation | Notes |
|---|---|---|---|---|
| `Core/DeltaTimer.cs` (Stopwatch) | wall-clock | yes (via `LocalLogicTick`) | yes | Drives `MapTime`; by design. `FixedDelta` (P9) and the A1-4 host's fixed map time are the deterministic paths. |
| `Game.cs:876` `Environment.TickCount` | wall-clock | no (setup) | yes (seed origin) | Single-player seed entropy; keep. |
| `Network/LobbyManager.cs:97,125` `DateTime.Now` | wall-clock | no | no | Lobby keepalive. |
| `Network/ClientInstance.cs:7` `Guid.NewGuid()` | environment | no | no | Network client identity. |
| `Input/InputLogger.cs` `DateTime.Now` | wall-clock | no | no | Logging. |
| `Data/Rep/ReplayHeader.cs:47-51` `DateTime.UtcNow/Now` | wall-clock | no | no | Replay metadata; already excluded from parity diffs by the results-file header format. |
| `Diagnostics/GameTrace.cs:52` `Stopwatch` | wall-clock | no (diagnostics) | no | Tracing. |
| `Diagnostics/MainView.cs:190` `Environment.TickCount` | wall-clock | no | no | Developer UI. |
| `Network/SkirmishManager.cs`, `Network/NetworkConnection.cs`, `Network/UPnP.cs`, `Network/AsyncExtensions.cs` | concurrency (`async`, sockets) | no (lobby/pre-game) | no | All async work completes before `StartGame`; the tick path is single-threaded. |
| `Logic/GameLogic.cs` object iteration | ordering | yes | yes | Iterates `_objects` (a `List` in id order) and `_sleepyUpdates` (priority list with deterministic rebalancing); dictionary fields (`_nameLookup`, `_techTreeOverrides`, `_structuresBeingSold`) are only read by key lookup, never iterated in a state-affecting way. No unordered iteration found on the tick path. |

Simulation-affecting entries ranked by blast radius:

1. `DeltaTimer` wall-clock `MapTime` under normal windowed play (mitigated: `FixedDelta` for
   measurement, host-fixed map time for headless; a run's frame count is a function of wall-clock
   otherwise).
2. `Game.cs:876` single-player seed entropy (changes the random stream per run by design).
3. `LuaScriptEngine.GetRandomNumber` uncontrolled `Random` (converted by `WI-A2-03`).

Candidates needing concurrent-write conflict rules (listed, not decided; the rules are a deep-dive
task): none today, because no concurrent writer is reachable from the tick path. B3 parallelism
and B6 sharding will introduce them; track in those phases.
