using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;

namespace Panopticon.Graphics;

public sealed class ModelBox : ModelRenderObject
{
    private readonly AxisAlignedBoundingBox _boundingBox;
    public override ref readonly AxisAlignedBoundingBox BoundingBox => ref _boundingBox;

    private readonly BoundingSphere _boundingSphere;
    public override ref readonly BoundingSphere BoundingSphere => ref _boundingSphere;

    public override bool Hidden { get; } = false;

    internal ModelBox(ImportedBox importedBox)
    {
        _boundingBox = importedBox.BoundingBox;
        _boundingSphere = importedBox.BoundingSphere;
    }

    internal override void BuildRenderList(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 modelTransform,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS)
    {
        // TODO
    }

    internal override void BuildRenderListWithWorldMatrix(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 meshWorldMatrix,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS)
    {
        // TODO
    }
}
