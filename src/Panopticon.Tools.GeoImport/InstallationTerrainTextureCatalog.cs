using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content;
using Panopticon.Data;
using Panopticon.Geospatial.Preview;
using Panopticon.Mods.BuiltIn;

namespace Panopticon.Tools.GeoImport;

/// <summary>
/// The real terrain texture catalog of the first installed Generals, loaded data-only
/// through the accepted <see cref="TerrainTextureCatalog"/>; no Game instance is created.
/// Used only by the preview verb.
/// </summary>
internal sealed class InstallationTerrainTextureCatalog : ITerrainTextureCatalog
{
    private readonly IReadOnlyList<string> _names;

    private InstallationTerrainTextureCatalog(IReadOnlyList<string> names)
    {
        _names = names;
    }

    public IReadOnlyCollection<string> TerrainTextureNames => _names;

    public static InstallationTerrainTextureCatalog FromFirstInstalledGenerals()
    {
        var definition = GameDefinition.FromGame(SageGame.CncGenerals);
        var installation = InstallationLocators.FindAllInstallations(definition).FirstOrDefault()
            ?? throw new InvalidOperationException("No installed Generals installation was found for the terrain texture catalog.");

        return new InstallationTerrainTextureCatalog(TerrainTextureCatalog.LoadNames(installation));
    }
}
