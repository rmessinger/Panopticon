#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.W3d;
using Panopticon.IO;

namespace Panopticon.Content.Importing.W3d;

/// <summary>
/// W3D reference importer: owns W3D path resolution, file parsing, embedded/referenced/default
/// skeleton selection, HLOD and single-renderable model composition, and box conversion.
/// Produces CPU-only interchange values; never allocates GPU or asset-store resources.
/// </summary>
internal sealed class W3dModelImporter : IModelImporter
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IPathResolver _pathResolver;

    public string Id => "W3D";

    public W3dModelImporter(IPathResolver pathResolver)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public ImportedModel? ImportModel(string name, AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var w3dFile = FindAndParseFile(name, context);
        if (w3dFile is null)
        {
            Logger.Warn("Failed to load model: " + name);
            return null;
        }

        return Convert(w3dFile, context);
    }

    public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var w3dFile = FindAndParseFile(name, context);
        if (w3dFile is null)
        {
            Logger.Warn("Failed to load hierarchy: " + name);
            return null;
        }

        return w3dFile.Hierarchy != null
            ? ConvertSkeleton(w3dFile.Hierarchy)
            : CreateDefaultSkeleton();
    }

    /// <summary>
    /// Composes one parsed W3D file into an imported model: skeleton selection, then HLOD
    /// first-LOD or single-renderable sub-object composition. Throws contextual exceptions for
    /// invalid model structure per the C1 failure contract.
    /// </summary>
    internal ImportedModel Convert(W3dFile w3dFile, AssetImportContext context)
    {
        var modelName = Path.GetFileNameWithoutExtension(w3dFile.FilePath);

        ImportedSkeleton skeleton;
        ImportedSkeletonOrigin skeletonOrigin;
        if (w3dFile.Hierarchy != null)
        {
            skeleton = ConvertSkeleton(w3dFile.Hierarchy);
            skeletonOrigin = ImportedSkeletonOrigin.Embedded;
        }
        else if (w3dFile.HLod != null)
        {
            var hierarchyName = w3dFile.HLod.Header.HierarchyName;
            var referencedSkeleton = ImportSkeleton(hierarchyName, context);
            if (referencedSkeleton is null)
            {
                throw new InvalidDataException(
                    $"Model '{modelName}' references hierarchy '{hierarchyName}', which could not be loaded.");
            }

            skeleton = referencedSkeleton;
            skeletonOrigin = ImportedSkeletonOrigin.Referenced;
        }
        else
        {
            skeleton = CreateDefaultSkeleton();
            skeletonOrigin = ImportedSkeletonOrigin.Default;
        }

        var subObjects = new List<ImportedSubObject>();

        if (w3dFile.HLod != null)
        {
            if (w3dFile.HLod.Lods.Count == 0)
            {
                throw new InvalidDataException(
                    $"Model '{modelName}' has an HLod chunk with no LOD arrays.");
            }

            foreach (var w3dSubObject in w3dFile.HLod.Lods[0].SubObjects)
            {
                if (!w3dFile.RenderableObjectsByName.TryGetValue(w3dSubObject.Name, out var w3dRenderableObject))
                {
                    throw new InvalidDataException(
                        $"Model '{modelName}' references render object '{w3dSubObject.Name}', " +
                        "which is not present in the file.");
                }

                if (w3dSubObject.BoneIndex >= (uint)skeleton.Bones.Count)
                {
                    throw new InvalidDataException(
                        $"Model '{modelName}' sub-object '{w3dSubObject.Name}' references bone " +
                        $"{w3dSubObject.BoneIndex}, but the skeleton has {skeleton.Bones.Count} bones.");
                }

                subObjects.Add(CreateSubObject(
                    w3dSubObject.Name,
                    (int)w3dSubObject.BoneIndex,
                    w3dRenderableObject,
                    modelName));
            }
        }
        else if (w3dFile.RenderableObjects.Count == 1)
        {
            // Simple models can have only one render object with no HLod chunk.
            var w3dRenderableObjectPair = w3dFile.RenderableObjectsByName.First();

            subObjects.Add(CreateSubObject(
                w3dRenderableObjectPair.Key,
                parentBoneIndex: 0,
                w3dRenderableObjectPair.Value,
                modelName));
        }
        else if (w3dFile.RenderableObjects.Count > 1)
        {
            throw new InvalidDataException(
                $"Model '{modelName}' has {w3dFile.RenderableObjects.Count} render objects " +
                "without an HLod chunk; a W3D model file without HLod may contain only one.");
        }
        else
        {
            throw new InvalidDataException(
                $"Model '{modelName}' contains no render objects.");
        }

        return new ImportedModel(modelName, skeleton, subObjects, skeletonOrigin);
    }

    /// <summary>
    /// Shared skeleton conversion. Validates parent ordering before construction so malformed
    /// data throws a contextual exception instead of an interchange invariant.
    /// </summary>
    internal static ImportedSkeleton ConvertSkeleton(W3dHierarchyDef w3dHierarchy)
    {
        var hierarchyName = w3dHierarchy.Header.Name;
        var pivots = w3dHierarchy.Pivots.Items;

        if (pivots.Count == 0)
        {
            throw new InvalidDataException(
                $"Hierarchy '{hierarchyName}' contains no pivots.");
        }

        var bones = new ImportedBone[pivots.Count];
        for (var i = 0; i < pivots.Count; i++)
        {
            var pivot = pivots[i];

            if (pivot.ParentIdx < -1 || pivot.ParentIdx >= i)
            {
                throw new InvalidDataException(
                    $"Hierarchy '{hierarchyName}' pivot '{pivot.Name}' at index {i} references " +
                    $"parent index {pivot.ParentIdx}; parents must appear before children.");
            }

            bones[i] = new ImportedBone(pivot.Name, pivot.ParentIdx, pivot.Translation, pivot.Rotation);
        }

        return new ImportedSkeleton(hierarchyName, bones);
    }

    internal static ImportedSkeleton CreateDefaultSkeleton()
    {
        // The unnamed default root bone mirrors ModelBoneHierarchy.CreateDefault.
        return new ImportedSkeleton(
            "[Default]",
            [new ImportedBone(string.Empty, parentIndex: -1, Vector3.Zero, Quaternion.Identity)]);
    }

    private static ImportedSubObject CreateSubObject(
        string fullName,
        int parentBoneIndex,
        W3dChunk w3dRenderableObject,
        string modelName)
    {
        return w3dRenderableObject switch
        {
            W3dMesh w3dMesh => new ImportedSubObject(
                fullName,
                w3dMesh.Header.MeshName,
                parentBoneIndex,
                W3dMeshConverter.Convert(w3dMesh)),

            W3dBox w3dBox => new ImportedSubObject(
                fullName,
                GetDisplayNameAfterDot(fullName),
                parentBoneIndex,
                new ImportedBox(w3dBox.Center, w3dBox.Extent)),

            _ => throw new NotSupportedException(
                $"Model '{modelName}' contains unsupported render object chunk " +
                $"'{w3dRenderableObject.ChunkType}'."),
        };
    }

    private static string GetDisplayNameAfterDot(string fullName)
    {
        var dotIndex = fullName.IndexOf('.');
        return dotIndex >= 0
            ? fullName.Substring(dotIndex + 1)
            : fullName;
    }

    private W3dFile? FindAndParseFile(string name, AssetImportContext context)
    {
        FileSystemEntry? entry = null;
        foreach (var path in _pathResolver.GetPaths(name, context.Language))
        {
            entry = context.FileSystem.GetFile(path);
            if (entry != null)
            {
                break;
            }
        }

        if (entry == null)
        {
            return null;
        }

        using var entryStream = entry.Open();
        return W3dFile.FromStream(entryStream, entry.FilePath);
    }
}
