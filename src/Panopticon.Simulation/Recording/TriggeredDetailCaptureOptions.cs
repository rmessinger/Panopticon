#nullable enable

using System.Collections.Generic;

namespace Panopticon.Recording;

/// <summary>
/// Card WI-A5-21: caller options for T2 triggered detail capture. Null entries keep the
/// <see cref="TriggeredDetailCapture"/> defaults. Region keys, when set, filter captured detail
/// to entities whose stable domain region key (<c>ISimulationDomainModel.TopologyKey</c>) is
/// listed.
/// </summary>
public sealed record TriggeredDetailCaptureOptions(
    int? RingDepth = null,
    int? MaxWindows = null,
    uint? CooldownFrames = null,
    uint? MarkedStartFrame = null,
    uint? MarkedEndFrame = null,
    IReadOnlyList<string>? RegionKeys = null);
