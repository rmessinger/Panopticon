using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Content;
using OpenSage.Utilities.Extensions;

namespace OpenSage.Logic;

public sealed class PlayerManager : IPersistableObject
{
    private readonly IGame _game;

    public IReadOnlyList<Player> Players => _players;
    private Player[] _players;

    public Player LocalPlayer { get; private set; }

    internal PlayerManager(IGame game)
    {
        _game = game;
        _players = Array.Empty<Player>();
    }

    internal void OnNewGame(Data.Map.Player[] mapPlayers, GameType gameType)
        => OnNewGame(mapPlayers, gameType, null);

    internal void OnNewGame(Data.Map.Player[] mapPlayers, GameType gameType, PlayerSetting[] playerSettings)
    {
        _players = CreatePlayers(mapPlayers, gameType).ToArray();

        if (playerSettings != null)
        {
            for (var i = 0; i < playerSettings.Length && i + 2 < _players.Length; i++)
            {
                _players[i + 2].Team = playerSettings[i].Team;
            }
        }

        LocalPlayer = null;

        if (playerSettings != null)
        {
            var localPlayerIndex = Array.FindIndex(playerSettings, setting => setting.IsLocalForMultiplayer);
            if (localPlayerIndex >= 0 && localPlayerIndex + 2 < _players.Length)
            {
                LocalPlayer = _players[localPlayerIndex + 2];
            }
        }

        if (LocalPlayer == null)
        {
            foreach (var player in _players)
            {
                if (player.IsHuman)
                {
                    LocalPlayer = player;
                    break;
                }
            }
        }

        if (LocalPlayer == null && _players.Length > 2)
        {
            LocalPlayer = _players[2];
        }

        if (playerSettings != null)
        {
            var skirmishPlayers = _players.Skip(2).Take(playerSettings.Length).ToArray();
            for (var i = 0; i < skirmishPlayers.Length; i++)
            {
                for (var j = i + 1; j < skirmishPlayers.Length; j++)
                {
                    var left = skirmishPlayers[i];
                    var right = skirmishPlayers[j];

                    if (left.Team == right.Team && left.Team != -1)
                    {
                        left.AddAlly(right);
                    }
                    else
                    {
                        left.AddEnemy(right);
                    }
                }
            }
        }
    }

    // This needs to operate on the entire player list, because players have references to each other
    // (allies and enemies).
    private IEnumerable<Player> CreatePlayers(Data.Map.Player[] mapPlayers, GameType gameType)
    {
        var players = new Dictionary<string, Player>();
        var allies = new Dictionary<string, string[]>();
        var enemies = new Dictionary<string, string[]>();

        var id = 0u;
        foreach (var mapPlayer in mapPlayers)
        {
            var player = Player.FromMapData(id++, mapPlayer, _game, gameType != GameType.SinglePlayer);
            players[player.Name] = player;
            allies[player.Name] =
                mapPlayer.Allies?.Split(' ')
                .Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? []; // Neutral has a player name of "", so it's important not to add empty strings
            enemies[player.Name] =
                mapPlayer.Enemies?.Split(' ')
                .Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? []; // Neutral has a player name of "", so it's important not to add empty strings
        }

        foreach (var (name, player) in players)
        {
            player.Allies = allies[name].Select(ally => players[ally]).ToSet();
            player.Enemies = enemies[name].Select(enemy => players[enemy]).ToSet();
        }

        return players.Values;
    }

    public Player GetPlayerByName(string name)
    {
        return Array.Find(_players, x => x.Name == name);
    }

    public Player GetPlayerByIndex(uint index)
    {
        return _players[(int)index];
    }

    public int GetPlayerIndex(Player player)
    {
        return Array.IndexOf(_players, player);
    }

    // TODO: Is this right?
    public Player GetCivilianPlayer() => _players[1];

    /// <summary>
    /// Returns the "neutral" player. There is always a player that is "neutral"
    /// with respect to all other players. This is so that everything can be
    /// associated with a non-null player, to simplify the universe.
    /// </summary>
    public Player NeutralPlayer => _players[0];

    internal void LogicTick()
    {
        foreach (var player in _players)
        {
            player.LogicTick();
        }
    }

    public void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.PersistArrayWithUInt32Length(
            _players,
            static (StatePersister persister, ref Player item) =>
            {
                persister.PersistObjectValue(item);
            });
    }
}
