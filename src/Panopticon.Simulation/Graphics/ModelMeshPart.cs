using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics;

public sealed class ModelMeshPart
{
    public readonly ModelMesh ModelMesh;

    public readonly uint StartIndex;
    public readonly uint IndexCount;

    public readonly DeviceBuffer TexCoordVertexBuffer;

    public readonly bool BlendEnabled;
    public readonly MaterialPass Material;
    public readonly MaterialPass MaterialBlend;

    /// <summary>Index of this part's material within the owning mesh's material list.</summary>
    public readonly int MaterialIndex;

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

    internal void BeforeRender(CommandList commandList)
    {
        commandList.SetVertexBuffer(0, ModelMesh.VertexBuffer);

        if (TexCoordVertexBuffer != null)
        {
            commandList.SetVertexBuffer(1, TexCoordVertexBuffer);
        }
    }
}
