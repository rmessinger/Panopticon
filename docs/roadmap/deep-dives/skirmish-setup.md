# Deep Dive: Skirmish Setup & Player Relationships (P1)

> Expands phase **P1** of [Track P — Playable Baseline](../track-p-playable-baseline.md). Part of
> the [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it lands.

**Goal:** make starting a **single-player skirmish vs. ≥1 AI** work end-to-end without networking —
players created, factions/colors/teams assigned, start positions resolved, starting buildings/units
spawned, and — critically — **player relationships wired** (allies / enemies / neutrals). After P1, a
match *boots* with a human + AI players correctly related; behavior (AI) and outcome (victory) come in
P2/P3.

---

## 1. Why this is foundational

Everything else in Track P assumes a correctly-initialized set of related players: the AI (P2) needs to
know who its enemy is; victory/defeat (P3) needs teams/relationships to decide who lost; combat (P6)
needs relationship checks to allow attacks. Today relationships are an explicit `// TODO`, and the
single-player spawn path diverges from the multiplayer one, so a SP skirmish doesn't get starting
buildings or a camera the way an MP game does.

---

## 2. Current code (grounded)

### 2.1 Start path
- `SkirmishManager.StartGame()` builds `List<PlayerSetting>` from `Settings.Slots` and calls
  `Game.StartSkirmishOrMultiPlayerGame(...)` (`src/OpenSage.Game/Network/SkirmishManager.cs:55-93`).
- `LocalSkirmishManager.HandleStartButtonClickAsync()` only sets `Connection = new EchoConnection()`
  and flips status to `ReadyToStart` — no networking (`Network/SkirmishManager.cs:96-114`).
- `HostSkirmishManager`/`ClientSkirmishManager` hold the real network path; SP never enters them
  (`Network/SkirmishManager.cs:373-588`).
- `Game.StartSkirmishOrMultiPlayerGame()` wraps `StartGame(...)`; `StartSinglePlayerGame()` uses
  `new EchoConnection()` and `gameType: SinglePlayer` (`Game.cs:822-845`).
- `Game.StartGame()` loads the map, creates `Scene3D`, sets `NetworkMessageBuffer`, builds the
  control bar, resets timers, enables scripting (`Game.cs:717-802`).
- **Divergence:** starting buildings + camera-at-start are created only for
  `gameType != SinglePlayer`, for `Scene3D.Players[i + 2]` (`Game.cs:745-763`) — so a SP skirmish has
  **no equivalent local placement path**.
- `Game.LocalLogicTick()` pumps `SkirmishManager.Update()` until status is `Started`
  (`Game.cs:921-929`).

### 2.2 Menu entry points
- "Skirmish" button → `new LocalSkirmishManager(context.Game)` + opens `SkirmishGameOptionsMenu.wnd`
  (`src/OpenSage.Mods.Generals/Gui/MainMenuCallbacks.cs:127-130`).
- Lobby init: `SkirmishGameOptionsMenuCallbacks` → `GameOptionsUtil`
  (`Gui/SkirmishGameOptionsMenuCallbacks.cs:31-39`).
- Start button validates slots, fills missing start positions/factions/colors, then calls
  `SkirmishManager.HandleStartButtonClickAsync()` (`Gui/GameOptionsUtil.cs:239-305`); missing faction
  is randomized to a playable side (`:292-295`); faction combo from `Game.GetPlayableSides()` (`:68-75`).

### 2.3 Player model & relationships
- `PlayerManager.OnNewGame(...)` creates players from map players, picks `LocalPlayer`, then
  **TODOs relationships** (`src/OpenSage.Game/Logic/PlayerManager.cs:24-46`).
- `PlayerManager.CreatePlayers(...)` maps `mapPlayer.Allies/Enemies` into `player.Allies`/
  `player.Enemies` (`PlayerManager.cs:50-76`) — so map-defined relationships are read, but the
  skirmish-slot-driven setup is the gap.
- `Player` has `_playerToPlayerRelationships` + `_playerToTeamRelationships` (`Logic/Player.cs:33-35`),
  public `Allies`/`Enemies` sets (`:126-128`), a `Team` (`:139`),
  `GetRelationship(Team? that)` (team override → player override → neutral) (`:811-831`),
  `RelationshipType { Enemies=0, Neutral=1, Allies=2 }` (`:965-970`), `PlayerRelationships` overrides
  (`:972-994`), and `AddAlly()`/`AddEnemy()` that **only add to sets, no reciprocal wiring** (`:779-786`).
- `PlayerSetting` fields: `StartPosition`, `SideName`, `Color`, `Team`, `Owner`, `Name`,
  `IsLocalForMultiplayer` (`Logic/PlayerSettings.cs:17-36`).
- `PlayerTemplate` fields: `Side`, `PlayableSide`, `IsObserver`, `StartMoney`, `PreferredColor`,
  `DisplayName`, `StartingBuilding`, `StartingUnits`, … (`Logic/PlayerTemplate.cs:115-140`; INI parse
  `:58-113`).

### 2.4 Start positions & factions
- `Waypoints.TryGetPlayerStart(int playerIndex)` looks up `Player_{i}_Start`
  (`src/OpenSage.Game/Scripting/Waypoints.cs:76-81`); map load builds `Waypoints` from map objects
  (`Scene3D.cs:139-152`).
- `Scene3D.CreateSkirmishPlayerStartingBuilding(...)` uses `TryGetPlayerStart`, falls back to
  `(80,80,0)` + heightmap Z (`Scene3D.cs:430-438`); `player.Template.StartingBuilding` +
  `StartingUnits[0]` drive spawns (`Scene3D.cs:440-455`).
- Map start positions stored in `MapCache` `Player_1..8_Start` (`Content/MapCache.cs:29-38,66-73`);
  user maps in `UserMapCache` (`Data/Map/UserMapCache.cs:144-145,272-273`).
- Slot faction index → playable side name (`SkirmishManager.cs:76-82`); replay metadata maps
  `PlayerTemplate.Side` ↔ faction index (`Game.cs:225-279`).

---

## 3. Design

### 3.1 Relationship setup (the core fix)
Implement the `PlayerManager.OnNewGame()` TODO (`PlayerManager.cs:45-46`) so that, after players are
created from skirmish slots:

- Each player is assigned its **`Team`** from its slot (`PlayerSetting.Team`).
- Relationships are derived from teams: players on the **same team are allies**, different teams are
  **enemies**, and the neutral/civilian player is **neutral** to all. This drives
  `Player.GetRelationship(...)` (already team-aware at `Player.cs:811-831`).
- Wiring must be **reciprocal** — fix/augment `AddAlly()`/`AddEnemy()` (`Player.cs:779-786`) or set
  both sides in `PlayerManager`, so `A enemy B ⇒ B enemy A`.
- Reuse the existing map-relationship path (`CreatePlayers`, `PlayerManager.cs:50-76`) where the map
  defines relationships, but let **skirmish-slot teams take precedence** for slot-driven games.

### 3.2 Unified skirmish spawn path
Resolve the SP/MP divergence at `Game.cs:745-763`: a single-player skirmish must run the **same**
start-position lookup + `CreateSkirmishPlayerStartingBuilding` + camera-placement path the MP branch
uses, for **every** non-neutral player (human and AI). Decide the player-array indexing explicitly
(the `Players[i + 2]` offset and the `_players[2]` `LocalPlayer` fallback are ad hoc — see §5).

### 3.3 Faction / color / start-position fill
The lobby already fills missing factions/colors/start positions (`GameOptionsUtil.cs:239-305`); P1
must ensure the **AI slots** are filled the same way (faction from `GetPlayableSides()`, a free start
position, a free color) so an AI player is fully specified before `StartGame`.

---

## 4. Integration surface (what P1 changes)
- `Logic/PlayerManager.cs:24-46` — implement relationship + team setup after player creation.
- `Logic/Player.cs:779-786` (+ `:811-831,965-994`) — reciprocal ally/enemy wiring; confirm team→
  player default resolution.
- `Game.cs:717-802` (esp. `:745-763`) — SP skirmish uses the MP start-position/building/camera path.
- `Network/SkirmishManager.cs:55-114` — slot→`PlayerSetting` mapping for AI + human in the local path.
- `Gui/GameOptionsUtil.cs:239-305` — fill missing faction/color/start for AI slots.
- `Scene3D.cs:430-455` + `Scripting/Waypoints.cs:76-81` + `Content/MapCache.cs:29-38` — start-position
  lookup/spawn (already largely present; verify under SP).

## 5. Open design questions (↗)
- **`LocalPlayer` selection** for games that aren't human-only is ad hoc (`_players[2]` fallback,
  `PlayerManager.cs:39-46`). Define how the human player is identified for a SP skirmish.
- **Spawn ownership** — should AI-vs-human skirmish placement live in the SP path or the (renamed)
  shared skirmish path? (`Game.cs:745-763`). Pick one owner to avoid the current SP/MP split.

## 6. Sub-phases

| Phase | Goal | Status |
|---|---|---|
| **P1-1. Team & relationship setup** | Implement `OnNewGame()` relationships: assign teams from slots, derive ally/enemy/neutral, wire reciprocally. | Not started |
| **P1-2. Unified skirmish spawn** | One start-position + starting-building + camera path for SP and MP; resolve player-array indexing and `LocalPlayer`. | Not started |
| **P1-3. AI slot fill** | Ensure AI slots get faction/color/start filled before `StartGame` (lobby + local path). | Not started |
| **P1-4. Boot validation** | A human + ≥1 AI skirmish boots headlessly with correct relationships and spawns (assert via [P0](playable-baseline-harness.md)). | Not started |

## 7. Sequencing
- P1 has **no Track-P prerequisite** (entry point alongside P0) but **gates P2/P3/P4/P5/P6** — they all
  assume correctly-related, spawned players.
- P1-4's boot check rides on the [P0 harness](playable-baseline-harness.md) once P0-1/P0-5 exist.

## 8. Related
- Track overview: [`../track-p-playable-baseline.md`](../track-p-playable-baseline.md)
- AI that consumes relationships: [`skirmish-ai.md`](skirmish-ai.md) (P2)
- Outcome that consumes teams: [`victory-defeat.md`](victory-defeat.md) (P3)
- Harness for the boot check: [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
