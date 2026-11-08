#nullable enable

using System.Numerics;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One river center-line segment.</summary>
public sealed record MapRiverLineRecord(Vector2 Start, Vector2 End);
