using System.Linq;
using Panopticon.Content.Loaders;
using Panopticon.Mathematics;
using Panopticon.Rendering;
using Panopticon.Utilities.Extensions;
using Veldrid;

namespace Panopticon.Terrain.Decals;

internal sealed class TerrainDecalRenderObject : RenderObject
{
    private readonly DeviceBuffer _vertices;
    private readonly DeviceBuffer _indices;
    private readonly uint _indexCount;

    public override string DebugName => "TerrainDecals";
    public override MaterialPass MaterialPass { get; }
    public override AxisAlignedBoundingBox BoundingBox { get; }

    internal TerrainDecalRenderObject(AssetLoadContext context, TerrainDecalMesh mesh, TerrainDecalTemplate template)
    {
        BoundingBox = AxisAlignedBoundingBox.CreateFromPoints(mesh.Vertices.Select(vertex => vertex.Position));
        _vertices = AddDisposable(context.GraphicsDevice.CreateStaticBuffer(mesh.Vertices, BufferUsage.VertexBuffer));
        _indices = AddDisposable(context.GraphicsDevice.CreateStaticBuffer(mesh.Indices, BufferUsage.IndexBuffer));
        _indexCount = (uint)mesh.Indices.Length;
        var shaders = context.ShaderResources.TerrainDecal;
        var texture = context.AssetStore.Textures.GetByName(template.TextureName);
        var set = AddDisposable(shaders.CreateMaterialResourceSet(texture, context.StandardGraphicsResources.WorldAnisoSampler));
        var material = AddDisposable(new Material(shaders, shaders.Pipeline, set, SurfaceType.Transparent));
        MaterialPass = new MaterialPass(material, null);
    }

    public override void Render(CommandList commandList)
    {
        commandList.SetVertexBuffer(0, _vertices);
        commandList.SetIndexBuffer(_indices, IndexFormat.UInt32);
        commandList.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }
}
