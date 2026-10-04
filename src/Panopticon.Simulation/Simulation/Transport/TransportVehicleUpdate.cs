#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Simulation.Transport;

/// <summary>
/// Module data for <see cref="TransportVehicleUpdate"/>: the owning domain and the speed in
/// world units per logic frame. The per-vehicle route is object state (persisted by the module
/// itself), so one shared template definition serves every vehicle and checkpoints round-trip.
/// Constructed in code by the graph scenario; there is no INI parse path.
/// </summary>
public sealed class TransportVehicleUpdateModuleData : UpdateModuleData
{
    public TransportGraphDomainModel? Domain { get; set; }

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

    // Per-vehicle route state: the module data is a shared template, so the route lives here
    // and persists with the object (WI-A1-24).
    private int[] _route = [];
    private int _routeIndex;
    private float _segmentLength;
    private bool _finished;

    internal bool RouteComplete => _finished;

    internal float EdgeProgress { get; private set; }

    /// <summary>The route this vehicle follows, as node indices.</summary>
    internal IReadOnlyList<int> Route => _route;

    /// <summary>The edge the vehicle is currently traversing ("edge:u->v").</summary>
    internal string CurrentEdgeKey =>
        $"edge:{_route[_routeIndex - 1]}->{_route[_routeIndex]}";

    public TransportVehicleUpdate(GameObject gameObject, IGameEngine gameEngine, TransportVehicleUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;
        _routeIndex = 1;
    }

    /// <summary>
    /// Installs the route after construction. Separate from the constructor because restore
    /// recreates the module with an empty route before its <see cref="Load"/> replays the
    /// persisted one.
    /// </summary>
    internal void InitializeRoute(IReadOnlyList<int> route)
    {
        _route = route.ToArray();
        _routeIndex = 1;
        _segmentLength = SegmentLength(1);
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        var routeCount = (uint)_route.Length;
        reader.PersistUInt32(ref routeCount, "RouteCount");
        if (reader.Mode == StatePersistMode.Read)
        {
            _route = new int[routeCount];
        }

        for (var i = 0; i < routeCount; i++)
        {
            reader.PersistInt32(ref _route[i], "RouteNode");
        }

        reader.PersistInt32(ref _routeIndex, "RouteIndex");
        reader.PersistSingle(ref _segmentLength, "SegmentLength");

        var edgeProgress = EdgeProgress;
        reader.PersistSingle(ref edgeProgress, "EdgeProgress");
        if (reader.Mode == StatePersistMode.Read)
        {
            EdgeProgress = edgeProgress;
        }

        reader.PersistBoolean(ref _finished, "Finished");
    }

    private float SegmentLength(int routeIndex)
    {
        var domain = _moduleData.Domain!;
        var from = domain.GetNodePosition(_route[routeIndex - 1]);
        var to = domain.GetNodePosition(_route[routeIndex]);
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
        var target = domain.GetNodePosition(_route[_routeIndex]);
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
        var fromNode = _route[_routeIndex - 1];
        var toNode = _route[_routeIndex];

        // Advance module state first so the domain reads fresh route state when notified.
        if (_routeIndex >= _route.Length - 1)
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
