using System;
using Panopticon.Content.Importing;
using Panopticon.Graphics;

namespace Panopticon.Content.Loaders;

/// <summary>
/// Engine-owned materialization of imported interchange values into runtime model assets.
/// Interchange construction has already validated bone ordering, index ranges, and material
/// references, so materialization reaches GPU allocation only with valid data. Texture lookup
/// stays here through AssetStore.Textures; importers emit texture names only.
/// </summary>
internal static class ImportedModelMaterializer
{
    /// <summary>
    /// Skeleton materialization entry point, later used by the hierarchy loader cutover.
    /// </summary>
    public static ModelBoneHierarchy MaterializeSkeleton(ImportedSkeleton importedSkeleton)
    {
        return new ModelBoneHierarchy(importedSkeleton);
    }

    /// <param name="rootScale">
    /// Uniform model scale baked into the root bone binds at materialization (default 1). Applied
    /// once here so the runtime world matrix stays unscaled.
    /// </param>
    public static Model MaterializeModel(ImportedModel importedModel, AssetLoadContext context, float rootScale = 1f)
    {
        return MaterializeModel(
            importedModel,
            new ModelBoneHierarchy(importedModel.Skeleton, rootScale),
            context);
    }

    /// <summary>
    /// Materialization with an externally supplied hierarchy, used when the on-demand loader
    /// delegates a referenced skeleton to the shared ModelBoneHierarchies cache.
    /// </summary>
    public static Model MaterializeModel(ImportedModel importedModel, ModelBoneHierarchy boneHierarchy, AssetLoadContext context)
    {
        // Blocking diagnostic before any GPU allocation: morph models only materialize under their gate.
        // The gates travel on the imported model from the importing context (NIF is the only gated importer).
        if (!importedModel.IsFeatureGateEnabled(ImportedFeatureGates.MorphTargets))
        {
            for (var i = 0; i < importedModel.SubObjects.Count; i++)
            {
                if (importedModel.SubObjects[i].RenderObject is ImportedMesh { MorphTargetSets.Count: > 0 })
                {
                    throw new NotSupportedException(
                        $"Model '{importedModel.Name}' carries morph target sets; materializing morph targets requires " +
                        $"the '{ImportedFeatureGates.MorphTargets}' feature gate and the C4g morph runtime.");
                }
            }
        }

        ImportedModelValidator.Validate(importedModel, boneHierarchy);

        var subObjects = new ModelSubObject[importedModel.SubObjects.Count];
        for (var i = 0; i < subObjects.Length; i++)
        {
            var importedSubObject = importedModel.SubObjects[i];

            var renderObject = importedSubObject.RenderObject;
            if (renderObject is ImportedMesh mesh && NeedsTangentGeneration(mesh))
            {
                renderObject = ImportedTangentGenerator.GenerateTangents(mesh);
            }

            subObjects[i] = new ModelSubObject(
                importedSubObject.FullName,
                importedSubObject.DisplayName,
                boneHierarchy.Bones[importedSubObject.ParentBoneIndex],
                CreateRenderObject(importedSubObject, renderObject, context));
        }

        return new Model(importedModel.Name, boneHierarchy, subObjects);
    }

    private static bool NeedsTangentGeneration(ImportedMesh mesh)
    {
        // Only normal-mapped PBR parts that omit part of the tangent frame need generation;
        // W3D meshes and meshes with imported tangents and binormals pass through untouched.
        foreach (var part in mesh.MeshParts)
        {
            var payload = mesh.Materials[part.MaterialIndex].PbrMetallicRoughness;
            if (payload?.NormalTexture is null)
            {
                continue;
            }

            if (mesh.Vertices[0].Tangent is null || mesh.Vertices[0].Binormal is null)
            {
                return true;
            }
        }

        return false;
    }

    private static ModelRenderObject CreateRenderObject(
        ImportedSubObject importedSubObject,
        ImportedRenderObject renderObject,
        AssetLoadContext context)
    {
        return renderObject switch
        {
            ImportedMesh importedMesh => new ModelMesh(importedSubObject.DisplayName, importedMesh, context),

            ImportedBox importedBox => new ModelBox(importedBox),

            _ => throw new NotSupportedException(
                $"Sub-object '{importedSubObject.FullName}' has unsupported render object type " +
                $"'{renderObject.GetType().Name}'."),
        };
    }
}
