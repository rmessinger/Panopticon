#nullable enable

using System.Collections.Generic;
using OpenSage.Logic.Object;

namespace OpenSage.Simulation.Transport;

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
