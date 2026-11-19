#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>One generated-building mesh vertex: position and normal in map-local coordinates.</summary>
public readonly record struct BuildingVertex(double X, double Y, double Z, double NormalX, double NormalY, double NormalZ);
