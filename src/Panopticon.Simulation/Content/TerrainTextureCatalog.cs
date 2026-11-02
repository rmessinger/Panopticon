using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Data;
using Panopticon.Data.Ini;
using Panopticon.IO;

namespace Panopticon.Content;

/// <summary>
/// Data-only access to the TerrainTexture block names declared by an installation's
/// <c>Data/INI/Terrain.ini</c>. Parsing runs through the data-only asset pipeline (null
/// graphics parameters, <see cref="OnDemandAssetLoadStrategy.None"/>), so no Game
/// instance and no graphics device are created.
/// </summary>
public static class TerrainTextureCatalog
{
    private const string TerrainIniPath = "Data/INI/Terrain.ini";

    /// <summary>
    /// Loads the canonical TerrainTexture block names of the installation. The returned
    /// list is Ordinal-sorted, duplicate-free, and never modified after being returned.
    /// Throws <see cref="FileNotFoundException"/> when the installation has no
    /// <c>Data/INI/Terrain.ini</c>.
    /// </summary>
    public static IReadOnlyList<string> LoadNames(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        using var fileSystem = installation.CreateFileSystem();
        var terrainIni = fileSystem.GetFile(TerrainIniPath)
            ?? throw new FileNotFoundException($"TerrainTextureCatalog requires {TerrainIniPath} in the installation.");

        var assetStore = new AssetStore(
            installation.Game.Game,
            fileSystem,
            language: null,
            graphicsDevice: null,
            standardGraphicsResources: null,
            shaderResources: null,
            shaderSetStore: null,
            OnDemandAssetLoadStrategy.None);

        assetStore.PushScope();
        try
        {
            new IniParser(terrainIni, assetStore, installation.Game.Game, new IniDataContext(), Encoding.ASCII).ParseFile();

            return Array.AsReadOnly(
                assetStore.TerrainTextures
                    .Select(texture => texture.Name)
                    .Distinct()
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            assetStore.PopScope();
        }
    }
}
