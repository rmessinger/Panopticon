using System;
using System.Numerics;
using Panopticon.Graphics.Rendering.Instancing;
using Panopticon.Mathematics;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Rendering;

internal readonly struct RenderItem : IComparable<RenderItem>
{
    public readonly string DebugName;

    public readonly Material Material;
    public readonly AxisAlignedBoundingBox BoundingBox;
    public readonly Matrix4x4 World;
    public readonly BeforeRenderDelegate BeforeRenderCallback;
    public readonly uint StartIndex;
    public readonly uint IndexCount;
    public readonly DeviceBuffer IndexBuffer;

    /// <summary>
    /// B5-2: this item may join a GPU-instanced batch. Requires a mesh that is rigid (no
    /// per-instance skinning buffer), not morphing, not camera-oriented, drawn opaque with
    /// default per-item constants, on a material that has an instanced pipeline variant.
    /// </summary>
    public readonly bool InstancingEligible;

    /// <summary>Per-instance payload for eligible items; unused otherwise.</summary>
    public readonly InstanceData InstancePayload;

    /// <summary>The mesh's shared constants buffer, needed to bind an instanced item set.</summary>
    public readonly DeviceBuffer MeshConstantsBuffer;

    /// <summary>The mesh's position/normal/tangent vertex buffer (slot 0), for instanced runs.</summary>
    public readonly DeviceBuffer VertexBuffer;

    /// <summary>The part's texture-coordinate vertex buffer (slot 1); null when the mesh has none.</summary>
    public readonly DeviceBuffer TexCoordVertexBuffer;

    public readonly int Key;

    public RenderItem(
        string debugName,
        Material material,
        in AxisAlignedBoundingBox boundingBox,
        in Matrix4x4 world,
        uint startIndex,
        uint indexCount,
        DeviceBuffer indexBuffer,
        BeforeRenderDelegate beforeRenderCallback)
        : this(
            debugName,
            material,
            boundingBox,
            world,
            startIndex,
            indexCount,
            indexBuffer,
            beforeRenderCallback,
            instancingEligible: false,
            instancePayload: default,
            meshConstantsBuffer: null,
            meshPartInstanceId: 0)
    {
    }

    public RenderItem(
        string debugName,
        Material material,
        in AxisAlignedBoundingBox boundingBox,
        in Matrix4x4 world,
        uint startIndex,
        uint indexCount,
        DeviceBuffer indexBuffer,
        BeforeRenderDelegate beforeRenderCallback,
        bool instancingEligible,
        in InstanceData instancePayload,
        DeviceBuffer meshConstantsBuffer,
        int meshPartInstanceId,
        DeviceBuffer vertexBuffer = null,
        DeviceBuffer texCoordVertexBuffer = null)
    {
        DebugName = debugName;
        Material = material;
        BoundingBox = boundingBox;
        World = world;
        StartIndex = startIndex;
        IndexCount = indexCount;
        IndexBuffer = indexBuffer;
        BeforeRenderCallback = beforeRenderCallback;
        InstancingEligible = instancingEligible;
        InstancePayload = instancePayload;
        MeshConstantsBuffer = meshConstantsBuffer;
        VertexBuffer = vertexBuffer;
        TexCoordVertexBuffer = texCoordVertexBuffer;

        // Key.
        Key = 0;

        // Bit 24-31: ShaderSet
        Key |= (material.ShaderSet.Id << 24);

        // Bit 16-23: Material
        Key |= (material.Id) << 16;

        // Bit 0-15: mesh-part identity, so eligible items of one repeated part sort
        // contiguously and form maximal instanced runs (B5-2).
        if (instancingEligible)
        {
            Key |= meshPartInstanceId & 0xFFFF;
        }

        // TODO: Vertex buffer?
    }

    /// <summary>Run identity: same pipeline, index buffer, and index range can batch.</summary>
    public bool JoinsInstancedRunWith(in RenderItem other)
    {
        return Material.InstancedPipeline == other.Material.InstancedPipeline
            && IndexBuffer == other.IndexBuffer
            && StartIndex == other.StartIndex
            && IndexCount == other.IndexCount;
    }

    int IComparable<RenderItem>.CompareTo(RenderItem other)
    {
        return Key.CompareTo(other.Key);
    }
}

internal delegate void BeforeRenderDelegate(CommandList commandList, in RenderItem renderItem);
