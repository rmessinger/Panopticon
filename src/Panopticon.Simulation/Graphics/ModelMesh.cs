using System.Collections.Generic;
using System.Numerics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// A mesh is composed of the following hierarchy:
///
/// - Mesh: Vertices, Normals, Indices, Materials.
///   - MeshMaterialPasses[]: per-vertex TexCoords,
///                           per-vertex Material indices,
///                           per-triangle Texture indices.
///     - MeshParts[]: One for each unique PipelineState in a material pass.
///                    StartIndex, IndexCount, PipelineState, AlphaTest, Texturing
/// </summary>
public sealed partial class ModelMesh : ModelRenderObject
{
    internal readonly DeviceBuffer VertexBuffer;
    private readonly DeviceBuffer _indexBuffer;

    internal readonly ConstantBuffer<MeshShaderResources.MeshConstants> MeshConstantsBuffer;

    private readonly AxisAlignedBoundingBox _boundingBox;
    public override ref readonly AxisAlignedBoundingBox BoundingBox => ref _boundingBox;

    private readonly BoundingSphere _boundingSphere;
    public override ref readonly BoundingSphere BoundingSphere => ref _boundingSphere;

    public readonly List<ModelMeshPart> MeshParts;

    /// <summary>Number of materials in the imported mesh this mesh was built from; bounds valid material indices.</summary>
    public readonly int MaterialCount;

    /// <summary>
    /// Runtime morph target set materialized under the MorphTargets gate, or null for the
    /// feature-disabled path. Morph-enabled instances deform a dynamic copy; the static
    /// vertex buffer stays intact.
    /// </summary>
    internal readonly ModelMorphTargetSet? MorphTargetSet;

    /// <summary>Base vertices for CPU morph application; null when the mesh has no morph targets.</summary>
    internal readonly MeshShaderResources.MeshVertex.Basic[]? MorphBaseVertices;

    public readonly bool Skinned;

    public override bool Hidden { get; }
    public readonly bool CameraOriented;

    internal override void BuildRenderList(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 modelTransform,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS,
        float visualScale)
    {
        var meshWorldMatrix = Skinned
            ? modelTransform
            : modelInstance.AbsoluteBoneTransforms[parentBone.Index];

        if (visualScale != 1f)
        {
            // Presentation-only proportions (WI-G10-02/03): uniform scale about the model's
            // world anchor. Bone absolutes already carry the world placement, so a bare
            // pre-multiplied CreateScale would scale each subobject about its own bone
            // origin and pull multi-bone models apart; conjugating by the anchor keeps the
            // whole model coherent. Shared matrices stay untouched.
            var anchor = modelInstance.GetWorldMatrix().Translation;
            meshWorldMatrix = meshWorldMatrix *
                Matrix4x4.CreateTranslation(-anchor) *
                Matrix4x4.CreateScale(visualScale) *
                Matrix4x4.CreateTranslation(anchor);
        }

        BuildRenderListWithWorldMatrix(
            renderList,
            camera,
            modelInstance,
            modelMeshInstance,
            parentBone,
            meshWorldMatrix,
            castsShadow,
            renderItemConstantsPS);
    }

    internal override void BuildRenderListWithWorldMatrix(
        RenderList renderList,
        Camera camera,
        ModelInstance modelInstance,
        ModelMeshInstance modelMeshInstance,
        ModelBone parentBone,
        in Matrix4x4 meshWorldMatrix,
        bool castsShadow,
        MeshShaderResources.RenderItemConstantsPS? renderItemConstantsPS = null)
    {
        if (Hidden || !modelInstance.BoneFrameVisibilities[parentBone.Index])
        {
            return;
        }

        Matrix4x4 world;
        if (CameraOriented)
        {
            // TODO: I don't think this is correct yet.

            var localToWorldMatrix = meshWorldMatrix;

            var viewInverse = Matrix4x4Utility.Invert(camera.View);
            var cameraPosition = viewInverse.Translation;

            var toCamera = Vector3.Normalize(Vector3.TransformNormal(
                cameraPosition - meshWorldMatrix.Translation,
                Matrix4x4Utility.Invert(meshWorldMatrix)));

            toCamera.Z = 0;

            var cameraOrientedRotation = Matrix4x4.CreateFromQuaternion(
                QuaternionUtility.CreateRotation(
                    Vector3.UnitX,
                    toCamera));

            world = cameraOrientedRotation * localToWorldMatrix;
        }
        else
        {
            world = meshWorldMatrix;
        }

        // B5-3: whole-mesh distance LOD. A mesh below the screen-size threshold never
        // enters any bucket (opaque, shadow, transparent); the counter is read per frame by
        // the render statistics.
        if (ModelLod.IsNegligible(camera, in BoundingSphere, in world))
        {
            RenderScaleOptions.LodSkippedMeshes++;
            return;
        }

        var meshBoundingBox = AxisAlignedBoundingBox.Transform(BoundingBox, world); // TODO: Not right for skinned meshes

        // B5-2: per-item constants at defaults (only house colour varies per instance) plus
        // a rigid, non-morphing, non-camera-oriented mesh make this item instancing-eligible.
        var psConstants = renderItemConstantsPS ?? default;
        var itemConstantsDefault = psConstants.OverrideFlags == 0
            && psConstants.Opacity >= 1.0f
            && psConstants.TintColor == Vector3.One
            && psConstants.AmbientOverride.W <= 0.0f
            && psConstants.DiffuseOverride.W <= 0.0f
            && psConstants.SpecularOverride.W <= 0.0f
            && psConstants.EmissiveOverride.W <= 0.0f;
        var meshInstancingEligible = !Skinned
            && modelMeshInstance.MorphVertexBuffer is null
            && !CameraOriented
            && itemConstantsDefault;

        InstanceData instancePayload = default;
        if (meshInstancingEligible)
        {
            instancePayload = new InstanceData
            {
                World = world,
                UVOffset = modelMeshInstance.RenderItemConstantsBufferVS.Value.UVOffset,
                HouseColorOpacity = new Vector4(psConstants.HouseColor, psConstants.Opacity),
                Tint = new Vector4(psConstants.TintColor, 1.0f),
            };
        }

        for (var i = 0; i < MeshParts.Count; i++)
        {
            var meshPart = MeshParts[i];

            var forceBlendEnabled = renderItemConstantsPS != null && renderItemConstantsPS.Value.Opacity < 1.0f;
            if (!forceBlendEnabled &&
                modelInstance.PropertyState.TryGetAlpha(modelMeshInstance.SubObjectIndex, meshPart.MaterialIndex, out var animatedAlpha) &&
                animatedAlpha < 1.0f)
            {
                forceBlendEnabled = true;
            }

            var blendEnabled = meshPart.BlendEnabled || forceBlendEnabled;

            var partInstancingEligible = meshInstancingEligible
                && !blendEnabled
                && meshPart.Material.ForwardPass is { InstancedPipeline: not null }
                && meshPart.Material.ShadowPass is { InstancedPipeline: not null };

            // Depth pass

            // TODO: With more work, we could draw shadows for translucent and alpha-tested materials.
            if (!blendEnabled && castsShadow)
            {
                renderList.Shadow.RenderItems.Add(new RenderItem(
                    Name,
                    modelMeshInstance.MeshPartInstances[i].ModelMeshPart.Material.ShadowPass,
                    meshBoundingBox,
                    world,
                    meshPart.StartIndex,
                    meshPart.IndexCount,
                    _indexBuffer,
                    modelMeshInstance.MeshPartInstances[i].BeforeRenderCallbackDepth,
                    partInstancingEligible,
                    instancePayload,
                    MeshConstantsBuffer.Buffer,
                    meshPart.InstanceId,
                    VertexBuffer,
                    meshPart.TexCoordVertexBuffer));
            }

            // Standard pass

            var renderQueue = blendEnabled
                ? renderList.Transparent
                : renderList.Opaque;

            renderQueue.RenderItems.Add(new RenderItem(
                FullName,
                forceBlendEnabled
                    ? modelMeshInstance.MeshPartInstances[i].ModelMeshPart.MaterialBlend.ForwardPass
                    : modelMeshInstance.MeshPartInstances[i].ModelMeshPart.Material.ForwardPass,
                meshBoundingBox,
                world,
                meshPart.StartIndex,
                meshPart.IndexCount,
                _indexBuffer,
                modelMeshInstance.MeshPartInstances[i].BeforeRenderCallback,
                partInstancingEligible,
                instancePayload,
                MeshConstantsBuffer.Buffer,
                meshPart.InstanceId,
                VertexBuffer,
                meshPart.TexCoordVertexBuffer));
        }
    }
}
