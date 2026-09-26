#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.M3;
using Panopticon.IO;

namespace Panopticon.Content.Importing.M3;

internal sealed class M3ModelImporter : IModelImporter
{
    public string Id => "m3";

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

        var file = ReadM3File(name, context);
        if (file is null)
        {
            return null;
        }

        var material = CreatePlaceholderMaterial();
        var subObjects = new List<ImportedSubObject>();
        foreach (var division in file.Model.Divisions)
        {
            for (var batchIndex = 0; batchIndex < division.Batches.Count; batchIndex++)
            {
                subObjects.Add(M3GeometryConverter.ConvertBatch(file.Model, division, batchIndex, name, material));
            }
        }

        if (subObjects.Count == 0)
        {
            throw new InvalidDataException($"M3 model '{name}' contains no importable batch.");
        }

        return new ImportedModel(
            name,
            CreateSkeleton(name),
            subObjects,
            ImportedSkeletonOrigin.Embedded);
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

        return ResolveEntry(name, context.FileSystem) is null
            ? null
            : CreateSkeleton(name);
    }

    internal static M3File? ReadM3File(string name, AssetImportContext context)
    {
        var entry = ResolveEntry(name, context.FileSystem);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;
        return M3File.Read(buffered, entry.FilePath);
    }

    private static FileSystemEntry? ResolveEntry(string name, FileSystem fileSystem)
    {
        var entry = fileSystem.GetFile(name);
        if (entry is not null)
        {
            return entry;
        }

        var normalizedName = NormalizeVirtualPath(name);
        foreach (var candidate in fileSystem.GetFilesInDirectory(string.Empty, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(NormalizeVirtualPath(candidate.FilePath), normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeVirtualPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static ImportedSkeleton CreateSkeleton(string name)
    {
        return new ImportedSkeleton(
            name,
            [new ImportedBone("root", -1, Vector3.Zero, Quaternion.Identity)]);
    }

    private static ImportedMaterial CreatePlaceholderMaterial()
    {
        return ImportedMaterial.CreatePbrMetallicRoughness(
            new ImportedPbrMetallicRoughness(
                baseColorFactor: Vector4.One,
                metallicFactor: 0.0f,
                roughnessFactor: 1.0f));
    }
}
