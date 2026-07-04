using System;
using System.Collections.Generic;
using OpenSage.Tests.Data;

namespace OpenSage.Tests;

public class GameFixture : IDisposable
{
    private readonly Dictionary<SageGame, IGame> _games = new();
    private bool _platformStarted = false;

    public GameFixture()
    {
        // Do not call Platform.Start() here. xUnit v2 creates class fixtures even
        // when all tests in the class are skipped (e.g. [Theory(Skip = "...")]). 
        // Calling SDL_Init from an xUnit background thread causes an NSException on
        // macOS. Platform.Start() is deferred to GetGame() so it only runs when a
        // test actually needs a game instance.
    }

    public IGame GetGame(SageGame sageGame)
    {
        if (!_platformStarted)
        {
            Platform.Start();
            _platformStarted = true;
        }

        if (!_games.TryGetValue(sageGame, out var game))
        {
            var installation = InstalledFilesTestData.GetInstallation(sageGame);

            game = new Game(installation);

            _games.Add(sageGame, game);
        }

        return game;
    }

    public void Dispose()
    {
        foreach (var kvp in _games)
        {
            kvp.Value.Dispose();
        }

        Platform.Stop();
    }
}
