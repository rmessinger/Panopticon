#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>A planar vector coordinate with an optional altitude in meters.</summary>
public readonly record struct VectorPosition(double X, double Y, double? AltitudeMeters);
