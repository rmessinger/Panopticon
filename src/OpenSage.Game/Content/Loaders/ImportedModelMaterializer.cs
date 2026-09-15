using System;
using OpenSage.Content.Importing;
using OpenSage.Graphics;

namespace OpenSage.Content.Loaders;

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

    public static Model MaterializeModel(ImportedModel importedModel, AssetLoadContext context)
    {
        var boneHierarchy = new ModelBoneHierarchy(importedModel.Skeleton);

        var subObjects = new ModelSubObject[importedModel.SubObjects.Count];
        for (var i = 0; i < subObjects.Length; i++)
        {
            var importedSubObject = importedModel.SubObjects[i];

            subObjects[i] = new ModelSubObject(
                importedSubObject.FullName,
                importedSubObject.DisplayName,
                boneHierarchy.Bones[importedSubObject.ParentBoneIndex],
                CreateRenderObject(importedSubObject, context));
        }

        return new Model(importedModel.Name, boneHierarchy, subObjects);
    }

    private static ModelRenderObject CreateRenderObject(
        ImportedSubObject importedSubObject,
        AssetLoadContext context)
    {
        return importedSubObject.RenderObject switch
        {
            ImportedMesh importedMesh => new ModelMesh(importedSubObject.DisplayName, importedMesh, context),

            ImportedBox importedBox => new ModelBox(importedBox),

            _ => throw new NotSupportedException(
                $"Sub-object '{importedSubObject.FullName}' has unsupported render object type " +
                $"'{importedSubObject.RenderObject.GetType().Name}'."),
        };
    }
}
