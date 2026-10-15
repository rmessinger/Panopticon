using System.Threading;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics;

public sealed class ModelMeshPart
{
    private static int NextInstanceId;

    public readonly ModelMesh ModelMesh;

    public readonly uint StartIndex;
    public readonly uint IndexCount;

    public readonly DeviceBuffer TexCoordVertexBuffer;

    public readonly bool BlendEnabled;
    public readonly MaterialPass Material;
    public readonly MaterialPass MaterialBlend;

    /// <summary>Index of this part's material within the owning mesh's material list.</summary>
    public readonly int MaterialIndex;

    /// <summary>
    /// Stable identity for render sorting (B5-2): all instances of one model share the part
    /// object, so its id groups repeated parts contiguously in the sorted render list.
    /// </summary>
    public readonly int InstanceId = Interlocked.Increment(ref NextInstanceId);

    internal ModelMeshPart(
        ModelMesh modelMesh,
        DeviceBuffer texCoordVertexBuffer,
        uint startIndex,
        uint indexCount,
        bool blendEnabled,
        MaterialPass material,
        MaterialPass materialBlend,
        int materialIndex)
    {
        ModelMesh = modelMesh;

        TexCoordVertexBuffer = texCoordVertexBuffer;

        StartIndex = startIndex;
        IndexCount = indexCount;

        BlendEnabled = blendEnabled;
        Material = material;
        MaterialBlend = materialBlend;
        MaterialIndex = materialIndex;
    }

    internal void BeforeRender(CommandList commandList, DeviceBuffer vertexBufferOverride = null)
    {
        commandList.SetVertexBuffer(0, vertexBufferOverride ?? ModelMesh.VertexBuffer);

        if (TexCoordVertexBuffer != null)
        {
            commandList.SetVertexBuffer(1, TexCoordVertexBuffer);
        }
    }
}
