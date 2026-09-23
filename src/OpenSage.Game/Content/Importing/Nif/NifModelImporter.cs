#nullable enable

using System;
using System.IO;
using OpenSage.IO;

namespace OpenSage.Content.Importing.Nif;

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

        var file = ReadNifFile(name, context);
        if (file is null)
        {
            return null;
        }

        var graph = NifSceneConverter.ConvertGraph(file, name, context);

        return new ImportedModel(name, graph.Skeleton, graph.SubObjects, ImportedSkeletonOrigin.Embedded);
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

        return NifSceneConverter.ConvertGraph(file, name, context).Skeleton;
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
