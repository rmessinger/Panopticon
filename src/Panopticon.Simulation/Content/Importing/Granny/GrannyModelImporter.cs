#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// GR2 model importer through the C1 seam: resolves an archive path (for example
/// Art\units\...\musketeer_4age.gr2) through the import file system, reads it with the generic
/// Granny container reader, and converts the first skeleton plus every mesh into CPU-only
/// interchange values normalized to engine axes and meters. Returns null only when the entry is
/// absent. Rigid binding only until WI-C10c-01.
/// </summary>
internal sealed class GrannyModelImporter : IModelImporter
{
    public string Id => "GR2";

    public GrannyImportDiagnostics Diagnostics { get; } = new();

    public ImportedModel? ImportModel(string name, AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var root = ReadRoot(name, context);
        if (root is null)
        {
            return null;
        }

        var axes = ReadAxisSystem(root);
        var skeleton = ReadSkeleton(root, axes, name);
        var boneIndexByName = GrannySkeletonConverter.BuildBoneIndexByName(skeleton);

        var meshObjects = root.GetObjects("Meshes");
        if (meshObjects.Count == 0)
        {
            throw new InvalidDataException($"Granny model '{name}': the file contains no meshes.");
        }

        var materialConverter = new Aoe3MaterialConverter(context, Diagnostics);
        var subObjects = new List<ImportedSubObject>();
        for (var i = 0; i < meshObjects.Count; i++)
        {
            subObjects.AddRange(GrannyMeshConverter.Convert(
                meshObjects[i], skeleton, boneIndexByName, axes, name, i, materialConverter));
        }

        return new ImportedModel(
            name,
            skeleton,
            subObjects,
            ImportedSkeletonOrigin.Embedded,
            context.EnabledFeatureGates);
    }

    public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var root = ReadRoot(name, context);
        if (root is null)
        {
            return null;
        }

        return ReadSkeleton(root, ReadAxisSystem(root), name);
    }

    private static GrannyObject? ReadRoot(string name, AssetImportContext context)
    {
        var entry = context.FileSystem.GetFile(name);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;

        try
        {
            return GrannyFile.Read(buffered, name).Root;
        }
        catch (GrannyCompressedSectionException ex)
        {
            throw new InvalidDataException(
                $"Granny model '{name}' cannot be imported: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"Granny model '{name}': {ex.Message}", ex);
        }
    }

    private static GrannyAxisSystem ReadAxisSystem(GrannyObject root)
    {
        return root.GetObject("ArtToolInfo") is { } artToolInfo
            ? GrannyAxisSystem.FromArtToolInfo(artToolInfo)
            : new GrannyAxisSystem(1.0f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }

    private static ImportedSkeleton ReadSkeleton(GrannyObject root, GrannyAxisSystem axes, string name)
    {
        var skeletons = root.GetObjects("Skeletons");
        if (skeletons.Count == 0)
        {
            throw new InvalidDataException($"Granny model '{name}': the file contains no skeleton.");
        }

        return GrannySkeletonConverter.Convert(skeletons[0], axes, name);
    }
}
