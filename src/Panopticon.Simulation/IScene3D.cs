using System;
using System.Collections.Generic;
using Panopticon.Audio;
using Panopticon.Content.Loaders;
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
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Rendering;
using Panopticon.Scripting;
using Panopticon.Settings;
using Panopticon.Terrain;
using Panopticon.Terrain.Roads;
using Player = Panopticon.Logic.Player;

namespace Panopticon;

public interface IScene3D : IDisposable
{
    IEditorCameraController EditorCameraController { get; }
    IGameEngine GameEngine { get; }
    SelectionGui SelectionGui { get; }
    DebugOverlay DebugOverlay { get; }
    internal ParticleSystemManager ParticleSystemManager { get; }
    internal TracerManager TracerManager { get; }
    internal ProjectileStreamManager ProjectileStreamManager { get; }
    Camera Camera { get; }
    TacticalView TacticalView { get; }
    MapFile MapFile { get; }
    Terrain.Terrain Terrain { get; }
    IQuadtree<GameObject> Quadtree { get; }
    bool ShowTerrain { get; set; }
    WaterAreaCollection WaterAreas { get; }
    bool ShowWater { get; set; }
    RoadCollection Roads { get; }
    bool ShowRoads { get; set; }
    Bridge[] Bridges { get; }
    bool ShowBridges { get; set; }
    bool FrustumCulling { get; set; }
    PlayerScriptsList PlayerScripts { get; }
    IGameObjectCollection GameObjects { get; }
    bool ShowObjects { get; set; }
    CameraCollection Cameras { get; }
    WaypointCollection Waypoints { get; }
    WorldLighting Lighting { get; }
    ShadowSettings Shadows { get; }
    WaterSettings Waters { get; }
    IReadOnlyList<Player> Players { get; }
    Player LocalPlayer { get; }
    Navigation.Navigation Navigation { get; }
    AudioSystem Audio { get; }
    internal AssetLoadContext AssetLoadContext { get; }
    Radar Radar { get; }
    IGame Game { get; }
    GameObject BuildPreviewObject { get; set; }
    RenderScene RenderScene { get; }
    RadarDrawUtil RadarDrawUtil { get; }
    int GetPlayerIndex(Player player);
    void LogicTick(in TimeInterval time);
    void LocalLogicTick(in TimeInterval gameTime, float tickT, in TimeInterval? cameraTime = null);
    void BuildRenderList(RenderList renderList, Camera camera, in TimeInterval gameTime);
    void Render(DrawingContext2D drawingContext);
    GameObject CreateSkirmishPlayerStartingBuilding(in PlayerSetting playerSetting, Player player);
}
