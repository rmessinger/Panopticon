#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed interchange home for PolygonTriggers.</summary>
public sealed record MapPolygonTriggersRecord(
    ushort Version,
    IReadOnlyList<MapPolygonTriggerRecord> Triggers);
