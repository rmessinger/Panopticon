#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.IO;

namespace Panopticon.Content.Importing.MapSources;

/// <summary>
/// Public converter boundary for H3 map packages and the editor's MapFile model.
/// Re-serializes live sections for package save and reads package interchange into resident MapFile.
/// </summary>
public static class MapPackageConverter
{
    /// <summary>
    /// Converts a validated MapInterchange into an in-memory resident MapFile editor model.
    /// </summary>
    public static MapFile ToMapFile(MapInterchange interchange)
    {
        ArgumentNullException.ThrowIfNull(interchange);
        return MapFile.FromInterchange(interchange);
    }

    /// <summary>
    /// Converts a resident MapFile into a validated MapInterchange by re-serializing live sections.
    /// </summary>
    public static MapInterchange ToInterchange(MapFile mapFile, string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(mapFile);
        return new LegacyMapReader(mapFile, sourceLabel).Read();
    }

    /// <summary>
    /// Opens an H3 map package from a loose directory into a resident MapFile with companions.
    /// Rejects archive-addressed paths with an UnsupportedAddressing diagnostic.
    /// </summary>
    public static MapPackageOpenResult OpenPackage(string packagePath, FileSystemEntry? entry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (entry is not null && entry.FileSystem is not DiskFileSystem)
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.UnsupportedAddressing,
                    $"Package addressing through archive container for '{entry.FilePath}' is unsupported; packages must be loose directories."),
            ]);
        }

        if (packagePath.Contains(".big", StringComparison.OrdinalIgnoreCase))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.UnsupportedAddressing,
                    $"Package addressing through archive container for '{packagePath}' is unsupported; packages must be loose directories."),
            ]);
        }

        string packageDirectory;

        if (File.Exists(packagePath))
        {
            if (Path.GetFileName(packagePath).Equals(MapPackageFormat.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                packageDirectory = Path.GetDirectoryName(Path.GetFullPath(packagePath))!;
            }
            else
            {
                throw new MapImportException(
                [
                    new MapInterchangeDiagnostic(
                        MapPackageDiagnostic.PathInvalid,
                        $"Package path '{packagePath}' points to a file, not a package directory."),
                ]);
            }
        }
        else if (Directory.Exists(packagePath))
        {
            packageDirectory = Path.GetFullPath(packagePath);
        }
        else
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageChunkDiagnostic.FileMissing,
                    $"Package directory '{packagePath}' does not exist."),
            ]);
        }

        var manifestPath = Path.Combine(packageDirectory, MapPackageFormat.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapPackageChunkDiagnostic.FileMissing,
                    $"Package manifest '{manifestPath}' does not exist."),
            ]);
        }

        var reader = new PackageReader(packageDirectory);
        var interchange = reader.Read();
        var mapFile = MapFile.FromInterchange(interchange);

        var mapIniContent = string.Empty;
        var mapStrContent = string.Empty;

        var manifestJson = File.ReadAllText(manifestPath);
        var (manifest, _) = MapPackageManifestJson.Parse(manifestJson);

        if (manifest?.Companions is not null)
        {
            foreach (var companion in manifest.Companions)
            {
                var filePath = Path.Combine(packageDirectory, companion.Path);

                if (File.Exists(filePath))
                {
                    var text = File.ReadAllText(filePath, Encoding.UTF8);

                    if (companion.Name.Equals("map.ini", StringComparison.OrdinalIgnoreCase) ||
                        companion.Path.EndsWith("map.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        mapIniContent = text;
                    }
                    else if (companion.Name.Equals("map.str", StringComparison.OrdinalIgnoreCase) ||
                             companion.Path.EndsWith("map.str", StringComparison.OrdinalIgnoreCase))
                    {
                        mapStrContent = text;
                    }
                }
            }
        }
        else
        {
            var fallbackIni = Path.Combine(packageDirectory, "companions", "map.ini");

            if (File.Exists(fallbackIni))
            {
                mapIniContent = File.ReadAllText(fallbackIni, Encoding.UTF8);
            }

            var fallbackStr = Path.Combine(packageDirectory, "companions", "map.str");

            if (File.Exists(fallbackStr))
            {
                mapStrContent = File.ReadAllText(fallbackStr, Encoding.UTF8);
            }
        }

        var objectTypesByName = new Dictionary<string, AssetReference>(StringComparer.Ordinal);

        if (interchange.Objects?.Objects is not null)
        {
            foreach (var o in interchange.Objects.Objects)
            {
                if (!string.Equals(o.Type.Scope, "legacy", StringComparison.Ordinal))
                {
                    objectTypesByName[o.Type.Name] = o.Type;
                }
            }
        }

        var context = new PackageConversionContext
        {
            Identity = interchange.Identity,
            Provenance = interchange.Provenance,
            SampleSpacing = interchange.Layout.SampleSpacing,
            ObjectTypesByIndex = interchange.Objects?.Objects.Select(o => o.Type).ToArray(),
            ObjectTypesByStableId = interchange.Objects?.Objects.ToDictionary(o => o.StableId, o => o.Type, StringComparer.Ordinal),
            ObjectTypesByName = objectTypesByName,
        };

        return new MapPackageOpenResult(mapFile, mapIniContent, mapStrContent, context);
    }

    /// <summary>
    /// Saves a resident MapFile into an H3 map package loose directory by re-serializing live sections.
    /// </summary>
    public static MapPackageWriteResult SavePackage(
        MapFile mapFile,
        string packageDirectory,
        string? mapIniContent = null,
        string? mapStrContent = null,
        PackageConversionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(mapFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        if (packageDirectory.Contains(".big", StringComparison.OrdinalIgnoreCase))
        {
            throw new MapImportException(
            [
                new MapInterchangeDiagnostic(
                    MapInterchangeDiagnostic.UnsupportedAddressing,
                    $"Package addressing through archive container for '{packageDirectory}' is unsupported; packages must be loose directories."),
            ]);
        }

        var sourceLabel = context?.Identity?.Name ?? Path.GetFileName(packageDirectory);
        var legacyReader = new LegacyMapReader(mapFile, sourceLabel);
        var interchange = legacyReader.Read();

        if (interchange.Objects is not null)
        {
            var updatedObjects = new List<PlacedObject>(interchange.Objects.Objects.Count);

            for (var index = 0; index < interchange.Objects.Objects.Count; index++)
            {
                var obj = interchange.Objects.Objects[index];
                var scope = "legacy";

                if (context?.ObjectTypesByStableId is not null &&
                    context.ObjectTypesByStableId.TryGetValue(obj.StableId, out var scopedType) &&
                    string.Equals(scopedType.Name, obj.Type.Name, StringComparison.Ordinal))
                {
                    scope = scopedType.Scope;
                }
                else if (context?.ObjectTypesByIndex is not null &&
                    index < context.ObjectTypesByIndex.Count &&
                    string.Equals(context.ObjectTypesByIndex[index].Name, obj.Type.Name, StringComparison.Ordinal))
                {
                    scope = context.ObjectTypesByIndex[index].Scope;
                }
                else if (context?.ObjectTypesByName is not null &&
                         context.ObjectTypesByName.TryGetValue(obj.Type.Name, out var scopedByName))
                {
                    scope = scopedByName.Scope;
                }

                updatedObjects.Add(new PlacedObject(
                    obj.StableId,
                    obj.X,
                    obj.Y,
                    obj.Z,
                    obj.OrientationRadians,
                    new AssetReference(scope, obj.Type.Name),
                    obj.RoadType,
                    obj.LegacyProperties));
            }

            interchange = interchange with { Objects = new PlacedObjectSet(updatedObjects) };
        }

        if (context?.SampleSpacing is not null)
        {
            interchange = interchange with
            {
                Layout = interchange.Layout with { SampleSpacing = context.SampleSpacing.Value },
            };
        }

        if (context?.Identity is not null)
        {
            interchange = interchange with
            {
                Identity = context.Identity with { SourceKind = MapSourceKind.H3Package },
            };
        }

        if (context?.Provenance is not null)
        {
            interchange = interchange with
            {
                Provenance = context.Provenance,
            };
        }

        var companions = new List<MapPackageCompanionEntry>();
        var companionPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(mapIniContent))
        {
            var iniBytes = Encoding.UTF8.GetBytes(mapIniContent);
            var iniSha = Convert.ToHexString(SHA256.HashData(iniBytes)).ToLowerInvariant();
            companions.Add(new MapPackageCompanionEntry("map.ini", "companions/map.ini", iniBytes.Length, iniSha));
            companionPayloads["map.ini"] = iniBytes;
        }

        if (!string.IsNullOrEmpty(mapStrContent))
        {
            var strBytes = Encoding.UTF8.GetBytes(mapStrContent);
            var strSha = Convert.ToHexString(SHA256.HashData(strBytes)).ToLowerInvariant();
            companions.Add(new MapPackageCompanionEntry("map.str", "companions/map.str", strBytes.Length, strSha));
            companionPayloads["map.str"] = strBytes;
        }

        var companionList = companions.Count > 0 ? companions : null;

        return LegacyMapPackageImporter.Write(interchange, packageDirectory, companionList, companionPayloads);
    }
}
