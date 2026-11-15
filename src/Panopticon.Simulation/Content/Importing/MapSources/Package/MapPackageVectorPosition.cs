#nullable enable

using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Planar vector vertex with optional altitude in the package vector sidecar.</summary>
public sealed record MapPackageVectorPosition(double X, double Y, double? AltitudeMeters)
{
    public static MapPackageVectorPosition FromModel(VectorPosition position) =>
        new(position.X, position.Y, position.AltitudeMeters);

    public VectorPosition ToModel() => new(X, Y, AltitudeMeters);
}
