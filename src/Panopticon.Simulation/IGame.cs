using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Audio;
using Panopticon.Client;
using Panopticon.Content;
using Panopticon.Data.Sav;
using Panopticon.Graphics;
using Panopticon.Gui;
using Panopticon.Gui.Apt;
using Panopticon.Gui.Wnd.Controls;
using Panopticon.Input;
using Panopticon.Input.Cursors;
using Panopticon.IO;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Scripting;
using Veldrid;

namespace Panopticon;

public interface IGame
{
    CursorManager Cursors { get; }
    internal GraphicsLoadContext GraphicsLoadContext { get; }
    AssetStore AssetStore { get; }
    ContentManager ContentManager { get; }
    GraphicsDevice GraphicsDevice { get; }
    InputMessageBuffer InputMessageBuffer { get; }
    SkirmishManager SkirmishManager { get; set; }
    LobbyManager LobbyManager { get; }
    List<GameSystem> GameSystems { get; }

    /// <summary>
    /// Gets the graphics system.
    /// </summary>
    GraphicsSystem Graphics { get; }

    /// <summary>
    /// Gets the scripting system.
    /// </summary>
    ScriptingSystem Scripting { get; }

    /// <summary>
    /// Load lua script engine.
    /// </summary>
    LuaScriptEngine Lua { get; set; }

    /// <summary>
    /// Gets the selection system.
    /// </summary>
    SelectionSystem Selection { get; }

    /// <summary>
    /// Gets the order generator system.
    /// </summary>
    OrderGeneratorSystem OrderGenerator { get; }

    /// <summary>
    /// Gets the audio system
    /// </summary>
    AudioSystem Audio { get; }

    GameState GameState { get; }
    internal GameStateMap GameStateMap { get; }
    CampaignManager CampaignManager { get; }
    Terrain.TerrainLogic TerrainLogic { get; }
    Terrain.TerrainVisual TerrainVisual { get; }
    GhostObjectManager GhostObjectManager { get; }

    /// <summary>
    /// Is the game running?
    /// This is only false when the game is shutting down.
    /// </summary>
    bool IsRunning { get; }

    Action Restart { get; set; }

    /// <summary>
    /// Are we currently in a skirmish game?
    /// </summary>
    bool InGame { get; }

    float LogicUpdateScaleFactor { get; set; }

    /// <summary>
    /// Is the game running logic updates?
    /// Automatically starts and stops the map timer.
    /// </summary>
    bool IsLogicRunning { get; set; }

    /// <summary>
    /// The amount of time the game has been in this map while running logic updates.
    /// </summary>
    TimeInterval MapTime { get; }

    TimeInterval CurrentGameTime { get; }

    /// <summary>
    /// The amount of time the game has been rendering frames.
    /// </summary>
    TimeInterval RenderTime { get; }

    TimeSpan CumulativeLogicUpdateError { get; }
    IGameDefinition Definition { get; }
    SageGame SageGame { get; }
    Configuration Configuration { get; }
    GameOutcome CurrentOutcome { get; }
    VictoryOptions VictoryOptions { get; set; }

    event EventHandler MatchOutcomeDecided;
    string UserDataLeafName { get; }
    string UserDataFolder { get; }
    string UserAppDataFolder { get; }
    GamePanel Panel { get; }
    Viewport Viewport { get; }
    Scene2D Scene2D { get; }
    IScene3D Scene3D { get; set; }

    NetworkMessageBuffer NetworkMessageBuffer { get; set; }

    Texture LauncherImage { get; }
    internal GameLogic GameLogic { get; }
    internal GameClient GameClient { get; }
    internal VictorySystem VictorySystem { get; }
    PlayerManager PlayerManager { get; }
    TeamFactory TeamFactory { get; }
    PartitionCellManager PartitionCellManager { get; }
    IGameEngine GameEngine { get; }

    event EventHandler<GameUpdatingEventArgs> Updating;

    /// <summary>
    /// Fired each time an <see cref="Order"/> is processed by the <see cref="OrderProcessor"/>.
    /// </summary>
    event EventHandler<Logic.Orders.OrderProcessedEventArgs> OrderProcessed;

    /// <summary>
    /// Fired each time a <see cref="Logic.Object.GameObject"/> is created via
    /// <see cref="Logic.GameLogic.CreateObject"/>.
    /// </summary>
    event EventHandler<Logic.GameObjectCreatedEventArgs> ObjectCreated;

    /// <summary>
    /// Fired each time a <see cref="Logic.Object.GameObject"/> is destroyed via
    /// <see cref="Logic.GameLogic.DestroyObject"/>.
    /// </summary>
    event EventHandler<Logic.GameObjectDestroyedEventArgs> ObjectDestroyed;

    /// <summary>
    /// Fired when a building finishes construction (transitions out of the under-construction state).
    /// </summary>
    event EventHandler<Logic.GameObjectConstructionCompletedEventArgs> ConstructionCompleted;

    /// <summary>
    /// Immediately processes <paramref name="orders"/> through the order processor,
    /// bypassing the replay file. Intended for event-driven order injection at runtime.
    /// </summary>
    void InjectOrders(IEnumerable<Logic.Orders.Order> orders);

    /// <summary>
    /// Fired when a <see cref="Game.Render"/> completes, but before
    /// <see cref="Game.Panel"/>'s <see cref="GamePanel.Framebuffer"/>
    /// is copied to <see cref="GraphicsDevice.SwapchainFramebuffer"/>.
    /// Useful for drawing additional overlays.
    /// </summary>
    event EventHandler RenderCompleted;

    void LoadSaveFile(FileSystemEntry entry);
    void LoadReplayFile(FileSystemEntry replayFileEntry);
    void ShowMainMenu();
    Window LoadWindow(string wndFileName);
    AptWindow LoadAptWindow(string aptFileName);
    void StartCampaign(string side);
    void StartCampaign(string campaignName, string missionName);

    void StartSkirmishOrMultiPlayerGame(
        string mapFileName,
        IConnection connection,
        PlayerSetting[] playerSettings,
        int seed,
        bool isMultiPlayer);

    void StartSinglePlayerGame(string mapFileName);
    void EndGame();
    void StartRun();
    void Update(IEnumerable<InputMessage> messages);
    void Step();
    void Render();

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources
    /// </summary>
    void Dispose();

    Vector2 GetTopLeftUV();
    Vector2 GetBottomRightUV();
    IEnumerable<PlayerTemplate> GetPlayableSides();
    MappedImage GetMappedImage(string name);
    void Exit();

    // TODO: Move this somewhere where it could be configured for specific games;
    // e.g. for a replay we might want to use SageRandom.
    IRandom CreateRandom() => new SageRandom();
}
