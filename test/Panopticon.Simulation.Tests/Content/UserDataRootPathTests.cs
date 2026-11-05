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
    private const string SandboxMapRelativePath = @"Maps/userdatarootproof/userdatarootproof.map";

    // UserMapCache writes Path.Combine(root, @"Maps\MapCache.ini") verbatim; on Linux that
    // is a single file with a backslash in its name, not the Maps directory. Asserting the
    // same construction stays faithful to the engine's write path on every platform.
    private static string MapCacheIniPath(string root) => Path.Combine(root, @"Maps\MapCache.ini");

    // The operator's real Generals user-data folder; the sandbox tests must leave it untouched.
    private static string RealUserDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Command and Conquer Generals Data");

    private static string CreateSandboxRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "opencode", "user-data-root-tests", Guid.NewGuid().ToString("N"));

        // A renamed copy of a real install map: the name exists nowhere in the install,
        // so a successful resolution proves it came from the sandbox root, not the archives.
        var mapDirectory = Path.Combine(root, "Maps", "userdatarootproof");
        Directory.CreateDirectory(mapDirectory);

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var fileSystem = installation.CreateFileSystem();
        var mapEntry = fileSystem.GetFile("maps/alpine assault/alpine assault.map")
            ?? throw new FileNotFoundException("Test setup: alpine assault was not found in the install.");
        using (var input = mapEntry.Open())
        using (var output = File.Create(Path.Combine(mapDirectory, "userdatarootproof.map")))
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
        var root = CreateSandboxRoot();
        try
        {
            var realMapCache = MapCacheIniPath(RealUserDataFolder);
            var realFolderExistedBefore = Directory.Exists(RealUserDataFolder);
            var realMapCacheWriteBefore = File.Exists(realMapCache)
                ? File.GetLastWriteTimeUtc(realMapCache)
                : (DateTime?)null;

            using (var game = CreateGameWithRoot(root))
            {
                var sandboxMapCache = MapCacheIniPath(root);
                Assert.True(File.Exists(sandboxMapCache), $"Expected the map cache inside the sandbox root: {sandboxMapCache}");
                Assert.Contains("userdatarootproof", File.ReadAllText(sandboxMapCache));
            }

            Assert.Equal(realFolderExistedBefore, Directory.Exists(RealUserDataFolder));
            var realMapCacheWriteAfter = File.Exists(realMapCache)
                ? File.GetLastWriteTimeUtc(realMapCache)
                : (DateTime?)null;
            Assert.Equal(realMapCacheWriteBefore, realMapCacheWriteAfter);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Game_WithUserDataRootPath_StartsSandboxMapAndRoutesAllUserData()
    {
        var root = CreateSandboxRoot();
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
                SandboxMapRelativePath,
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
