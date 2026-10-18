#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Recording;

namespace Panopticon.Simulation;

/// <summary>
/// The reference A4-1 domain adapter: the RTS world behind the domain contracts, reproducing
/// the pre-seam recording projections exactly (same entity iteration order, same spatial
/// cell bucketing, same T0 metric rows, initiator interaction authority).
/// </summary>
public sealed class RtsSimulationDomainModel : ISimulationDomainModel
{
    private readonly IGameEngine _gameEngine;

    /// <summary>Registry id of the RTS domain adapter (WI-A5-03 scenario domain selection).</summary>
    public const string DefaultDomainId = "rts";

    public string DomainId => DefaultDomainId;

    public DomainFidelityPolicy Fidelity => DomainFidelityPolicy.Identity;

    /// <summary>Spatial bucket edge length for <see cref="TopologyKey"/> (world units).</summary>
    public float CellSize { get; init; } = SignatureTreeBuilder.DefaultCellSize;

    public RtsSimulationDomainModel(IGameEngine gameEngine)
    {
        _gameEngine = gameEngine;
    }

    public void Initialize(SimulationHost host)
    {
    }

    public void RunStarting()
    {
    }

    public void RunComplete(in SimulationHostRunResult result)
    {
    }

    public IReadOnlyList<DomainEntitySummary> CaptureEntities()
    {
        var playerManager = _gameEngine.Game.PlayerManager;
        var entities = new List<DomainEntitySummary>();

        foreach (var gameObject in _gameEngine.GameLogic.Objects)
        {
            entities.Add(new DomainEntitySummary(
                gameObject.Id.Index,
                gameObject.Definition.Name,
                playerManager.GetPlayerIndex(gameObject.Owner),
                gameObject.Transform.Translation,
                gameObject.BodyModule?.Health ?? 0f,
                gameObject.BodyModule?.MaxHealth ?? 0f));
        }

        return entities;
    }

    public IReadOnlyList<uint> CapturePlayerMoneyVector()
    {
        var money = new List<uint>();
        foreach (var player in _gameEngine.Game.PlayerManager.Players)
        {
            money.Add(player.BankAccount.Money);
        }

        return money;
    }

    public string TopologyKey(in DomainEntitySummary entity)
    {
        var cellX = (int)MathF.Floor(entity.Position.X / CellSize);
        var cellY = (int)MathF.Floor(entity.Position.Y / CellSize);
        return $"cell:{cellX},{cellY}";
    }

    public DomainTopologyDescription DescribeTopology() => new([], []);

    public DomainMetricsSnapshot SampleMetrics()
    {
        var snapshot = new DomainMetricsSnapshot();
        foreach (var player in _gameEngine.Game.PlayerManager.Players)
        {
            snapshot.Players.Add(new DomainPlayerMetrics(
                player.DisplayName ?? player.Name ?? string.Empty,
                player.BankAccount.Money,
                player.OwnedObjects.Count));
        }

        return snapshot;
    }

    public uint ResolveInteractionAuthority(in DomainInteraction interaction) => interaction.InitiatorId;
}
