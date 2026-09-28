#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.IO;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// NIF 10.1 model importer through the C1 seam: resolves
/// cache/nifcache/&lt;key&gt;.nifcache through the import file system, parses a
/// validated graph, preserves the node hierarchy as an embedded skeleton,
/// bakes shape transforms relative to their parent bones, and produces
/// CPU-only interchange values. Returns null only when the top-level entry
/// is absent. CPU-only; never allocates GPU, asset-store, or
/// texture-decode resources, and is not registered in shipped game
/// definitions.
/// </summary>
internal sealed class NifModelImporter : IModelImporter
{
    private const string ContainerDirectory = "cache/nifcache";
    private const string ContainerExtension = ".nifcache";

    public string Id => "NIF";

    public ImportedModel? ImportModel(string name, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (NifMorphModelKey.TryParse(name, out var morphKey))
        {
            return ImportMorphModel(morphKey, name, context);
        }

        var file = ReadNifFile(name, context);
        if (file is null)
        {
            return null;
        }

        var graph = NifSceneConverter.ConvertGraph(file, name, context);

        return new ImportedModel(name, graph.Skeleton, graph.SubObjects, ImportedSkeletonOrigin.Embedded);
    }

    /// <summary>
    /// Imports a model with morph target sets paired from a KF sequence:
    /// the KF's controlled blocks name the target shapes and reference
    /// their NiGeomMorpherController blocks. Returns null when either the
    /// model or the KF entry is absent or the KF carries no morph
    /// controllers (an unpaired morph KF).
    /// </summary>
    private ImportedModel? ImportMorphModel(NifMorphModelKey morphKey, string modelKey, AssetImportContext context)
    {
        var file = ReadNifFile(morphKey.ModelName, context);
        if (file is null)
        {
            return null;
        }

        var entry = context.FileSystem.GetFile(morphKey.MorphKfPath);
        if (entry is null)
        {
            return null;
        }

        FileFormats.Nif.NifFile kfFile;
        using (var stream = entry.Open())
        using (var buffered = new MemoryStream())
        {
            stream.CopyTo(buffered);
            buffered.Position = 0;
            kfFile = FileFormats.Nif.NifFile.Read(buffered);
        }

        if (kfFile.Blocks[0] is not FileFormats.Nif.NifControllerSequence sequence)
        {
            throw new InvalidDataException(
                $"NIF animation '{modelKey}' block 0 is not a controller sequence.");
        }

        if (kfFile.Roots.Count > 1)
        {
            throw new NotSupportedException(
                $"NIF animation '{modelKey}' holds {kfFile.Roots.Count} root blocks; " +
                "multi-sequence KF files need a selection rule.");
        }

        var morphSources = new List<NifMorphSource>();
        foreach (var controlledBlock in sequence.ControlledBlocks)
        {
            if (controlledBlock.ControllerReference < 0 ||
                kfFile.Blocks[controlledBlock.ControllerReference] is not FileFormats.Nif.NifGeomMorpherController controller)
            {
                continue;
            }

            if (kfFile.Blocks[controller.DataReference] is not FileFormats.Nif.NifMorphData morphData)
            {
                throw new InvalidDataException(
                    $"NIF animation '{modelKey}' controller references block {controller.DataReference} " +
                    "as morph data, but that block is not a NiMorphData.");
            }

            if (morphSources.Any(source => string.Equals(source.ShapeName, controlledBlock.TargetName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException(
                    $"NIF animation '{modelKey}' carries multiple morph controllers for shape " +
                    $"'{controlledBlock.TargetName}'; one morph source per shape is supported.");
            }

            morphSources.Add(new NifMorphSource(controlledBlock.TargetName, morphData));
        }

        if (morphSources.Count == 0)
        {
            return null;
        }

        var graph = NifSceneConverter.ConvertGraph(
            file, morphKey.ModelName, context, convertMeshes: true, morphSources);

        return new ImportedModel(modelKey, graph.Skeleton, graph.SubObjects, ImportedSkeletonOrigin.Embedded);
    }

    public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var file = ReadNifFile(name, context);
        if (file is null)
        {
            return null;
        }

        return NifSceneConverter.ConvertGraph(file, name, context, convertMeshes: false).Skeleton;
    }

    internal static FileFormats.Nif.NifFile? ReadNifFile(string name, AssetImportContext context)
    {
        var entry = context.FileSystem.GetFile(
            $"{ContainerDirectory}/{name}{ContainerExtension}");
        if (entry is null)
        {
            return null;
        }

        // ZIP entry streams are not seekable; the bounded reader requires a
        // knowable length, so buffer the (small) model into memory.
        using var stream = entry.Open();
        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;
        return FileFormats.Nif.NifFile.Read(buffered);
    }
}
