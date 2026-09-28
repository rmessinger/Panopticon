using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content.Loaders;
using Panopticon.Graphics.Animation;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Veldrid;

namespace Panopticon.Graphics;

internal sealed class ModelMeshPartInstance : DisposableBase
{
    private readonly ResourceSet _renderItemConstantsResourceSet;
    private readonly ConstantBuffer<MeshShaderResources.RenderItemConstantsPS> _renderItemConstantsBufferPS;
    private MeshShaderResources.RenderItemConstantsPS _appliedConstants;

    public readonly ModelMeshPart ModelMeshPart;
    public readonly ModelMeshInstance MeshInstance;

    public readonly BeforeRenderDelegate BeforeRenderCallback;
    public readonly BeforeRenderDelegate BeforeRenderCallbackDepth;

    public ModelMeshPartInstance(
        ModelMeshPart modelMeshPart,
        ModelMeshInstance modelMeshInstance,
        AssetLoadContext loadContext)
    {
        ModelMeshPart = modelMeshPart;
        MeshInstance = modelMeshInstance;

        _renderItemConstantsBufferPS = AddDisposable(
            new ConstantBuffer<MeshShaderResources.RenderItemConstantsPS>(
                loadContext.GraphicsDevice,
                $"{modelMeshInstance.ModelInstance.Model.Name}.{modelMeshPart.ModelMesh.Name}.RenderItemConstantsPS"));

        _renderItemConstantsResourceSet = AddDisposable(
            loadContext.ShaderResources.Mesh.CreateRenderItemConstantsResourceSet(
                modelMeshPart.ModelMesh.MeshConstantsBuffer,
                modelMeshInstance.RenderItemConstantsBufferVS,
                modelMeshInstance.ModelInstance.SkinningBuffer,
                _renderItemConstantsBufferPS));

        BeforeRenderCallback = (CommandList cl, in RenderItem renderItem) =>
        {
            OnBeforeRender(cl, renderItem);
        };

        BeforeRenderCallbackDepth = (CommandList cl, in RenderItem renderItem) =>
        {
            OnBeforeRender(cl, renderItem);
        };
    }

    private void OnBeforeRender(
        CommandList cl,
        in RenderItem renderItem)
    {
        MeshInstance.OnBeforeRender(cl, renderItem);

        UpdatePropertyConstants(cl);

        cl.SetGraphicsResourceSet(3, _renderItemConstantsResourceSet);

        ModelMeshPart.BeforeRender(cl, MeshInstance.MorphVertexBuffer);
    }

    /// <summary>
    /// Composes the model-level constants (house color, opacity, tint) with this part's animated
    /// material-slot overrides into the part's own constants buffer. Shared materials stay untouched.
    /// </summary>
    private void UpdatePropertyConstants(CommandList cl)
    {
        var modelInstance = MeshInstance.ModelInstance;
        var value = modelInstance.RenderItemConstantsBufferPS.Value;

        var hasSlotOverrides = modelInstance.PropertyState.TryGetSlotOverride(
            MeshInstance.SubObjectIndex,
            ModelMeshPart.MaterialIndex,
            out var slot);
        if (hasSlotOverrides)
        {
            ApplySlotOverrides(ref value, slot);
        }

        if (!EqualsApplied(value))
        {
            _renderItemConstantsBufferPS.Value = value;
            _renderItemConstantsBufferPS.Update(cl);
            _appliedConstants = value;
        }
    }

    private static void ApplySlotOverrides(
        ref MeshShaderResources.RenderItemConstantsPS value,
        ModelMaterialSlotState slot)
    {
        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Ambient) != 0)
        {
            value.AmbientOverride = new Vector4(slot.Ambient, 1.0f);
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Diffuse) != 0)
        {
            value.DiffuseOverride = new Vector4(slot.Diffuse, 1.0f);
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Specular) != 0)
        {
            value.SpecularOverride = new Vector4(slot.Specular, 1.0f);
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Emissive) != 0)
        {
            value.EmissiveOverride = new Vector4(slot.Emissive, 1.0f);
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.Alpha) != 0)
        {
            value.MaterialOpacity = slot.Alpha;
            value.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Alpha;
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.UvTranslate) != 0)
        {
            value.UvTranslate = new Vector4(slot.UvTranslateU, slot.UvTranslateV, 0.0f, 0.0f);
            value.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvTranslate;
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.UvScale) != 0)
        {
            value.UvScale = new Vector4(slot.UvScaleU, slot.UvScaleV, 0.0f, 0.0f);
            value.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvScale;
        }

        if ((slot.OverrideFlags & (uint)ModelMaterialOverrideFlags.UvRotate) != 0)
        {
            value.UvRotate = new Vector4(slot.UvRotation, 0.0f, 0.0f, 0.0f);
            value.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvRotate;
        }
    }

    private bool EqualsApplied(in MeshShaderResources.RenderItemConstantsPS value)
    {
        return _appliedConstants.HouseColor == value.HouseColor &&
               _appliedConstants.Opacity == value.Opacity &&
               _appliedConstants.TintColor == value.TintColor &&
               _appliedConstants.AmbientOverride.Equals(value.AmbientOverride) &&
               _appliedConstants.DiffuseOverride.Equals(value.DiffuseOverride) &&
               _appliedConstants.SpecularOverride.Equals(value.SpecularOverride) &&
               _appliedConstants.EmissiveOverride.Equals(value.EmissiveOverride) &&
               _appliedConstants.UvTranslate.Equals(value.UvTranslate) &&
               _appliedConstants.UvScale.Equals(value.UvScale) &&
               _appliedConstants.UvRotate.Equals(value.UvRotate) &&
               _appliedConstants.MaterialOpacity == value.MaterialOpacity &&
               _appliedConstants.OverrideFlags == value.OverrideFlags;
    }
}

internal sealed class ModelMeshInstance : DisposableBase
{
    internal readonly ConstantBuffer<MeshShaderResources.RenderItemConstantsVS> RenderItemConstantsBufferVS;

    public readonly ModelInstance ModelInstance;

    /// <summary>Index of this mesh's sub-object within the owning model's sub-object list.</summary>
    public readonly int SubObjectIndex;

    public readonly List<ModelMeshPartInstance> MeshPartInstances = new();

    /// <summary>Per-instance dynamic vertex buffer for morph-enabled meshes; null otherwise.</summary>
    internal readonly DeviceBuffer? MorphVertexBuffer;

    /// <summary>Current CPU morph result; only valid when MorphVertexBuffer is present.</summary>
    internal readonly MeshShaderResources.MeshVertex.Basic[]? MorphedVertices;

    private readonly ModelMorphTargetSet? _morphTargetSet;
    private readonly MeshShaderResources.MeshVertex.Basic[]? _morphBaseVertices;
    private readonly float[] _morphWeights;
    private bool _morphDirty;

    public ModelMeshInstance(
        ModelMesh modelMesh,
        ModelInstance modelInstance,
        AssetLoadContext loadContext,
        int subObjectIndex)
    {
        ModelInstance = modelInstance;
        SubObjectIndex = subObjectIndex;

        RenderItemConstantsBufferVS = AddDisposable(
            new ConstantBuffer<MeshShaderResources.RenderItemConstantsVS>(
                loadContext.GraphicsDevice,
                $"{modelMesh.SubObject.FullName}_RenderItemConstantsVS"));

        // z=1 keeps the material's own time-based UV animation (e.g. waterfalls) running;
        // tread meshes set it to 0 so W3dTankDraw can drive the texture by drive speed.
        RenderItemConstantsBufferVS.Value.UVOffset = new Vector4(0f, 0f, 1f, 0f);

        if (modelMesh.MorphTargetSet != null && modelMesh.MorphBaseVertices != null)
        {
            _morphTargetSet = modelMesh.MorphTargetSet;
            _morphBaseVertices = modelMesh.MorphBaseVertices;
            _morphWeights = new float[_morphTargetSet.TargetNames.Length];
            _morphTargetSet.DefaultWeights.AsSpan().CopyTo(_morphWeights);

            MorphedVertices = new MeshShaderResources.MeshVertex.Basic[_morphBaseVertices.Length];
            _morphTargetSet.Apply(_morphBaseVertices, _morphWeights, MorphedVertices);

            MorphVertexBuffer = AddDisposable(loadContext.GraphicsDevice.ResourceFactory.CreateBuffer(
                new BufferDescription(
                    (uint)(_morphBaseVertices.Length * MeshShaderResources.MeshVertex.BasicStride),
                    BufferUsage.VertexBuffer | BufferUsage.Dynamic)));

            _morphDirty = true;
        }
        else
        {
            _morphWeights = Array.Empty<float>();
        }

        foreach (var modelMeshPart in modelMesh.MeshParts)
        {
            MeshPartInstances.Add(
                AddDisposable(
                    new ModelMeshPartInstance(
                        modelMeshPart,
                        this,
                        loadContext)));
        }
    }

    internal void OnBeforeRender(
        CommandList cl,
        in RenderItem renderItem)
    {
        ref var value = ref RenderItemConstantsBufferVS.Value;
        if (value.World != renderItem.World || value.UVOffset != _appliedUvOffset)
        {
            value.World = renderItem.World;
            _appliedUvOffset = value.UVOffset;
            RenderItemConstantsBufferVS.Update(cl);
        }

        if (_morphDirty && MorphVertexBuffer != null && MorphedVertices != null)
        {
            cl.UpdateBuffer(
                MorphVertexBuffer,
                0,
                MorphedVertices);
            _morphDirty = false;
        }
    }

    private Vector4 _appliedUvOffset = new(0f, 0f, 1f, 0f);

    /// <summary>
    /// Sets the morph weight vector for this mesh's primitive (one weight per target, target
    /// order) and rebuilds the CPU-deformed vertices. The dynamic buffer uploads on the next
    /// render; existing skinning then runs on the morphed positions unchanged.
    /// </summary>
    internal void SetMorphWeights(ReadOnlySpan<float> weights)
    {
        if (_morphTargetSet == null)
        {
            throw new InvalidOperationException(
                $"Mesh '{ModelInstance.Model.SubObjects[SubObjectIndex].FullName}' has no morph targets.");
        }

        if (weights.Length != _morphWeights.Length)
        {
            throw new ArgumentException(
                $"Morph target set '{_morphTargetSet.PrimitiveId}' expects {_morphWeights.Length} weights but received {weights.Length}.");
        }

        weights.CopyTo(_morphWeights);
        _morphTargetSet.Apply(_morphBaseVertices, _morphWeights, MorphedVertices);
        _morphDirty = true;
    }

    /// <summary>
    /// Scroll offset (xy, texture units) for this mesh's primary texture, and a scale (z) for the
    /// material's own time-based UV animation (1 = baked scroll runs, 0 = suppressed so the draw
    /// module can drive the texture, e.g. tank treads by drive speed).
    /// </summary>
    internal void SetUvOffset(Vector2 offset, bool suppressBakedTextureScroll)
    {
        RenderItemConstantsBufferVS.Value.UVOffset =
            new Vector4(offset.X, offset.Y, suppressBakedTextureScroll ? 0f : 1f, 0f);
    }
}
