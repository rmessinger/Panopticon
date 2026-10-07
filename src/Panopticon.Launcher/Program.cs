using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CommandLine;
using NLog;
using NLog.Targets;
using Panopticon.Data;
using Panopticon.Diagnostics;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Mods.BuiltIn;
using Panopticon.Network;
using Veldrid;

namespace Panopticon.Launcher;

public static class Program
{
    public sealed class Options
    {
        [Option('r', "renderer", Default = null, Required = false, HelpText = "Set the renderer backend (Direct3D11,Vulkan,OpenGL,Metal,OpenGLES).")]
        public GraphicsBackend? Renderer { get; set; }

        [Option("noshellmap", Default = false, Required = false, HelpText = "Disables loading the shell map, speeding up startup time.")]
        public bool NoShellmap { get; set; }

        [Option('g', "game", Default = SageGame.CncGenerals, Required = false, HelpText = "Chooses which game to start.")]
        public SageGame Game { get; set; }

        [Option('m', "map", Required = false, HelpText = "Immediately starts a new skirmish in the specified map. Use with --players to configure opponents.")]
        public string? Map { get; set; }

        [Option("players", Required = false, Separator = '|',
            HelpText = "Define players for the skirmish started via --map. Each entry is a '|'-separated player token, " +
                       "where each token is a comma-separated list of key:value pairs. " +
                       "Supported keys: faction (e.g. FactionAmerica), owner (Player|EasyAi|MediumAi|HardAi), " +
                       "color (R.G.B, each 0-255), pos (start position 1-8), team (integer). " +
                       "Example: --players \"faction:FactionAmerica,owner:Player,color:255.0.0,pos:1,team:0|faction:FactionGLA,owner:HardAi,color:0.255.0,pos:2,team:0\"")]
        public IEnumerable<string> Players { get; set; } = Enumerable.Empty<string>();

        [Option("novsync", Default = false, Required = false, HelpText = "Disable vsync.")]
        public bool DisableVsync { get; set; }

        [Option('f', "fullscreen", Default = false, Required = false, HelpText = "Enable fullscreen mode.")]
        public bool Fullscreen { get; set; }

        [Option('d', "renderdoc", Default = false, Required = false, HelpText = "Enable renderdoc debugging.")]
        public bool RenderDoc { get; set; }

        [Option("developermode", Default = false, Required = false, HelpText = "Enable developer mode.")]
        public bool DeveloperMode { get; set; }

        [Option("tracefile", Default = null, Required = false, HelpText = "Generate trace output to the specified path, for example `--tracefile trace.json`. Trace files can be loaded into Chrome's tracing GUI at chrome://tracing")]
        public string? TraceFile { get; set; }

        [Option("replay", Default = null, Required = false, HelpText = "Specify a replay file to immediately start replaying")]
        public string? ReplayFile { get; set; }

        [Option("save", Default = null, Required = false, HelpText = "Specify a save file to immediately load")]
        public string? SaveFile { get; set; }

        [Option('p', "gamepath", Default = null, Required = false, HelpText = "Force game to use this gamepath")]
        public string? GamePath { get; set; }

        [Option('b', "basegamepath", Default = null, Required = false, HelpText = "Force the game's base game to use this gamepath")]
        public string? BaseGamePath { get; set; }

        [Option('u', "uniqueports", Default = false, Required = false, HelpText = "Use a unique port for each client in a multiplayer game. Normally, port 8088 is used, but when we want to run multiple game instances on the same machine (for debugging purposes), each client needs a different port.")]
        public bool UseUniquePorts { get; set; }

        [Option("noaudio", Default = false, Required = false, HelpText = "Disable all audio.")]
        public bool NoAudio { get; set; }

        [Option("savereplay", Default = null, Required = false, HelpText = "Record the skirmish started via --map and save it to the specified filename inside the game's Replays folder (e.g. --savereplay mymatch.rep).")]
        public string? SaveReplay { get; set; }
    }

    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        LogManager.Setup().SetupExtensions(b => b.RegisterTarget<Core.InternalLogger>("Panopticon"));

        Parser.Default.ParseArguments<Options>(args)
          .WithParsed(opts => Run(opts));
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static readonly PlayerSetting[] DefaultSkirmishPlayers =
    [
        new(1, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, isLocalForMultiplayer: true),
        new(2, "FactionGLA",     new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi),
    ];

    /// <summary>
    /// Parses a single --players token such as:
    ///   faction:FactionAmerica,owner:Player,color:255.0.0,pos:1,team:0
    /// </summary>
    private static PlayerSetting ParsePlayerSetting(string token, int fallbackPosition)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in token.Split(','))
        {
            var idx = part.IndexOf(':');
            if (idx > 0)
                dict[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }

        var faction = dict.TryGetValue("faction", out var f) ? f : "FactionAmerica";

        var owner = PlayerOwner.EasyAi;
        if (dict.TryGetValue("owner", out var o))
        {
            owner = o.ToLowerInvariant() switch
            {
                "player" => PlayerOwner.Player,
                "easyai" => PlayerOwner.EasyAi,
                "mediumai" => PlayerOwner.MediumAi,
                "hardai" => PlayerOwner.HardAi,
                _ => PlayerOwner.EasyAi,
            };
        }

        var color = new ColorRgb(255, 255, 255);
        if (dict.TryGetValue("color", out var c))
        {
            var rgb = c.Split('.');
            if (rgb.Length == 3
                && byte.TryParse(rgb[0], out var r)
                && byte.TryParse(rgb[1], out var g)
                && byte.TryParse(rgb[2], out var b))
            {
                color = new ColorRgb(r, g, b);
            }
        }

        int? pos = dict.TryGetValue("pos", out var posStr) && int.TryParse(posStr, out var posVal)
            ? posVal
            : fallbackPosition;

        int team = dict.TryGetValue("team", out var teamStr) && int.TryParse(teamStr, out var teamVal)
            ? teamVal
            : 0;

        return new PlayerSetting(pos, faction, color, team, owner, isLocalForMultiplayer: owner == PlayerOwner.Player);
    }

    private static GameInstallation? GameFromPath(Options opts, SageGame game, string? path)
    {
        var UseLocators = true;

        path ??= Environment.CurrentDirectory;

        foreach (var gameDef in GameDefinition.All)
        {
            if (gameDef.Probe(path))
            {
                game = gameDef.Game;
                UseLocators = false;
            }
        }

        var definition = GameDefinition.FromGame(game);
        if (UseLocators)
        {
            return GameInstallation
                .FindAll(new[] { definition })
                .FirstOrDefault();
        }

        var baseGame = definition.BaseGame != null
            ? GameFromPath(opts, definition.BaseGame.Game, opts.BaseGamePath) // we shouldn't ever have more than one base game
            : null;
        return new GameInstallation(definition, path, baseGame);
    }

    public static void Run(Options opts)
    {
        Logger.Info("Starting...");

        var installation = GameFromPath(opts, opts.Game, opts.GamePath);

        if (installation == null)
        {
            var definition = GameDefinition.FromGame(opts.Game);
            Console.WriteLine($"Panopticon was unable to find any installations of {definition.DisplayName}.\n");

            Console.WriteLine("You can manually specify the installation path by setting the following environment variable:");
            Console.WriteLine($"\t{definition.Identifier.ToUpper()}_PATH=<installation path>\n");

            Console.WriteLine("Panopticon doesn't yet detect every released version of every game. Please report undetected versions to our GitHub page:");
            Console.WriteLine("\thttps://github.com/Panopticon/Panopticon/issues");

            Console.WriteLine("\n\n Press any key to exit.");

            Console.ReadLine();

            Environment.Exit(1);
        }

        Logger.Debug($"Have installation of {installation.Game.DisplayName}");

        Platform.Start();

        var traceEnabled = !string.IsNullOrEmpty(opts.TraceFile);
        if (traceEnabled)
        {
            GameTrace.Start(opts.TraceFile);
        }

        // TODO: Read game version from assembly metadata or .git folder
        // TODO: Set window icon.
        var config = new Configuration()
        {
            UseRenderDoc = opts.RenderDoc,
            LoadShellMap = !opts.NoShellmap,
            UseUniquePorts = opts.UseUniquePorts,
            NoAudio = opts.NoAudio,
            SaveReplayFile = opts.SaveReplay
        };

        UPnP.InitializeAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => Logger.Info($"UPnP status: {UPnP.Status}"));

        Logger.Debug($"Have configuration");

        using (var window = new GameWindow($"Panopticon - {installation.Game.DisplayName} - master", 100, 100, 1024, 768, opts.Fullscreen))
        using (var game = new Game(installation, opts.Renderer, config, window))
        using (var textureCopier = new TextureCopier(game, window.Swapchain.Framebuffer.OutputDescription))
        using (var developerModeView = new DeveloperModeView(game, window))
        {
            game.GraphicsDevice.SyncToVerticalBlank = !opts.DisableVsync;

            var developerModeEnabled = opts.DeveloperMode;

            if (opts.DeveloperMode)
            {
                window.Maximized = true;
            }

            if (opts.ReplayFile != null)
            {
                var replayFile = game.ContentManager.UserDataFileSystem?.GetFile(Path.Combine("Replays", opts.ReplayFile));
                if (replayFile == null)
                {
                    Logger.Debug("Could not find entry for Replay " + opts.ReplayFile);
                    game.ShowMainMenu();
                }

                game.LoadReplayFile(replayFile);
            }
            else if (opts.SaveFile != null)
            {
                var saveFile = game.ContentManager.UserDataFileSystem?.GetFile(Path.Combine("Save", opts.SaveFile));
                if (saveFile == null)
                {
                    Logger.Debug("Could not find entry for Save " + opts.SaveFile);
                    game.ShowMainMenu();
                }

                game.LoadSaveFile(saveFile);
            }
            else if (opts.Map != null)
            {
                game.Restart = StartMap;
                StartMap();

                void StartMap()
                {
                    var mapCache = game.AssetStore.MapCaches.GetByName(opts.Map);
                    if (mapCache == null)
                    {
                        Logger.Warn("Could not find MapCache entry for map " + opts.Map);
                        game.ShowMainMenu();
                    }
                    else if (mapCache.IsMultiplayer)
                    {
                        var playerTokens = opts.Players.ToList();
                        var pSettings = playerTokens.Count > 0
                            ? playerTokens.Select((t, i) => ParsePlayerSetting(t, i + 1)).ToArray()
                            : DefaultSkirmishPlayers;

                        Logger.Debug($"Starting skirmish on {opts.Map} with {pSettings.Length} players");

                        game.StartSkirmishOrMultiPlayerGame(opts.Map,
                            new EchoConnection(),
                            pSettings,
                            Environment.TickCount,
                            false);
                    }
                    else
                    {
                        Logger.Debug("Starting singleplayer game");

                        game.StartSinglePlayerGame(opts.Map);
                    }
                }
            }
            else
            {
                Logger.Debug("Showing main menu");
                game.ShowMainMenu();
            }

            Logger.Debug("Starting game");

            game.StartRun();

            while (game.IsRunning)
            {
                if (!window.PumpEvents())
                {
                    break;
                }

                // Handle window-level shortcuts before routing input to game or developer UI,
                // so they work regardless of whether the game view is focused.
                foreach (var message in window.MessageQueue)
                {
                    if (message.MessageType != InputMessageType.KeyDown)
                        continue;

                    if (message.Value.Key == Key.Enter && (message.Value.Modifiers & ModifierKeys.Alt) != 0)
                    {
                        window.Fullscreen = !window.Fullscreen;
                    }
                    else if (message.Value.Key == Key.D && (message.Value.Modifiers & ModifierKeys.Alt) != 0)
                    {
                        developerModeEnabled = !developerModeEnabled;
                    }
                }

                if (developerModeEnabled)
                {
                    developerModeView.Tick();
                }
                else
                {
                    game.Update(window.MessageQueue);

                    game.Panel.EnsureFrame(window.DrawableBounds);

                    game.Render();

                    textureCopier.Execute(
                        game.Panel.Framebuffer.ColorTargets[0].Target,
                        window.Swapchain.Framebuffer);
                }

                window.MessageQueue.Clear();

                game.GraphicsDevice.SwapBuffers(window.Swapchain);
            }
        }

        if (traceEnabled)
        {
            GameTrace.Stop();
        }

        Platform.Stop();
    }
}
