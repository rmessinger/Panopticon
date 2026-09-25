using System;
using System.Collections.Generic;
using Panopticon.Tests.Data;

namespace Panopticon.Tests;

public class GameFixture : IDisposable
{
    private readonly Dictionary<SageGame, IGame> _games = new();

    public GameFixture()
    {
    }

    public IGame GetGame(SageGame sageGame)
    {
        if (!_games.TryGetValue(sageGame, out var game))
        {
            var installation = InstalledFilesTestData.GetInstallation(sageGame);

            // Deliberately not calling Platform.Start() (SDL_Init) here: passing a null
            // window makes Game create a headless GraphicsDevice directly via Veldrid
            // (see GraphicsDeviceUtility.CreateGraphicsDevice), with no SDL2/Cocoa
            // dependency at all. Calling Platform.Start() from an xUnit worker thread
            // (not the process's actual main thread) crashes on macOS with
            // "API misuse: setting the main menu on a non-main thread", since SDL2's
            // Cocoa video backend requires AppKit calls to happen on the real main thread.
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
    }
}

