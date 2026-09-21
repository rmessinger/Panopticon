#nullable enable

using System;
using System.IO;
using System.Numerics;
using OpenSage.FileFormats.Nif;
using OpenSage.IO;

namespace OpenSage.Content.Importing.Nif;

/// <summary>
/// NIF 10.1 static-mesh importer through the C1 seam: resolves
/// cache/nifcache/&lt;key&gt;.nifcache through the import file system, parses a
/// validated graph, bakes transforms, and produces CPU-only interchange
/// values. Returns null only when the top-level entry is absent. CPU-only;
/// never allocates GPU, asset-store, or texture-decode resources, and is not
/// registered in shipped game definitions.
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

        var entry = context.FileSystem.GetFile(
            $"{ContainerDirectory}/{name}{ContainerExtension}");
        if (entry is null)
        {
            return null;
        }

        // ZIP entry streams are not seekable; the bounded reader requires a
        // knowable length, so buffer the (small) model into memory.
        FileFormats.Nif.NifFile file;
        using (var stream = entry.Open())
        using (var buffered = new MemoryStream())
        {
            stream.CopyTo(buffered);
            buffered.Position = 0;
            file = FileFormats.Nif.NifFile.Read(buffered);
        }

        var skeleton = CreateDefaultSkeleton();
        var subObjects = NifSceneConverter.Convert(file, name, context);

        return new ImportedModel(name, skeleton, subObjects, ImportedSkeletonOrigin.Default);
    }

    public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return CreateDefaultSkeleton();
    }

    private static ImportedSkeleton CreateDefaultSkeleton()
    {
        // One identity root bone; NIF static targets carry no skeleton.
        return new ImportedSkeleton(
            "[Default]",
            [new ImportedBone(string.Empty, parentIndex: -1, Vector3.Zero, Quaternion.Identity)]);
    }
}
