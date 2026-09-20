#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Complete glTF model importer: composes document loading, resource resolution, scene
/// planning, primitive conversion, skinning, and PBR material conversion into whole imported
/// graphs. Returns null only for an invalid key or missing root document; never a partial
/// graph.
/// </summary>
internal sealed class GltfModelImporter : IModelImporter
{
    private readonly GltfImportOptions _options;
    private readonly GltfImportDiagnostics? _diagnostics;

    public string Id => "GLTF";

    internal GltfModelImporter(GltfImportOptions options, GltfImportDiagnostics? diagnostics = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _diagnostics = diagnostics;
    }

    public ImportedModel? ImportModel(string name, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!GltfAssetKey.TryParseModel(name, out var modelKey))
        {
            return null;
        }

        var resolver = new GltfResourceResolver(modelKey.Path, context.FileSystem, _options);
        var document = GltfDocumentLoader.TryLoadDocument(modelKey.Path, context, _options, resolver);
        if (document is null)
        {
            return null;
        }

        var root = document.Root;
        ValidateExtensionPolicy(root, _diagnostics);
        EnforceDocumentLimits(modelKey.Path, root);

        var plan = GltfSceneConverter.CreatePlan(modelKey.Path, root, _options, document.HasDeclaredDefaultScene);

        var skeleton = plan.Skeleton;
        GltfSkinConversion? skin = null;
        if (plan.SkinnedNodes.Count > 0)
        {
            skin = GltfSkinConverter.ConvertSkin(root, plan, _options);
            skeleton = skin.Skeleton;
        }

        var subObjects = new List<ImportedSubObject>();
        var primitiveCountByNode = new Dictionary<int, int>();

        foreach (var attachment in plan.Attachments)
        {
            var node = root.LogicalNodes[attachment.SourceNodeIndex];
            var primitives = node.Mesh!.Primitives;
            if (primitives.Count > _options.MaxPrimitiveCount)
            {
                throw new InvalidDataException(
                    $"glTF model '{modelKey.Path}' node '{node.Name}' has {primitives.Count} primitives; the limit is {_options.MaxPrimitiveCount}.");
            }

            for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
            {
                var primitive = primitives[primitiveIndex];
                EnforcePrimitiveLimits(modelKey.Path, primitive, node.Name, primitiveIndex);

                var material = GltfMaterialConverter.Convert(primitive.Material, resolver);
                var converted = GltfPrimitiveConverter.Convert(
                    primitive,
                    attachment,
                    _options,
                    _ => material);

                foreach (var mesh in converted.Meshes)
                {
                    ValidatePartUvSets(modelKey.Path, node.Name, primitiveIndex, mesh, material);
                    subObjects.Add(new ImportedSubObject(
                        $"{node.Name}.{primitiveIndex}.{subObjects.Count}",
                        $"{node.Name}_{primitiveIndex}",
                        attachment.BoneIndex,
                        mesh));
                }
            }
        }

        if (skin is not null)
        {
            foreach (var skinnedNode in plan.SkinnedNodes)
            {
                var node = root.LogicalNodes[skinnedNode.SourceNodeIndex];
                var primitives = node.Mesh!.Primitives;
                if (primitives.Count > _options.MaxPrimitiveCount)
                {
                    throw new InvalidDataException(
                        $"glTF model '{modelKey.Path}' node '{node.Name}' has {primitives.Count} primitives; the limit is {_options.MaxPrimitiveCount}.");
                }

                for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
                {
                    var primitive = primitives[primitiveIndex];
                    EnforcePrimitiveLimits(modelKey.Path, primitive, node.Name, primitiveIndex);

                    var material = GltfMaterialConverter.Convert(primitive.Material, resolver);
                    var converted = GltfSkinConverter.ConvertSkinnedPrimitive(
                        primitive,
                        skinnedNode.SourceNodeIndex,
                        skin,
                        _options,
                        _ => material);

                    foreach (var mesh in converted.Meshes)
                    {
                        // Skinned primitives hang off the imported skeleton root; the
                        // skinned mesh-node transform is not baked.
                        subObjects.Add(new ImportedSubObject(
                            $"{node.Name}.{primitiveIndex}.{subObjects.Count}",
                            $"{node.Name}_{primitiveIndex}",
                            parentBoneIndex: 0,
                            mesh));
                    }
                }
            }
        }

        if (subObjects.Count == 0)
        {
            throw new InvalidDataException(
                $"glTF model '{modelKey.Path}' contains no convertible geometry.");
        }

        return new ImportedModel(
            Path.GetFileNameWithoutExtension(modelKey.Path),
            skeleton,
            subObjects,
            ImportedSkeletonOrigin.Embedded);
    }

    public ImportedSkeleton? ImportSkeleton(string name, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!GltfAssetKey.TryParseModel(name, out var modelKey))
        {
            return null;
        }

        var resolver = new GltfResourceResolver(modelKey.Path, context.FileSystem, _options);
        var document = GltfDocumentLoader.TryLoadDocument(modelKey.Path, context, _options, resolver);
        if (document is null)
        {
            return null;
        }

        ValidateExtensionPolicy(document.Root, _diagnostics);
        EnforceDocumentLimits(modelKey.Path, document.Root);

        var plan = GltfSceneConverter.CreatePlan(modelKey.Path, document.Root, _options, document.HasDeclaredDefaultScene);
        return plan.Skeleton;
    }

    internal static void ValidateExtensionPolicy(ModelRoot root, GltfImportDiagnostics? diagnostics)
    {
        foreach (var required in root.ExtensionsRequired)
        {
            throw new NotSupportedException(
                $"glTF document requires extension '{required}', which is outside the supported subset.");
        }

        if (diagnostics != null)
        {
            foreach (var used in root.ExtensionsUsed)
            {
                diagnostics.WarnOptionalExtensionIgnored(used);
            }
        }
    }

    private void EnforceDocumentLimits(string modelKey, ModelRoot root)
    {
        if (root.LogicalNodes.Count > _options.MaxNodeCount)
        {
            throw new InvalidDataException(
                $"glTF model '{modelKey}' has {root.LogicalNodes.Count} nodes; the limit is {_options.MaxNodeCount}.");
        }

        if (root.LogicalMeshes.Count > _options.MaxMeshCount)
        {
            throw new InvalidDataException(
                $"glTF model '{modelKey}' has {root.LogicalMeshes.Count} meshes; the limit is {_options.MaxMeshCount}.");
        }

        if (root.LogicalSkins.Count > 1)
        {
            throw new NotSupportedException(
                $"glTF model '{modelKey}' has {root.LogicalSkins.Count} skins; one skin per model is supported.");
        }

        foreach (var skin in root.LogicalSkins)
        {
            if (skin.JointsCount > _options.MaxJointCount)
            {
                throw new InvalidDataException(
                    $"glTF model '{modelKey}' skin has {skin.JointsCount} joints; the limit is {_options.MaxJointCount}.");
            }
        }

        if (root.LogicalAnimations.Count > _options.MaxAnimationCount)
        {
            throw new InvalidDataException(
                $"glTF model '{modelKey}' has {root.LogicalAnimations.Count} animations; the limit is {_options.MaxAnimationCount}.");
        }
    }

    private void EnforcePrimitiveLimits(string modelPath, MeshPrimitive primitive, string nodeName, int primitiveIndex)
    {
        if (!primitive.VertexAccessors.TryGetValue("POSITION", out var positionAccessor))
        {
            return;
        }

        if (positionAccessor.Count > _options.MaxVertexCount)
        {
            throw new InvalidDataException(
                $"glTF model '{modelPath}' node '{nodeName}' primitive {primitiveIndex} has {positionAccessor.Count} " +
                $"vertices; the limit is {_options.MaxVertexCount}.");
        }

        var indexCount = primitive.IndexAccessor?.Count ?? positionAccessor.Count;
        if (indexCount > _options.MaxIndexCount)
        {
            throw new InvalidDataException(
                $"glTF model '{modelPath}' node '{nodeName}' primitive {primitiveIndex} has {indexCount} " +
                $"indices; the limit is {_options.MaxIndexCount}.");
        }
    }

    private static void ValidatePartUvSets(
        string modelPath,
        string nodeName,
        int primitiveIndex,
        ImportedMesh mesh,
        ImportedMaterial material)
    {
        var part = mesh.MeshParts[0];
        try
        {
            GltfMaterialConverter.ValidateTextureBindingUvSets(
                material,
                part.TextureCoordinates0 != null,
                part.TextureCoordinates1 != null);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException(
                $"glTF model '{modelPath}' node '{nodeName}' primitive {primitiveIndex}: {ex.Message}", ex);
        }
    }
}
