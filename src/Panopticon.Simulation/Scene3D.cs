using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Audio;
using Panopticon.Client;
using Panopticon.Content.Loaders;
using Panopticon.Content.Util;
using Panopticon.Data.Map;
using Panopticon.DataStructures;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Graphics.ProjectileStreams;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Rendering.Shadows;
using Panopticon.Graphics.Rendering.Water;
using Panopticon.Graphics.Tracers;
using Panopticon.Gui;
using Panopticon.Gui.DebugUI;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Rendering;
using Panopticon.Scripting;
using Panopticon.Settings;
using Panopticon.Terrain;
using Panopticon.Terrain.Roads;
using Veldrid;
using Player = Panopticon.Logic.Player;

namespace Panopticon;

public sealed class Scene3D : DisposableBase, IScene3D
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // TODO: Remove or refactor these
    // RtsCameraController uses its own input handling, so these are only used for debug cameras
    //private readonly EditorCameraInputMessageHandler _editorCameraInputMessageHandler;
    //private EditorCameraInputState _editorCameraInputState;
    public IEditorCameraController EditorCameraController { get; }

    public IGameEngine GameEngine { get; }

    public SelectionGui SelectionGui { get; }

    private readonly DebugMessageHandler _debugMessageHandler;
    public DebugOverlay DebugOverlay { get; private set; }

    ParticleSystemManager IScene3D.ParticleSystemManager => ParticleSystemManager;
    internal ParticleSystemManager ParticleSystemManager { get; }

    TracerManager IScene3D.TracerManager => TracerManager;
    internal TracerManager TracerManager { get; }

    ProjectileStreamManager IScene3D.ProjectileStreamManager => ProjectileStreamManager;
    internal ProjectileStreamManager ProjectileStreamManager { get; }

    private readonly OrderGeneratorSystem _orderGeneratorSystem;

    public Camera Camera { get; }

    public TacticalView TacticalView { get; }

    public MapFile MapFile { get; }

    public Terrain.Terrain Terrain { get; }

    public IQuadtree<GameObject> Quadtree { get; }
    public bool ShowTerrain
    {
        get => RenderScene.GetRenderBucket("Terrain").Visible;
        set => RenderScene.GetRenderBucket("Terrain").Visible = value;
    }

    public WaterAreaCollection WaterAreas { get; }
    public bool ShowWater { get; set; } = true;

    public RoadCollection Roads { get; }
    public bool ShowRoads
    {
        get => RenderScene.GetRenderBucket("Roads").Visible;
        set => RenderScene.GetRenderBucket("Roads").Visible = value;
    }

    public Bridge[] Bridges { get; }
    public bool ShowBridges { get; set; } = true;

    public bool FrustumCulling { get; set; } = false;

    public PlayerScriptsList PlayerScripts { get; private set; }

    public IGameObjectCollection GameObjects => GameEngine.GameLogic;
    public bool ShowObjects { get; set; } = true;
    public CameraCollection Cameras { get; }
    public WaypointCollection Waypoints { get; }

    public WorldLighting Lighting { get; }

    public ShadowSettings Shadows { get; } = new ShadowSettings();

    public WaterSettings Waters { get; } = new WaterSettings();

    public IReadOnlyList<Player> Players => Game.PlayerManager.Players;
    public Player LocalPlayer => Game.PlayerManager.LocalPlayer;
    public Navigation.Navigation Navigation { get; }

    public AudioSystem Audio => Game.Audio;

    AssetLoadContext IScene3D.AssetLoadContext => AssetLoadContext;
    internal AssetLoadContext AssetLoadContext => Game.AssetStore.LoadContext;

    private readonly OrderGeneratorInputHandler _orderGeneratorInputHandler;

    public Radar Radar { get; }

    public IGame Game { get; }

    public GameObject BuildPreviewObject { get; set; }

    public RenderScene RenderScene { get; }

    private readonly Scene25D _scene25D;

    public RadarDrawUtil RadarDrawUtil { get; }

    internal Scene3D(
        IGame game,
        MapFile mapFile,
        string mapPath,
        Data.Map.Player[] mapPlayers,
        Data.Map.Team[] mapTeams,
        ScriptList[] mapScriptLists,
        GameType gameType,
        PlayerSetting[] playerSettings = null,
        MapObject[] materializedObjects = null,
        bool[,] maskFeed = null)
        : this(game, () => game.Viewport, game.InputMessageBuffer, false, mapFile, mapPath, maskFeed)
    {
        game.Scene3D = this;

        game.PlayerManager.OnNewGame(mapPlayers, gameType, playerSettings);

        game.TeamFactory.Initialize(mapTeams);

        Lighting = new WorldLighting(
            mapFile.GlobalLighting.LightingConfigurations.ToLightSettingsDictionary(),
            mapFile.GlobalLighting.Time);

        // Lighting, water, cameras, and paths remain on the parsed MapFile until the retained-sections backend is replaced.
        LoadObjects(
            game.AssetStore.LoadContext,
            Terrain.HeightMap,
            materializedObjects ?? mapFile.ObjectsList.Objects,
            MapFile.NamedCameras,
            out var waypoints,
            out var roads,
            out var bridges,
            out var cameras);

        Roads = roads;
        Bridges = bridges;
        Waypoints = waypoints;
        Cameras = cameras;

        // Generated-bridge roadway surfaces (WP1029): one registration per generated
        // bridge replaces the previous map's list, so the movement query rides the deck
        // of the loaded map. The clearance cut itself already ran inside LoadObjects
        // (before RoadCollection); legacy bridges carry no provenance marker and register
        // nothing, keeping raw ground behavior on legacy maps.
        Game.TerrainLogic.ClearBridgeSurfaces();

        foreach (var bridge in bridges)
        {
            if (bridge.IsGeneratedBridge && bridge.CreateSurface() is { } surface)
            {
                Game.TerrainLogic.RegisterBridgeSurface(surface);
            }

            if (bridge.CorridorPlan is { } corridorPlan)
            {
                Game.TerrainLogic.RegisterBridgeCorridor(corridorPlan.ToCorridor(GeneratedBridgeCorridorPlanner.PoseBlendDistance));
            }
        }

        AddDisposeAction(() => Game.TerrainLogic.ClearBridgeSurfaces());

        PlayerScripts = new PlayerScriptsList
        {
            ScriptLists = mapScriptLists
        };

        TacticalView = new TacticalView(game.AssetStore.GameData.Current, Camera, game.TerrainLogic, game.Cursors, GameEngine.MsPerLogicFrame, game.Configuration.CameraControlScheme);
        RegisterInputHandler(TacticalView.LookAtTranslator, game.InputMessageBuffer);

        game.ContentManager.GraphicsDevice.WaitForIdle();

        _scene25D = game.Definition.CreateScene25D(this, AssetLoadContext.AssetStore);
    }

    private void LoadObjects(
        AssetLoadContext loadContext,
        HeightMap heightMap,
        MapObject[] mapObjects,
        NamedCameras namedCameras,
        out WaypointCollection waypointCollection,
        out RoadCollection roads,
        out Bridge[] bridges,
        out CameraCollection cameras)
    {
        var waypoints = new List<Waypoint>();

        var bridgesList = new List<Bridge>();

        var roadTopology = new RoadTopology();

        for (var i = 0; i < mapObjects.Length; i++)
        {
            var mapObject = mapObjects[i];

            if (Terrain != null && Panopticon.Terrain.Decals.TerrainDecalMapLoader.TryAdd(Terrain.Decals, mapObject))
            {
                continue;
            }

            switch (mapObject.RoadType & RoadType.PrimaryType)
            {
                case RoadType.None:
                    switch (mapObject.TypeName)
                    {
                        case Waypoint.ObjectTypeName:
                            waypoints.Add(new Waypoint(mapObject));
                            break;

                        default:
                            GameObject.FromMapObject(mapObject, GameEngine, overwriteAngle: null);
                            break;
                    }

                    break;

                case RoadType.BridgeStart:
                case RoadType.BridgeEnd:
                    // Multiple invalid bridges can be found in e.g GLA01.
                    if ((i + 1) >= mapObjects.Length || !mapObjects[i + 1].RoadType.HasFlag(RoadType.BridgeEnd))
                    {
                        Logger.Warn($"Invalid bridge: {mapObject.ToString()}, skipping...");
                        continue;
                    }

                    var bridgeEnd = mapObjects[++i];

                    var bridge = new Bridge(
                        GameEngine,
                        mapObject,
                        mapObject.Position,
                        bridgeEnd.Position);
                    bridgesList.Add(AddDisposable(bridge));

                    // Marked bridges are placed after their paths are decided below.
                    if (!bridge.HasPathHandoff)
                    {
                        bridge.PlaceAuthored();
                    }

                    break;

                case RoadType.Start:
                case RoadType.End:
                    var roadEnd = mapObjects[++i];

                    // Some maps have roads with invalid start- or endpoints.
                    // We'll skip processing them altogether.
                    if (mapObject.TypeName == "" || roadEnd.TypeName == "")
                    {
                        Logger.Warn($"Road {mapObject} has invalid start- or endpoint, skipping...");
                        continue;
                    }

                    if (!mapObject.RoadType.HasFlag(RoadType.Start) || !roadEnd.RoadType.HasFlag(RoadType.End))
                    {
                        throw new InvalidDataException();
                    }

                    // Note that we're searching with the type of either end.
                    // This is because of weirdly corrupted roads with unmatched ends in USA04, which work fine in WB and SAGE.
                    var roadTemplate =
                        loadContext.AssetStore.RoadTemplates.GetByName(mapObject.TypeName)
                        ?? loadContext.AssetStore.RoadTemplates.GetByName(roadEnd.TypeName);

                    if (roadTemplate == null)
                    {
                        throw new InvalidDataException($"Missing road template: {mapObject.TypeName}");
                    }

                    roadTopology.AddSegment(roadTemplate, mapObject, roadEnd);
                    break;

            }

            loadContext.GraphicsDevice.WaitForIdle();
        }

        cameras = new CameraCollection(namedCameras?.Cameras);

        // Every path decision reads the pre-change heights; the decisions are applied together, then each
        // bridge is placed on its chosen path before the clearance plans below run.
        var pathDecisions = new List<GeneratedBridgePathDecision>();

        foreach (var bridge in bridgesList)
        {
            if (!bridge.IsPlaced)
            {
                pathDecisions.Add(GeneratedBridgePathDecision.Decide(bridge, heightMap));
            }
        }

        ApplyPathDecisions(heightMap, pathDecisions);

        foreach (var decision in pathDecisions)
        {
            if (decision.TakesLevelPath)
            {
                decision.Bridge.PlaceLevel(decision.Layout, decision.Corridor);
                continue;
            }

            Logger.Warn($"Generated bridge '{decision.Bridge.TypeName}' at {decision.Bridge.StartPoint} takes the authored path: {decision.Reason}");
            decision.Bridge.PlaceAuthored();
        }

        // r3-g8 corrective lifecycle: plan every generated supported bridge against the
        // SAME pre-cut heights, merge to one deterministic per-vertex minimum, apply
        // once (bounded normals refresh inside), and notify once — before RoadCollection
        // samples the lattice, so road meshes see the final heights. Bridge anchors were
        // sampled pre-cut in the Bridge constructor and are not resampled.
        var clearancePlans = new List<BridgeSurface.TerrainClearancePlan>();

        foreach (var bridge in bridgesList)
        {
            if (bridge.TryPlanClearance(heightMap) is { } plan)
            {
                clearancePlans.Add(plan);
            }
        }

        var (clearanceBounds, changedVertices) = BridgeSurface.TerrainClearancePlan.MergeAndApply(heightMap, clearancePlans);

        if (changedVertices > 0)
        {
            Terrain.OnHeightMapChanged(clearanceBounds);
        }

        roads = AddDisposable(new RoadCollection(roadTopology, loadContext, heightMap, RenderScene));
        waypointCollection = new WaypointCollection(waypoints, MapFile.WaypointsList.WaypointPaths);
        bridges = bridgesList.ToArray();
    }

    /// <summary>
    /// Writes the landing bands of the fallback paths, then every corridor plan, through one height-map
    /// write pass; corridors overwrite band values where they overlap, and one notification covers all changes.
    /// </summary>
    private void ApplyPathDecisions(HeightMap heightMap, IReadOnlyList<GeneratedBridgePathDecision> decisions)
    {
        var border = GeneratedBridgePathDecision.Border(heightMap);
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var changed = false;

        foreach (var decision in decisions)
        {
            if (decision.TakesLevelPath)
            {
                continue;
            }

            foreach (var target in decision.LandingBand)
            {
                // Off-grid targets are skipped: the converter keeps the original height there.
                if (BridgeLandingBand.ToElevation(target.Height) is not { } elevation)
                {
                    continue;
                }

                var x = target.X + border;
                var y = target.Y + border;

                if (heightMap.GetElevation(x, y) == elevation)
                {
                    continue;
                }

                heightMap.SetElevation(x, y, elevation);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                changed = true;
            }
        }

        var corridors = new List<GeneratedBridgeCorridorPlan>();

        foreach (var decision in decisions)
        {
            if (decision.TakesLevelPath)
            {
                corridors.Add(decision.Corridor);
            }
        }

        var applied = GeneratedBridgeCorridorPlanner.Apply(heightMap, corridors);

        if (applied.Changed > 0)
        {
            minX = Math.Min(minX, applied.MinX);
            minY = Math.Min(minY, applied.MinY);
            maxX = Math.Max(maxX, applied.MaxX);
            maxY = Math.Max(maxY, applied.MaxY);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        heightMap.RefreshNormalsInBoundedArea(minX, minY, maxX, maxY);

        // Same bounds rectangle as BridgeSurface.TerrainClearancePlan.MergeAndApply reports.
        var minCorner = heightMap.GetPosition(minX, minY);
        var maxCorner = heightMap.GetPosition(maxX, maxY);
        var bounds = new RectangleF(
            minCorner.X - HeightMap.HorizontalScale,
            minCorner.Y - HeightMap.HorizontalScale,
            (maxCorner.X - minCorner.X) + (2f * HeightMap.HorizontalScale),
            (maxCorner.Y - minCorner.Y) + (2f * HeightMap.HorizontalScale));
        Terrain.OnHeightMapChanged(bounds);
    }

    internal Scene3D(
        IGame game,
        InputMessageBuffer inputMessageBuffer,
        Func<Viewport> getViewport,
        IEditorCameraController cameraController,
        WorldLighting lighting,
        bool isDiagnosticScene = false)
        : this(game, getViewport, inputMessageBuffer, isDiagnosticScene, null, null, null)
    {
        WaterAreas = AddDisposable(new WaterAreaCollection());
        Lighting = lighting;

        Roads = AddDisposable(new RoadCollection());
        Bridges = Array.Empty<Bridge>();
        Waypoints = new WaypointCollection();
        Cameras = new CameraCollection();

        EditorCameraController = cameraController;
    }

    private Scene3D(IGame game, Func<Viewport> getViewport, InputMessageBuffer inputMessageBuffer, bool isDiagnosticScene, MapFile mapFile, string mapPath, bool[,] maskFeed)
    {
        Game = game;

        Shadows.ApplyTier(game.GraphicsSettings.ShadowTier);

        Camera = new Camera(getViewport);

        SelectionGui = new SelectionGui();

        DebugOverlay = new DebugOverlay(this, game.ContentManager);

        RenderScene = new RenderScene();

        if (mapFile != null)
        {
            MapFile = mapFile;
            Terrain = AddDisposable(new Terrain.Terrain(mapFile, game.TerrainLogic.HeightMap, game.AssetStore.LoadContext, RenderScene));
            Terrain.ImpassabilityOverride = maskFeed;
            WaterAreas = AddDisposable(new WaterAreaCollection(mapFile.PolygonTriggers, mapFile.StandingWaterAreas, mapFile.StandingWaveAreas, game.AssetStore.LoadContext));
            Navigation = maskFeed != null
                ? new Navigation.Navigation(maskFeed, Terrain.HeightMap)
                : new Navigation.Navigation(mapFile.BlendTileData, Terrain.HeightMap);
        }

        if (!isDiagnosticScene)
        {
            RegisterInputHandler(new SelectionMessageHandler(game.Selection), inputMessageBuffer);
            RegisterInputHandler(_orderGeneratorInputHandler = new OrderGeneratorInputHandler(game.OrderGenerator, game.Configuration.MouseClickMode), inputMessageBuffer);
            RegisterInputHandler(_debugMessageHandler = new DebugMessageHandler(DebugOverlay), inputMessageBuffer);
        }

        ParticleSystemManager = AddDisposable(new ParticleSystemManager(this, game.AssetStore.LoadContext));

        TracerManager = AddDisposable(new TracerManager(this, game.AssetStore.LoadContext));

        ProjectileStreamManager = AddDisposable(new ProjectileStreamManager(this, game.AssetStore.LoadContext));

        Radar = new Radar();

        if (mapFile != null)
        {
            var borderWidth = mapFile.HeightMapData.BorderWidth * HeightMap.HorizontalScale;
            var width = mapFile.HeightMapData.Width * HeightMap.HorizontalScale;
            var height = mapFile.HeightMapData.Height * HeightMap.HorizontalScale;
            Quadtree = new Quadtree<GameObject>(new RectangleF(-borderWidth, -borderWidth, width, height));
        }

        GameEngine = new GameEngine(
            game.AssetStore.LoadContext,
            game.Audio,
            ParticleSystemManager,
            new ObjectCreationListManager(),
            Terrain,
            Navigation,
            Radar,
            Quadtree,
            this,
            game);

        RadarDrawUtil = new RadarDrawUtil(Radar, Terrain?.HeightMap, GameObjects, Camera, AssetLoadContext.AssetStore, mapPath);

        Game.GameLogic.Reset();
        Game.GameClient.Reset();

        _orderGeneratorSystem = game.OrderGenerator;
    }

    private void RegisterInputHandler(InputMessageHandler handler, InputMessageBuffer inputMessageBuffer)
    {
        inputMessageBuffer.Handlers.Add(handler);
        AddDisposeAction(() => inputMessageBuffer.Handlers.Remove(handler));
    }

    // TODO: Move this over to a player collection?
    public int GetPlayerIndex(Player player) => Game.PlayerManager.GetPlayerIndex(player);

    public void LogicTick(in TimeInterval time)
    {
        Game.PlayerManager.LogicTick();

        foreach (var gameObject in GameObjects.Objects)
        {
            gameObject.LogicTick(time);
        }

        GameObjects.DeleteDestroyed();

        // Steps every body once per logic tick, after behavior updates
        // (PhysicsBehavior runs at Order1). The legacy backend no-ops here.
        GameEngine.PhysicsWorld.Timestep(GameEngine.MsPerLogicFrame / 1000f);
    }

    public void LocalLogicTick(in TimeInterval gameTime, float tickT, in TimeInterval? cameraTime = null)
    {
        _orderGeneratorInputHandler?.Update();

        foreach (var gameObject in GameObjects.Objects)
        {
            gameObject.LocalLogicTick(gameTime, tickT, Terrain?.HeightMap);
        }

        // TODO: Remove or refactor these
        //_editorCameraInputMessageHandler?.UpdateInputState(ref _editorCameraInputState);
        //EditorCameraController?.UpdateCamera(Camera, _editorCameraInputState, gameTime);
        // While the game is paused the map timer is frozen, which would freeze input-driven
        // camera motion with it; cameraTime substitutes a still-running clock for that case.
        TacticalView.Update(cameraTime ?? gameTime);

        DebugOverlay.Update(gameTime);

        if (_orderGeneratorInputHandler != null)
        {
            _orderGeneratorSystem.Update(gameTime, _orderGeneratorInputHandler.KeyModifiers);
        }

        Terrain?.Update(Waters, gameTime, Game.EffectiveGraphicsSettings, Camera);

        ParticleSystemManager?.Update(gameTime);

        TracerManager?.Update(gameTime);

        ProjectileStreamManager?.Update(gameTime);
    }

    public void BuildRenderList(RenderList renderList, Camera camera, in TimeInterval gameTime)
    {
        if (ShowWater)
        {
            WaterAreas.BuildRenderList(renderList);
        }

        if (ShowBridges)
        {
            foreach (var bridge in Bridges)
            {
                bridge.BuildRenderList(renderList, camera);
            }
        }

        if (ShowObjects)
        {
            foreach (var gameObject in GameObjects.Objects)
            {
                if (FrustumCulling
                    && !gameObject.Definition.KindOf.Get(ObjectKinds.NoCollide)
                    && !gameObject.RoughCollider.Intersects(Camera.BoundingFrustum))
                {
                    continue;
                }

                if (gameObject.Client is Drawable drawable)
                {
                    drawable.BuildObjectRenderList(renderList, camera, gameTime);
                }
            }
        }

        _orderGeneratorSystem.BuildRenderList(renderList, camera, gameTime);
    }

    // This is for drawing 2D elements which depend on the Scene3D, e.g tooltips and health bars.
    public void Render(DrawingContext2D drawingContext)
    {
        _scene25D?.Draw(drawingContext);

        SelectionGui?.Draw(drawingContext);
        DebugOverlay?.Draw(drawingContext, Camera);
    }

    // TODO(Port): placeNetworkBuildingsForPlayer in GameLogic.cpp
    public GameObject CreateSkirmishPlayerStartingBuilding(in PlayerSetting playerSetting, Player player)
    {
        // TODO: Not sure what the OG does here.
        var playerStartPosition = new Vector3(80, 80, 0);
        if (Waypoints.TryGetPlayerStart(playerSetting.StartPosition ?? 1, out var startWaypoint))
        {
            playerStartPosition = startWaypoint.Position;
        }

        playerStartPosition.Z += Terrain.HeightMap.GetHeight(playerStartPosition.X, playerStartPosition.Y);

        if (player.Template.StartingBuilding != null)
        {
            var startingBuilding = GameObjects.CreateObject(player.Template.StartingBuilding.Value, player);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathUtility.ToRadians(startingBuilding.Definition.PlacementViewAngle));
            startingBuilding.UpdateTransform(playerStartPosition, rotation);

            Navigation.UpdateAreaPassability(startingBuilding, false);

            var startingUnit0 = GameObjects.CreateObject(player.Template.StartingUnits[0].Unit.Value, player);
            var startingUnit0Position = playerStartPosition;
            startingUnit0Position += Vector3.Transform(Vector3.UnitX, startingBuilding.Rotation) * startingBuilding.Definition.Geometry.Shapes[0].MajorRadius;
            startingUnit0.SetTranslation(startingUnit0Position);

            Game.Selection.SetSelectedObjects(player, [startingBuilding], playAudio: false);
            return startingBuilding;
        }
        else
        {
            // BFME specific logic

            var castleBehaviors = new List<(CastleBehavior, TeamTemplate)>();
            GameObject lastGameObject = null;
            foreach (var gameObject in GameObjects.Objects)
            {
                var team = gameObject.TeamTemplate;
                if (team?.Name == $"Player_{playerSetting.StartPosition}_Inherit")
                {
                    var castleBehavior = gameObject.FindBehavior<CastleBehavior>();
                    if (castleBehavior != null)
                    {
                        castleBehaviors.Add((castleBehavior, team));
                        lastGameObject = gameObject;
                    }
                }
            }

            foreach (var (castleBehavior, _) in castleBehaviors)
            {
                castleBehavior.Unpack(player, instant: true);
            }

            // This is basically guaranteed to be wrong
            if (lastGameObject != null)
            {
                return lastGameObject;
            }
        }

        return null;
    }
}
