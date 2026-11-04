using System;
using System.IO;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Content;

// Game boots run serially with all other game-booting classes (headless device and
// content parsing are process-wide state); the sandboxed user-data root itself
// removes the shared user-data MapCache.ini contention these boots otherwise cause.
[Collection("SerialGameTests")]
public sealed class UserDataRootPathTests
{
    // UserMapCache writes Path.Combine(root, @"Maps\MapCache.ini") verbatim; on Linux that
    // is a single file with a backslash in its name, not the Maps directory. Asserting the
    // same construction stays faithful to the engine's write path on every platform.
    private static string MapCacheIniPath(string root) => Path.Combine(root, @"Maps\MapCache.ini");

    // The operator's real Generals user-data folder; the sandbox tests must leave it untouched.
    private static string RealUserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Command and Conquer Generals Data");

    private static string ReadTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string CreateSandboxRoot(out string mapMarker, out string sandboxMapRelativePath)
    {
        var runId = Guid.NewGuid().ToString("N");
        mapMarker = "userdatarootproof" + runId;
        sandboxMapRelativePath = $"Maps/{mapMarker}/{mapMarker}.map";
        var root = Path.Combine(Path.GetTempPath(), "opencode", "user-data-root-tests", runId);

        // Unique marker: absent from the install and the default user-data folder.
        var mapDirectory = Path.Combine(root, "Maps", mapMarker);
        Directory.CreateDirectory(mapDirectory);

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var fileSystem = installation.CreateFileSystem();
        var mapEntry = fileSystem.GetFile("maps/alpine assault/alpine assault.map")
            ?? throw new FileNotFoundException("Test setup: alpine assault was not found in the install.");
        using (var input = mapEntry.Open())
        using (var output = File.Create(Path.Combine(mapDirectory, mapMarker + ".map")))
        {
            input.CopyTo(output);
        }

        return root;
    }

    private static Game CreateGameWithRoot(string root)
    {
        return new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = root },
            window: null);
    }

    [Fact]
    public void Configuration_UserDataRootPath_DefaultsToNull()
    {
        Assert.Null(new Configuration().UserDataRootPath);
    }

    [GameFact(SageGame.CncGenerals)]
    public void Game_WithUserDataRootPath_WritesMapCacheInsideRoot()
    {
        var root = CreateSandboxRoot(out var mapMarker, out _);
        try
        {
            using (var game = CreateGameWithRoot(root))
            {
                Assert.Equal(root, game.UserDataFolder);
                Assert.Equal(root, game.UserAppDataFolder);
                Assert.NotNull(game.ContentManager.UserDataFileSystem);
                Assert.Equal(root, game.ContentManager.UserDataFileSystem.RootDirectory);

                var sandboxMapCache = MapCacheIniPath(root);
                Assert.True(File.Exists(sandboxMapCache), $"Expected the map cache inside the sandbox root: {sandboxMapCache}");
                Assert.Contains(mapMarker, ReadTextShared(sandboxMapCache), StringComparison.Ordinal);
            }

            var realMapCache = MapCacheIniPath(RealUserDataFolder);
            if (File.Exists(realMapCache))
            {
                Assert.DoesNotContain(mapMarker, ReadTextShared(realMapCache), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Game_WithUserDataRootPath_StartsSandboxMapAndRoutesAllUserData()
    {
        var root = CreateSandboxRoot(out _, out var sandboxMapRelativePath);
        try
        {
            using var game = CreateGameWithRoot(root);

            Assert.Equal(root, game.UserDataFolder);
            Assert.Equal(root, game.UserAppDataFolder);
            Assert.NotNull(game.ContentManager.UserDataFileSystem);
            Assert.Equal(root, game.ContentManager.UserDataFileSystem.RootDirectory);

            var playerSettings = new PlayerSetting[]
            {
                new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
                new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            };

            game.StartSkirmishOrMultiPlayerGame(
                sandboxMapRelativePath,
                new EchoConnection(),
                playerSettings,
                1,
                isMultiPlayer: false);
            game.StartRun();
            game.EnableFixedLogicTimestep();

            for (var i = 0; i < 5; i++)
            {
                game.Update(Array.Empty<InputMessage>());
            }

            Assert.True(game.InGame, "Skirmish start on the sandbox map did not reach the in-game state.");
            Assert.Equal(5u, game.GameLogic.CurrentFrame.Value);
            Assert.NotNull(game.Scene3D.Terrain);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Game_WithMissingUserDataRoot_Throws()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "opencode", "user-data-root-tests", Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<DirectoryNotFoundException>(
            () => CreateGameWithRoot(missingRoot));

        Assert.Contains(missingRoot, exception.Message);
    }

    [GameFact(SageGame.CncGenerals)]
    public void Game_WithoutUserDataRootPath_KeepsOsRouting()
    {
        using var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true },
            window: null);

        Assert.Equal(RealUserDataFolder, game.UserDataFolder);
    }
}
