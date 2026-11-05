using System;
using Panopticon.Geospatial.Preview;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// Optional injection points of the GeoImport CLI; the default terrain catalog resolver
/// runs only in the preview verb, so import and report never probe an installation.
/// </summary>
public sealed class GeoImportCliOptions
{
    /// <summary>Supplies the known terrain texture names; when null, the real installed Generals catalog is loaded.</summary>
    public Func<ITerrainTextureCatalog>? TerrainTextureCatalogResolver { get; init; }

    /// <summary>Per-instance test seam handed the publication transaction before it publishes; no production use.</summary>
    internal Action<PreviewMapTransaction>? ConfigureTransaction { get; init; }
}
