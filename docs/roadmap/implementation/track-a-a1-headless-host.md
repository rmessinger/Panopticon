# Track A · A1: Headless simulation host

> Audience: human overview above the divider; agent-context detail below it.

Implements the A1 row of [Track A](../track-a-foundations.md). Design source of truth:
[deep-dives/a1-headless-host.md](../deep-dives/a1-headless-host.md), including its
[architecture diagrams](../deep-dives/a1-headless-host.md#5-architecture-diagrams).

## Scope

Run the logic core with no renderer, audio, or window: audit the graphics and tick-path
couplings (A1-1), make asset loading data-only (A1-2), extract the fixed-rate logic step from
`Game.Update` (A1-3), introduce the domain-agnostic `SimulationHost` (A1-4), and emit the T0 run
output (A1-5, metrics/events tier only; T1/T2 use A5-4 bucketing).

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| A1-1 | [`WI-A1-01`](../agent/cards/WI-A1-01-audit-graphics-device-couplings.md) | GraphicsDevice coupling audit (below). |
| A1-1 | [`WI-A1-02`](../agent/cards/WI-A1-02-audit-tick-presentation-couplings.md) | Tick-path presentation audit (below). |
| A1-1 | [`WI-A1-03`](../agent/cards/WI-A1-03-populate-architecture-diagrams.md) | Grounded §5 diagrams in the deep dive. |
| A1-2 | [`WI-A1-04`](../agent/cards/WI-A1-04-data-only-asset-loading.md) | `AssetStore`/`AssetLoadContext` accept null graphics; INI data resolves without a device. |
| A1-3 | [`WI-A1-05`](../agent/cards/WI-A1-05-extract-logic-stepper.md) | `LogicStepper` extracted from `Game.Update`; presentation stays in `Game`. |
| A1-4 | [`WI-A1-06`](../agent/cards/WI-A1-06-simulation-host.md) | `SimulationHost`: FTRT run loop, order injection, per-tick hook. |
| A1-5 | [`WI-A1-07`](../agent/cards/WI-A1-07-run-outputs-metrics-stream.md) | T0 run output: JSON with seed, frames, terminal `GameOutcome`, bounded metric series. |
| A1-5 | [`WI-A1-08`](../agent/cards/WI-A1-08-testharness-simulation-host.md) | TestHarness headless + P0 `HarnessRunner` onto `SimulationHost`. |
| A1-5 | [`WI-A1-09`](../agent/cards/WI-A1-09-t1-signature-tree-recording.md) | T1 signature trees. |
| A1-5 | [`WI-A1-10`](../agent/cards/WI-A1-10-t2-triggered-detail-capture.md) | T2 triggered detail capture. |
| A1-5 | [`WI-A1-11`](../agent/cards/WI-A1-11-state-snapshot-delta-recording.md) | Snapshot and delta recording. |

---

## Agent context

## GraphicsDevice coupling audit

Every `GraphicsDevice` / `GraphicsLoadContext` / `ShaderSetStore` / `ShaderResourceManager` /
`StandardGraphicsResources` reference in `src/Panopticon.Simulation`, classified per
[`WI-A1-01`](../agent/cards/WI-A1-01-audit-graphics-device-couplings.md). Classification key:
`logic` (reached from `GameLogic.Update` / `Scene3D.LogicTick` / scripting), `asset-data`
(reached while parsing INI, map, or object-definition data), `presentation` (reached only from
render, UI, diagnostics, or audio paths).

### Construction path (H1, H2)

| Site | Owning type | Classification | Minimal surface used |
|---|---|---|---|
| `Game.cs:517` `GraphicsDeviceUtility.CreateGraphicsDevice` | `Game` ctor | presentation (unconditional construction; H1) | whole device |
| `Game.cs:565` `new StandardGraphicsResources(GraphicsDevice)` | `Game` ctor | presentation | white/placeholder textures, samplers, linear depth texture |
| `Game.cs:566` `new ShaderSetStore(...)` | `Game` ctor | presentation | pipeline factory |
| `Game.cs:567` `new ShaderResourceManager(...)` | `Game` ctor | presentation | per-material resource sets |
| `Game.cs:568` `new GraphicsLoadContext(...)` | `Game` ctor | presentation container | holds the four graphics objects |
| `Game.cs:570-578` `new AssetStore(..., GraphicsDevice, StandardGraphicsResources, ShaderResourceManager, ShaderSetStore, ...)` | `Game` ctor | asset-data (parameters) | forwarded only |
| `Content/AssetStore.cs:174-191` ctor graphics parameters | `AssetStore` | asset-data | forwarded into `AssetLoadContext` |
| `Content/Loaders/AssetLoadContext.cs:13-16` | `AssetLoadContext` | asset-data | holder fields; read by the on-demand loaders below |
| `Content/GraphicsLoadContext.cs` | `GraphicsLoadContext` | presentation | same four fields, game-level |
| `Content/ContentManager.cs:19,39,48` `GraphicsDevice` property | `ContentManager` | asset-data | read once: `Scene3D.cs:163` `WaitForIdle()` |
| `GameWindow.cs:110-113` `CreateGraphicsDevice` | `GameWindow` | presentation | swapchain device |

### Loaders and map-load path

| Site | Owning type | Classification | Minimal surface used |
|---|---|---|---|
| `Content/Loaders/OnDemandTextureLoader.cs:62-158` | texture on-demand loader | asset-data | `ResourceFactory.CreateTexture`, `GraphicsDevice.UpdateTexture`, staging `Map` |
| `Content/Loaders/OnDemandGuiTextureLoader.cs:74-135` | GUI texture on-demand loader | asset-data | same as above |
| `Scene3D.cs:255` `loadContext.GraphicsDevice.WaitForIdle()` per map object | `Scene3D.LoadObjects` | asset-data | `WaitForIdle` |
| `Scene3D.cs:163` `game.ContentManager.GraphicsDevice.WaitForIdle()` | `Scene3D` ctor | asset-data | `WaitForIdle` |
| `Terrain/Terrain.cs`, `Terrain/TerrainPatch.cs`, `Terrain/TerrainPatchIndexBufferCache.cs`, `Terrain/WaterArea.cs`, `Terrain/RadiusCursorDecals.cs`, `Terrain/Roads/Road.cs` | terrain/road visual construction during map load | asset-data reached, presentation purpose (GPU upload of terrain/road visuals) | texture + buffer creation |
| `Gui/Wnd/Images/MappedImageUtility.cs`, `ImageTextureCache.cs`, `CachedImageSource.cs`, `StretchableMappedImageSource.cs`, `MappedImageSource.cs` | Wnd image sources | presentation | texture creation |

Both on-demand loaders are only reachable when the strategy supplies them;
`OnDemandAssetLoadStrategy.None` (`Content/OnDemandAssetLoadStrategy.cs:11`) passes null loaders,
so under `None` the INI/map/object-definition parse path performs no GPU work. `TestGame`
(`test/Panopticon.Simulation.Tests/MockedGameTest.cs:145`) already constructs `AssetStore` with all four
graphics parameters null plus `None`, proving the parse boundary is device-free. It would fail on
a full map load (`Scene3D` ctor reaches terrain/road construction and `WaitForIdle`) and on any
`ScriptingSystem` construction (below); it never runs a full logic tick today because `Update`
throws `NotImplementedException`.

### Logic path

| Site | Owning type | Classification | Minimal surface used |
|---|---|---|---|
| `Scripting/ScriptingSystem.cs:54` `new CameraFadeOverlay(game)` | `ScriptingSystem` ctor, ticked by `ScriptingTick` | logic construction, presentation payload | ctor builds `DrawingContext2D` |
| `Scripting/CameraFadeOverlay.cs:30-34` `new DrawingContext2D(game.ContentManager, game.GraphicsLoadContext, ...)` | `CameraFadeOverlay` | logic construction, presentation payload | `ConstantBuffer` → `ResourceFactory.CreateBuffer`; `StandardGraphicsResources.SolidWhiteTexture`, `.LinearClampSampler` (line 92); `ShaderSetStore`-built sprite pipeline |
| `Logic/Object/GameObject.cs:1158`, `Logic/OrderGenerators/RallyPointOrderGenerator.cs:87`, `Logic/Object/Draw/*.cs` (`W3dModelDraw.cs:425,541`, `W3dDebrisDraw.cs:44`, `W3dTankDraw.cs:49`, `W3dTreeDraw.cs:37`, `W3dTracerDraw.cs:30`, `W3dRopeDraw.cs:27`, `W3dFloorDraw.cs:38`, `W3dLaserDraw.cs:38`, `W3dDefaultDraw.cs:22`, `W3dProjectileStreamDraw.cs:18`), `Client/Drawable.cs:353` | draw modules | logic files, zero device surface | `MeshShaderResources.RenderItemConstantsPS` is a nested payload struct; no device member is touched |

The only genuine logic-path device coupling is the eager `CameraFadeOverlay` construction inside
`ScriptingSystem`. Its fade state (`FadeType`, frame counters) is scripted simulation state, but
its drawing context is pure presentation. The A1-2 boundary (make the graphics dependency
optional) does not reach it; recorded here for the A3 seam.

### Presentation (complete list, no logic or asset-data reach)

`Diagnostics/*` (`MainView.cs`, `DeveloperModeView.cs`, `RenderSettingsView.cs`, `GameView.cs`,
`RenderedView.cs`, `AptGeometryView.cs`, `AssetViews/TextureView.cs`,
`AssetViews/MappedImageView.cs`), `Graphics/*` (`GraphicsSystem.cs`, `RenderTarget.cs`,
`SpriteBatch.cs`, `ConstantBuffer`1.cs`, `TextureCopier.cs`, `FrameCapturer.cs`,
`FrameCaptureService.cs`, `ModelInstance.cs`, `ModelMeshInstance.cs`, `ModelMesh.W3d.cs`,
`ParticleSystems/ParticleMaterial.cs`, `ParticleSystems/ParticleSystem.cs`,
`Rendering/RenderContext.cs`, `Rendering/RenderPipeline.cs`, `Rendering/Shadows/ShadowData.cs`,
`Rendering/Shadows/ShadowMapRenderer.cs`, `Rendering/Water/WaterData.cs`,
`Rendering/Water/WaterMapRenderer.cs`, `Shaders/*.cs` incl. `ShaderResourceManager.cs`,
`ShaderSetBase.cs`, `ShaderMaterialShaderResources.cs`, `ShaderMaterialResourceSetBuilder.cs`,
`StandardGraphicsResources.cs`, `GraphicsDeviceUtility.cs`), `Gui/*` (`Apt/AptRenderingContext.cs`,
`Apt/AptWindow.cs`, `Apt/AptWindowManager.cs`, `Apt/SpriteItem.cs`,
`CommandListOverlay/RadialButton.cs`, `DrawingContext2D.cs`, `TextCache.cs`,
`Wnd/Controls/Window.cs`), `GamePanel.cs`, `IGame.cs` (property declarations),
`Utilities/Extensions/ResourceFactoryExtensions.cs`. Ambiguous count: 0.

### Candidate null-device surface

Members actually used by `logic` and `asset-data` sites, i.e. the candidate null-device
interface if one is introduced:

- `GraphicsDevice.WaitForIdle()` (map load: `Scene3D.cs:163`, `Scene3D.cs:255`)
- `ResourceFactory.CreateTexture(TextureDescription)` (on-demand loaders; terrain/road upload)
- `GraphicsDevice.UpdateTexture(DeviceBuffer/TextureRegion source, Texture target)` and staging
  `Map(Mode, ...)` readback (on-demand loaders)
- `ResourceFactory.CreateBuffer(uint)` (logic path: `ConstantBuffer` via `CameraFadeOverlay` only)
- `StandardGraphicsResources.SolidWhiteTexture`, `.LinearClampSampler`, `.PlaceholderTexture`
  (logic path: `CameraFadeOverlay`/`SpriteBatch`; loader fallback: `ShaderMaterialShaderResources.cs:105`)
- `GraphicsDevice.IsUvOriginTopLeft` (`Game.GetTopLeftUV`/`GetBottomRightUV`, `Game.cs:1072-1084`)

Everything else the logic path touches is shielded by `IGameEngine`, which exposes no graphics
members (`GameEngine.cs`). Conclusion: the logic/asset-data surface is small enough that making
the `AssetStore` graphics parameters optional (A1-2) plus skipping GPU upload removes H2 without a
null-device abstraction; H1 (unconditional device creation in the `Game` ctor) remains the
blocker for a GPU-less full `Game`, so the A1-4 host test runs against the mocked `TestGame`
path, matching the card.

## Tick-path presentation audit

Every call made from `Game.Update`, `LocalLogicTick`, and `LogicTick`
(`src/Panopticon.Simulation/Game.cs:921-1000`), classified per
[`WI-A1-02`](../agent/cards/WI-A1-02-audit-tick-presentation-couplings.md). Key: `logic` (must run
headless), `presentation` (must not run headless), `mixed` (does both, needs splitting).

### `Game.Update(IEnumerable<InputMessage>)`

| Line | Call | Classification |
|---|---|---|
| 924 | `LocalLogicTick(messages)` | mixed; split within (table below) |
| 926 | `var totalGameTime = MapTime.TotalTime` | logic (timing read) |
| 929 | `if (IsLogicRunning && totalGameTime >= _nextLogicUpdate)` | logic |
| 931 | `LogicTick()` | logic |
| 932 | `CumulativeLogicUpdateError += (totalGameTime - _nextLogicUpdate)` | logic |
| 934 | `_nextLogicUpdate += TimeSpan.FromMilliseconds(LogicUpdateInterval)` | logic |
| 936-939 | `_isStepping` check, `IsLogicRunning = false` | logic (single-step mode) |
| 943 | `if (IsLogicRunning && totalGameTime >= _nextScriptingUpdate)` | logic |
| 945 | `Scripting.ScriptingTick()` | logic |
| 947 | `_nextScriptingUpdate += TimeSpan.FromMilliseconds(_scriptingUpdateInterval)` | logic |

### `Game.LocalLogicTick(IEnumerable<InputMessage>)`

| Line | Call | Classification |
|---|---|---|
| 953-954 | `_mapTimer.Update(); MapTime = _mapTimer.CurrentGameTime` | logic (advances the map clock) |
| 956-957 | `_renderTimer.Update(); RenderTime = _renderTimer.CurrentGameTime` | presentation |
| 959 | `InputMessageBuffer.PumpEvents(messages, RenderTime)` | presentation (input dispatch) |
| 962-969 | `SkirmishManager?.Update()` + extra `_mapTimer.Update()`/MapTime refresh | mixed: network lobby setup logic plus a MapTime correction hack; gate is `SkirmishManager.Settings.Status != Started`, so it never runs in-game or headless with an `EchoConnection` |
| 972-973 | `tickT` interpolation fraction | presentation (feeds `Scene3D.LocalLogicTick`) |
| 976 | `Scene2D.LocalLogicTick(RenderTime, ...)` | presentation (UI) |
| 980 | `Scene3D?.LocalLogicTick(MapTime, tickT, ...)` | presentation (camera, order-generator input, terrain visuals, particles; see `Scene3D.cs:364-389`) |
| 982 | `Audio.Update(Scene3D?.Camera)` | presentation |
| 983 | `Cursors.Update(RenderTime)` | presentation |
| 985 | `Updating?.Invoke(this, ...)` | presentation (observer event; no engine subscriber) |

### `Game.LogicTick()`

| Line | Call | Classification |
|---|---|---|
| 990 | `GameLogic.Update()` | logic |
| 992 | `NetworkMessageBuffer?.Tick()` | logic (order dispatch/network) |
| 996 | `GetTimeInterval()` (`new(MapTime.TotalTime, MsPerLogicFrame)`) | logic |
| 997 | `Scene3D?.LogicTick(timeInterval)` | logic (`PlayerManager.LogicTick`, `gameObject.LogicTick`, `DeleteDestroyed`; `Scene3D.cs:352-362`) |
| 999 | `PartitionCellManager.Update()` | logic |

Mixed calls needing splitting: only `LocalLogicTick` itself (its first two lines advance the
logic map clock; everything after is presentation) and the `SkirmishManager` block (setup
networking; keep behind its existing pre-game gate). `Game.Render` (`Game.cs:1027-1031`) is the
boundary the host never calls.

### Timing-state split

| Logic stepping state | Render-only state |
|---|---|
| `_mapTimer` (advanced by the stepper) | `_renderTimer` |
| `MapTime` | `RenderTime`, `CurrentGameTime` |
| `_nextLogicUpdate` | (input/UI consumers of `RenderTime` above) |
| `_nextScriptingUpdate` | `tickT` interpolation fraction |
| `LogicUpdateInterval` (from `GameEngine.MsPerLogicFrame` / `LogicUpdateScaleFactor`) | |
| `_scriptingUpdateInterval` (`1000 / ScriptingTicksPerSecond`) | |
| `CumulativeLogicUpdateError` | |
| `IsLogicRunning`, `_isStepping` | |

### `EnableFixedLogicTimestep` relation to the A1-3 stepper

`Game.EnableFixedLogicTimestep` (`Game.cs:1022-1025`) sets `_mapTimer.FixedDelta =
MsPerLogicFrame`, so each `LocalLogicTick` advances `MapTime` by exactly one logic frame and each
`Update` performs exactly one logic step (and, because both `_nextLogicUpdate` and
`_nextScriptingUpdate` advance by their intervals from zero, exactly one scripting tick once the
scripting interval divides evenly into the frame interval; for Generals both are 30 Hz). This is
the P9 Unit Validation Ground mechanism and is a special case of the A1-3 stepper, not a
replacement: the stepper owns the same `_nextLogicUpdate`/`_nextScriptingUpdate` arithmetic but
advances map time itself (fixed delta per step for fixed-step hosts, wall-clock when windowed),
rather than relying on `Game.Update` to have done it. Generalization required: none to the
arithmetic; the stepper must keep `FixedDelta` behavior byte-identical when driven through
`Game.Update`.

## A1-5 tier implementation

All tiers feed from one `WorldStateSampler` pass per tick (`src/Panopticon.Simulation/Recording/`):
a `WorldStateSummary` (per-object id/definition/owner/position/health plus the player money
vector, id-sorted) and the frame `WorldStateDelta` against the previous tick. Types:

| Tier | Type | Design |
|---|---|---|
| T0 | `RunOutputRecorder` → `RunOutput.MetricSamples` | Per-player metrics at a bounded sample interval; terminal `GameOutcome`. |
| T1 | `TieredSignatureRecorder` → `RunOutput.SignatureTrees` | `SignatureTreeBuilder` builds a root + spatial-cell-bucket tree per sampled frame. Bucket keys are origin-anchored grid cells (`cell:x,y`, default 100 world units), the stable domain-topology address for the RTS grid domain; entity signatures hash definition, owner, position/health quantized to `SignatureTolerances` (position ε 0.5, health ε 1.0). Bucket and root hashes are order-independent combines (sum of mixed member hashes), so differently-iterated or partitioned runs can be inspected. Per-frame trees are built only while a T2 trigger is armed. |
| T2 | `TriggeredDetailCapture` → `RunOutput.TriggeredCaptures` | Ring buffer (default depth 30 frames) of per-frame deltas, tripped live by the armed trigger with the diverged subtree keys recorded; cooldown of one ring depth between trips, max 8 windows, plus pre-marked windows (`WithMarkedWindow`). |
| Snapshots | `SnapshotDeltaRecorder` → `RunOutput.SnapshotArtifactPath` + `SnapshotFrames` | Full summaries at a snapshot interval (default 300) streamed to a binary artifact (`WorldStateArtifactWriter`, save-format `StatePersister` primitives, flushed per record) plus per-frame deltas; `WorldStateArtifact.Reconstruct` rebuilds any frame from the latest baseline plus deltas. Whole-object save blobs were rejected: `GameObject.Persist` needs live team/map context and cannot round-trip standalone. |

`RunRecording.Attach(host, game, seed, ...)` wires sampler + all recorders onto a host;
`Complete()` fills the `RunOutput` DTOs. `SignatureTreeComparer.FindDivergedSubtrees` is the
emission-side localization helper; the comparator UI remains A5-4.

WI-A1-08 (`SimulationHost` migration): `ReplayScenarioRunner.RunHeadless` and the P0
`HarnessRunner.RunAndAssert` loop step through `SimulationHost` (fixed-timestep FTRT) with
per-frame work on `TickStarting` (pre-step dispatch: scheduled actions, deferred-injection
drain) and `TickComplete` (recorder polls, runout/timeout checks). Frame counters that read
`MapTime` now read `NetworkMessageBuffer.CurrentFrame` (order-frame counter, tick-aligned) or
the host frame. Deferred-injection countdowns are per logic frame now, frame-based where the
wall-clock loop was not. Capture mode (`CaptureFrames: true`) is the documented render
exception: it renders offscreen frames on `TickComplete` so `FrameCaptureService` can inspect
them. Windowed mode and the validation ground (already fixed-timestep, interactive pause
semantics) are unchanged.
