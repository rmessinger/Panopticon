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
using Panopticon.Logic.Orders;
using Panopticon.Network;
using Panopticon.Scripting;
using Panopticon.Terrain;
using Panopticon.Tests.Content;
using Panopticon.Tests.Content.Importing;
using Veldrid;

namespace Panopticon.Tests.Graphics.Rendering;

/// <summary>
/// Minimal headless IGame for RenderPipeline construction on the TestGraphicsContext
/// device; only the members the pipeline constructor reads are wired, mirroring the
/// MockedGameTest.TestGame stub pattern.
/// </summary>
internal sealed class TestGraphicsGame : IGame
{
    public TestGraphicsGame(TestGraphicsContext graphics)
    {
        GraphicsDevice = graphics.Device;
        GraphicsLoadContext = new GraphicsLoadContext(
            graphics.Device,
            graphics.StandardResources,
            graphics.ShaderResources,
            graphics.ShaderSetStore);
        AssetStore = NewAssetStore(graphics);
        Panel = new GamePanel(GraphicsDevice);
        GraphicsSettings = GraphicsSettings.Legacy();
    }

    public GraphicsDevice GraphicsDevice { get; }

    public GraphicsLoadContext GraphicsLoadContext { get; }

    public AssetStore AssetStore { get; }

    public GamePanel Panel { get; }

    public GraphicsSettings GraphicsSettings { get; set; }

    private static AssetStore NewAssetStore(TestGraphicsContext graphics)
    {
        var store = new AssetStore(
            SageGame.CncGenerals,
            new MemoryFileSystem(),
            "english",
            graphics.Device,
            graphics.StandardResources,
            graphics.ShaderResources,
            graphics.ShaderSetStore,
            OnDemandAssetLoadStrategy.None);

        store.PushScope();
        return store;
    }

    public IGameDefinition Definition => null!;

    public SageGame SageGame => SageGame.CncGenerals;

    public Configuration Configuration => null!;

    public ContentManager ContentManager => null!;

    public GraphicsSystem Graphics => null!;

    public GraphicsSettings EffectiveGraphicsSettings => GraphicsSettings;

    public GameOutcome CurrentOutcome => null!;

    public VictoryOptions VictoryOptions { get; set; } = VictoryOptions.Default;

    public CursorManager Cursors => null!;

    public InputMessageBuffer InputMessageBuffer => null!;

    public SkirmishManager SkirmishManager { get; set; } = null!;

    public LobbyManager LobbyManager => null!;

    public List<GameSystem> GameSystems { get; } = [];

    public ScriptingSystem Scripting => null!;

    public LuaScriptEngine Lua { get; set; } = null!;

    public SelectionSystem Selection => null!;

    public OrderGeneratorSystem OrderGenerator => null!;

    public AudioSystem Audio => null!;

    public GameState GameState => null!;

    public GameStateMap GameStateMap => null!;

    public CampaignManager CampaignManager => null!;

    public TerrainLogic TerrainLogic => null!;

    public TerrainVisual TerrainVisual => null!;

    public GhostObjectManager GhostObjectManager => null!;

    public bool IsRunning => false;

    public Action Restart { get; set; } = null!;

    public bool InGame => false;

    public float LogicUpdateScaleFactor { get; set; }

    public bool IsLogicRunning { get; set; }

    public TimeInterval MapTime => TimeInterval.Zero;

    public TimeInterval CurrentGameTime => TimeInterval.Zero;

    public TimeInterval RenderTime => TimeInterval.Zero;

    public TimeSpan CumulativeLogicUpdateError => TimeSpan.Zero;

    public event EventHandler MatchOutcomeDecided
    {
        add { }
        remove { }
    }

    public string UserDataLeafName => string.Empty;

    public string UserDataFolder => string.Empty;

    public string UserAppDataFolder => string.Empty;

    public Viewport Viewport => default;

    public Scene2D Scene2D => null!;

    public IScene3D Scene3D { get; set; } = null!;

    public NetworkMessageBuffer NetworkMessageBuffer { get; set; } = null!;

    public Texture LauncherImage => null!;

    public GameLogic GameLogic => null!;

    public GameClient GameClient => null!;

    public VictorySystem VictorySystem => null!;

    public PlayerManager PlayerManager => null!;

    public TeamFactory TeamFactory => null!;

    public PartitionCellManager PartitionCellManager => null!;

    public IGameEngine GameEngine => null!;

    public event EventHandler<GameUpdatingEventArgs> Updating
    {
        add { }
        remove { }
    }

    public event EventHandler<OrderProcessedEventArgs> OrderProcessed
    {
        add { }
        remove { }
    }

    public event EventHandler<GameObjectCreatedEventArgs> ObjectCreated
    {
        add { }
        remove { }
    }

    public event EventHandler<GameObjectDestroyedEventArgs> ObjectDestroyed
    {
        add { }
        remove { }
    }

    public event EventHandler<GameObjectConstructionCompletedEventArgs> ConstructionCompleted
    {
        add { }
        remove { }
    }

    public event EventHandler RenderCompleted
    {
        add { }
        remove { }
    }

    public void AddAiDecisionSpan(TimeSpan span)
    {
    }

    public TimeSpan ConsumeAiDecisionSpan() => TimeSpan.Zero;

    public void InjectOrders(IEnumerable<Order> orders)
    {
        throw new NotSupportedException();
    }

    public void LoadSaveFile(FileSystemEntry entry) => throw new NotSupportedException();

    public void LoadReplayFile(FileSystemEntry replayFileEntry) => throw new NotSupportedException();

    public void ShowMainMenu() => throw new NotSupportedException();

    public Window LoadWindow(string wndFileName) => throw new NotSupportedException();

    public AptWindow LoadAptWindow(string aptFileName) => throw new NotSupportedException();

    public void StartCampaign(string side) => throw new NotSupportedException();

    public void StartCampaign(string campaignName, string missionName) => throw new NotSupportedException();

    public void StartSkirmishOrMultiPlayerGame(
        string mapFileName,
        IConnection connection,
        PlayerSetting[] playerSettings,
        int seed,
        bool isMultiPlayer) => throw new NotSupportedException();

    public void StartSinglePlayerGame(string mapFileName) => throw new NotSupportedException();

    public void EndGame() => throw new NotSupportedException();

    public void StartRun() => throw new NotSupportedException();

    public void Update(IEnumerable<InputMessage> messages) => throw new NotSupportedException();

    public void Step() => throw new NotSupportedException();

    public void Render() => throw new NotSupportedException();

    public void Dispose()
    {
    }

    public Vector2 GetTopLeftUV() => default;

    public Vector2 GetBottomRightUV() => default;

    public IEnumerable<PlayerTemplate> GetPlayableSides() => [];

    public MappedImage GetMappedImage(string name) => null!;

    public void Exit() => throw new NotSupportedException();
}
