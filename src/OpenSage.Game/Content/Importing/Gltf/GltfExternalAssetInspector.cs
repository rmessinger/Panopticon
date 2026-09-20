#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Mathematics;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// glTF inspection facade over the C4 pipeline: the same document loader, resolver, limits,
/// extension policy, scene planning, subset checks, and converters as the importers. Counts,
/// bounds, animation descriptors, and non-fatal warnings only; never a second, more
/// permissive parser path.
/// </summary>
internal sealed class GltfExternalAssetInspector : IExternalAssetInspector
{
    private static readonly string[] TextureChannels =
    [
        "BaseColor",
        "MetallicRoughness",
        "Normal",
        "Occlusion",
        "Emissive",
    ];

    private readonly GltfImportOptions _options;

    internal GltfExternalAssetInspector(GltfImportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!GltfAssetKey.TryParseModel(assetKey, out var modelKey))
        {
            return null;
        }

        var diagnostics = new GltfImportDiagnostics();
        var resolver = new GltfResourceResolver(modelKey.Path, context.FileSystem, _options);
        var document = GltfDocumentLoader.TryLoadDocument(modelKey.Path, context, _options, resolver);
        if (document is null)
        {
            return null;
        }

        var root = document.Root;
        GltfModelImporter.ValidateExtensionPolicy(root, diagnostics);
        GltfModelImporter.EnforceDocumentLimits(modelKey.Path, root, _options);

        var plan = GltfSceneConverter.CreatePlan(modelKey.Path, root, _options, document.HasDeclaredDefaultScene);

        GltfSkinConversion? skin = null;
        if (plan.SkinnedNodes.Count > 0)
        {
            skin = GltfSkinConverter.ConvertSkin(root, plan, _options);
        }

        var primitiveCount = 0;
        long vertexCount = 0;
        long indexCount = 0;
        var materials = new HashSet<Material?>();
        var images = new HashSet<Image>();
        var boxMin = new Vector3(float.PositiveInfinity);
        var boxMax = new Vector3(float.NegativeInfinity);
        var hasGeometry = false;

        foreach (var (node, sourceNodeIndex, attachment) in EnumeratePlanPrimitives(root, plan))
        {
            var primitives = node.Mesh!.Primitives;
            if (primitives.Count > _options.MaxPrimitiveCount)
            {
                throw new InvalidDataException(
                    $"glTF model '{modelKey.Path}' node '{node.Name}' has {primitives.Count} primitives; the limit is {_options.MaxPrimitiveCount}.");
            }

            for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
            {
                var primitive = primitives[primitiveIndex];
                GltfModelImporter.EnforcePrimitiveLimits(modelKey.Path, primitive, node.Name, primitiveIndex, _options);

                primitiveCount++;
                var material = GltfMaterialConverter.Convert(primitive.Material, resolver);
                materials.Add(primitive.Material);
                CollectImages(primitive.Material, images);

                var converted = attachment is not null
                    ? GltfPrimitiveConverter.Convert(primitive, attachment, _options, _ => material)
                    : GltfSkinConverter.ConvertSkinnedPrimitive(
                        primitive,
                        sourceNodeIndex,
                        skin!,
                        _options,
                        _ => material);

                foreach (var mesh in converted.Meshes)
                {
                    vertexCount += mesh.Vertices.Count;
                    indexCount += mesh.Indices.Count;
                    var meshBox = mesh.BoundingBox;
                    boxMin = Vector3.Min(boxMin, meshBox.Min);
                    boxMax = Vector3.Max(boxMax, meshBox.Max);
                    hasGeometry = true;
                }
            }
        }

        if (!hasGeometry)
        {
            throw new InvalidDataException(
                $"glTF model '{modelKey.Path}' contains no convertible geometry.");
        }

        var box = new AxisAlignedBoundingBox(boxMin, boxMax);
        var center = (boxMin + boxMax) / 2;
        var bounds = new ExternalModelBounds(
            boxMin,
            boxMax,
            center,
            Vector3.Distance(center, boxMax));

        var animations = new List<ExternalAnimationDescriptor>();
        foreach (var index in Enumerable.Range(0, root.LogicalAnimations.Count))
        {
            var imported = GltfAnimationConverter.Convert(index, modelKey.Path, document, plan, _options);
            animations.Add(new ExternalAnimationDescriptor(
                $"{modelKey.Path}#animation={index}",
                imported.Name,
                imported.Duration,
                imported.Channels.Count));
        }

        return new ExternalAssetCatalog(
            Path.GetFileName(modelKey.Path),
            ExternalAssetInspection.GltfFormatId,
            sceneCount: root.LogicalScenes.Count,
            nodeCount: root.LogicalNodes.Count,
            primitiveCount: primitiveCount,
            vertexCount: vertexCount,
            indexCount: indexCount,
            materialCount: materials.Count,
            imageCount: images.Count,
            skinCount: root.LogicalSkins.Count,
            jointCount: root.LogicalSkins.Count > 0 ? root.LogicalSkins[0].JointsCount : 0,
            bounds: bounds,
            requiredFeatureIds: root.ExtensionsRequired.ToArray(),
            optionalFeatureIds: root.ExtensionsUsed.ToArray(),
            animations: animations,
            diagnostics: diagnostics.Warnings
                .Select(w => new ExternalAssetInspectionDiagnostic(
                    "GLTF0001",
                    ExternalInspectionDiagnosticSeverity.Warning,
                    w))
                .ToArray());
    }

    private static IEnumerable<(Node Node, int SourceNodeIndex, GltfAttachmentPlan? Attachment)> EnumeratePlanPrimitives(
        ModelRoot root,
        GltfScenePlan plan)
    {
        foreach (var attachment in plan.Attachments)
        {
            yield return (root.LogicalNodes[attachment.SourceNodeIndex], attachment.SourceNodeIndex, attachment);
        }

        foreach (var skinned in plan.SkinnedNodes)
        {
            yield return (root.LogicalNodes[skinned.SourceNodeIndex], skinned.SourceNodeIndex, null);
        }
    }

    private static void CollectImages(Material? material, HashSet<Image> images)
    {
        if (material is null)
        {
            return;
        }

        foreach (var channelName in TextureChannels)
        {
            var image = material.FindChannel(channelName)?.Texture?.PrimaryImage;
            if (image is not null)
            {
                images.Add(image);
            }
        }
    }
}
