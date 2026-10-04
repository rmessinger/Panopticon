using System;
using System.IO;
using Panopticon.Network;

namespace Panopticon.Data.Sav;

internal sealed class GameStateMap : IPersistableObject
{
    private string _mapPath1;
    private string _mapPath2;
    private GameType _gameType;

    public void Persist(StatePersister reader)
    {
        // Live games never initialize these fields (only retail-save loads do),
        // so writing a checkpoint must source them from the running Game
        // (WI-A1-13).
        if (reader.Mode == StatePersistMode.Write)
        {
            var liveGame = reader.Game as Game
                ?? throw new InvalidOperationException("Cannot write a GameStateMap chunk without a running game.");
            _mapPath1 = liveGame.CurrentMapPath
                ?? throw new InvalidOperationException("Cannot write a GameStateMap chunk before a map is loaded.");
            _mapPath2 = _mapPath1;
            _gameType = liveGame.CurrentGameType;
        }

        reader.PersistVersion(2);

        reader.PersistAsciiString(ref _mapPath1);
        reader.PersistAsciiString(ref _mapPath2);
        reader.PersistEnum(ref _gameType);

        var mapSize = reader.BeginSegment("EmbeddedMap");

        if (reader.SageGame >= SageGame.Bfme)
        {
            var unknown4 = 0u;
            reader.PersistUInt32(ref unknown4);

            var unknown5 = 0u;
            reader.PersistUInt32(ref unknown5);

            mapSize -= 8;
        }

        var game = reader.Game;

        byte[] mapBytes;
        if (reader.Mode == StatePersistMode.Read)
        {
            mapBytes = new byte[mapSize];
        }
        else
        {
            // Write mode embeds the map archive entry, not a previously
            // extracted user-data copy (which live runs never create).
            var mapEntry = game.ContentManager.GetMapEntry(_mapPath1)
                ?? throw new FileNotFoundException($"Map \"{_mapPath1}\" was not found in the content file system.");
            using var mapStream = mapEntry.Open();
            using var buffer = new MemoryStream();
            mapStream.CopyTo(buffer);
            mapBytes = buffer.ToArray();
        }

        reader.PersistSpan(mapBytes);

        // Retail saves extract the embedded map into the user data folder
        // before starting the game from it. Checkpoint restores land in an
        // already-running game and skip that side effect.
        if (reader.Mode == StatePersistMode.Read && !game.InGame)
        {
            var mapPathInSaveFolder = Path.Combine(
                game.ContentManager.UserDataFileSystem.RootDirectory,
                _mapPath1);
            var saveFolder = Path.GetDirectoryName(mapPathInSaveFolder);
            if (!Directory.Exists(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
            }

            File.WriteAllBytes(mapPathInSaveFolder, mapBytes);
        }

        reader.EndSegment();

        reader.PersistUInt32(ref reader.Game.GameLogic.NextObjectId);
        reader.PersistUInt32(ref reader.Game.GameClient.NextDrawableId);

        if (reader.SageGame >= SageGame.Bfme)
        {
            var unknown6 = false;
            reader.PersistBoolean(ref unknown6);
        }

        if (_gameType == GameType.Skirmish)
        {
            // The skirmish settings record is part of the stream in every
            // mode; only the restart side effects are conditional.
            if (reader.Mode == StatePersistMode.Read)
            {
                var manager = new LocalSkirmishManager(game);
                reader.PersistObject(manager.Settings, "SkirmishGameSettings");

                if (!game.InGame)
                {
                    game.SkirmishManager = manager;

                    manager.Settings.MapName = _mapPath1;

                    manager.HandleStartButtonClickAsync().Wait();

                    manager.StartGame();

                    game.PartitionCellManager.OnNewGame();
                }
            }
            else
            {
                var settings = game.SkirmishManager?.Settings ?? new SkirmishGameSettings(isHost: true);
                reader.PersistObject(settings, "SkirmishGameSettings");
            }
        }
        else if (reader.Mode == StatePersistMode.Read && !game.InGame)
        {
            game.StartSinglePlayerGame(_mapPath1);

            game.PartitionCellManager.OnNewGame();
        }
    }
}
