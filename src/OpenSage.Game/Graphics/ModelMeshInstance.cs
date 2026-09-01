using System.Collections.Generic;
using System.Numerics;
using OpenSage.Content.Loaders;
using OpenSage.Graphics.Rendering;
using OpenSage.Graphics.Shaders;
using Veldrid;

namespace OpenSage.Graphics;

internal sealed class ModelMeshPartInstance : DisposableBase
{
    private readonly ResourceSet _renderItemConstantsResourceSet;

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

        _renderItemConstantsResourceSet = AddDisposable(
            loadContext.ShaderResources.Mesh.CreateRenderItemConstantsResourceSet(
                modelMeshPart.ModelMesh.MeshConstantsBuffer,
                modelMeshInstance.RenderItemConstantsBufferVS,
                modelMeshInstance.ModelInstance.SkinningBuffer,
                modelMeshInstance.ModelInstance.RenderItemConstantsBufferPS));

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

        cl.SetGraphicsResourceSet(3, _renderItemConstantsResourceSet);

        ModelMeshPart.BeforeRender(cl);
    }
}

internal sealed class ModelMeshInstance : DisposableBase
{
    internal readonly ConstantBuffer<MeshShaderResources.RenderItemConstantsVS> RenderItemConstantsBufferVS;

    public readonly ModelInstance ModelInstance;

    public readonly List<ModelMeshPartInstance> MeshPartInstances = new();

    public ModelMeshInstance(
        ModelMesh modelMesh,
        ModelInstance modelInstance,
        AssetLoadContext loadContext)
    {
        ModelInstance = modelInstance;

        RenderItemConstantsBufferVS = AddDisposable(
            new ConstantBuffer<MeshShaderResources.RenderItemConstantsVS>(
                loadContext.GraphicsDevice,
                $"{modelMesh.SubObject.FullName}_RenderItemConstantsVS"));

        // z=1 keeps the material's own time-based UV animation (e.g. waterfalls) running;
        // tread meshes set it to 0 so W3dTankDraw can drive the texture by drive speed.
        RenderItemConstantsBufferVS.Value.UVOffset = new Vector4(0f, 0f, 1f, 0f);

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
    }

    private Vector4 _appliedUvOffset = new(0f, 0f, 1f, 0f);

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
