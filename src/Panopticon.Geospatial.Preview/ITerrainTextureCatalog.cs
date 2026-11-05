using System.Collections.Generic;

namespace Panopticon.Geospatial.Preview;

/// <summary>
/// The known terrain texture block names of one installed game (B4b group 5 decision 3).
/// The card 24 CLI supplies the actual <c>Terrain.ini</c> block names of the installed
/// Generals; the preview library never constructs a Game instance and ships no fallback.
/// </summary>
public interface ITerrainTextureCatalog
{
    /// <summary>Actual terrain texture block names, exactly as the game defines them.</summary>
    IReadOnlyCollection<string> TerrainTextureNames { get; }
}
