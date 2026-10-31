namespace Panopticon.Geospatial.Interchange;

/// <summary>Point geometry.</summary>
public sealed record GeoPointGeometry(GeoPosition Position) : GeoGeometry;
