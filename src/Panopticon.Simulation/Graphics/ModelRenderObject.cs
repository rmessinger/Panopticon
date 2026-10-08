using System.Numerics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;

namespace Panopticon.Graphics;

public abstract class ModelRenderObject : BaseAsset
{
    public abstract ref readonly AxisAlignedBoundingBox BoundingBox { get; }
    public abstract ref readonly BoundingSphere BoundingSphere { get; }

    public abstract bool Hidden { get; }

    internal ModelSubObject SubObject;

    internal abstract void BuildRenderList(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 modelTransform,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS,
        float visualScale);

    internal abstract void BuildRenderListWithWorldMatrix(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 meshWorldMatrix,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS = null);
}
