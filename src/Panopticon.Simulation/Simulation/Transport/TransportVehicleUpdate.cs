#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Simulation.Transport;

/// <summary>
/// Module data for <see cref="TransportVehicleUpdate"/>: the owning domain, the node route,
/// and the speed in world units per logic frame. Constructed in code by the graph scenario;
/// there is no INI parse path.
/// </summary>
public sealed class TransportVehicleUpdateModuleData : UpdateModuleData
{
    public TransportGraphDomainModel? Domain { get; set; }

    public IReadOnlyList<int> Route { get; set; } = [];

    public float Speed { get; set; } = 10f;

    internal override TransportVehicleUpdate CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new TransportVehicleUpdate(gameObject, gameEngine, this);
    }
}

/// <summary>
/// Moves a vehicle <see cref="GameObject"/> along its route edge by edge at a fixed speed,
/// reporting node arrivals to the owning <see cref="TransportGraphDomainModel"/> (which
/// applies the owner-authority rule and updates boundary metrics exactly once per crossing).
/// </summary>
public sealed class TransportVehicleUpdate : UpdateModule
{
    private readonly TransportVehicleUpdateModuleData _moduleData;

    private int _routeIndex;
    private float _segmentLength;
    private bool _finished;

    internal bool RouteComplete => _finished;

    internal float EdgeProgress { get; private set; }

    /// <summary>The route this vehicle follows, as node indices.</summary>
    internal IReadOnlyList<int> Route => _moduleData.Route;

    /// <summary>The edge the vehicle is currently traversing ("edge:u->v").</summary>
    internal string CurrentEdgeKey =>
        $"edge:{_moduleData.Route[_routeIndex - 1]}->{_moduleData.Route[_routeIndex]}";

    public TransportVehicleUpdate(GameObject gameObject, IGameEngine gameEngine, TransportVehicleUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;
        _routeIndex = 1;
        _segmentLength = SegmentLength(_routeIndex);
    }

    private float SegmentLength(int routeIndex)
    {
        var domain = _moduleData.Domain!;
        var from = domain.GetNodePosition(_moduleData.Route[routeIndex - 1]);
        var to = domain.GetNodePosition(_moduleData.Route[routeIndex]);
        var delta = to - from;
        return MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
    }

    public override UpdateSleepTime Update()
    {
        if (_finished)
        {
            return UpdateSleepTime.Forever;
        }

        var domain = _moduleData.Domain!;
        var speed = _moduleData.Speed;
        var target = domain.GetNodePosition(_moduleData.Route[_routeIndex]);
        var position = GameObject.Translation;
        var delta = target - position;
        var distance = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

        if (distance <= speed)
        {
            GameObject.UpdateTransform(target, null);
            ArriveAtTargetNode(domain);
            return UpdateSleepTime.None;
        }

        var step = delta * (speed / distance);
        GameObject.UpdateTransform(position + step, null);
        EdgeProgress = 1f - (distance / _segmentLength);
        return UpdateSleepTime.None;
    }

    private void ArriveAtTargetNode(TransportGraphDomainModel domain)
    {
        var fromNode = _moduleData.Route[_routeIndex - 1];
        var toNode = _moduleData.Route[_routeIndex];

        // Advance module state first so the domain reads fresh route state when notified.
        if (_routeIndex >= _moduleData.Route.Count - 1)
        {
            _finished = true;
            EdgeProgress = 1f;
        }
        else
        {
            _routeIndex++;
            _segmentLength = SegmentLength(_routeIndex);
            EdgeProgress = 0f;
        }

        domain.NotifyNodeArrived(this, fromNode, toNode);
    }
}
