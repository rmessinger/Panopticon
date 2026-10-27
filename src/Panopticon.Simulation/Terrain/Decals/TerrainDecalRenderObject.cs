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
    private readonly AssetLoadContext _context;
    private readonly Texture _texture;
    private readonly DeviceBuffer _constants;
    private readonly Material _material;

    public override string DebugName => "TerrainDecals";
    public override MaterialPass MaterialPass { get; }
    public override AxisAlignedBoundingBox BoundingBox { get; }

    internal TerrainDecalRenderObject(AssetLoadContext context, TerrainDecalMesh mesh, TerrainDecalTemplate template, DeviceBuffer constants)
    {
        _context = context;
        _constants = constants;
        BoundingBox = AxisAlignedBoundingBox.CreateFromPoints(mesh.Vertices.Select(vertex => vertex.Position));
        _vertices = AddDisposable(context.GraphicsDevice.CreateStaticBuffer(mesh.Vertices, BufferUsage.VertexBuffer));
        _indices = AddDisposable(context.GraphicsDevice.CreateStaticBuffer(mesh.Indices, BufferUsage.IndexBuffer));
        _indexCount = (uint)mesh.Indices.Length;
        var shaders = context.ShaderResources.TerrainDecal;
        _texture = context.AssetStore.Textures.GetByName(template.TextureName);
        var set = AddDisposable(shaders.CreateMaterialResourceSet(_texture, context.StandardGraphicsResources.WorldAnisoSampler, constants));
        _material = AddDisposable(new Material(shaders, shaders.Pipeline, set, SurfaceType.Transparent));
        MaterialPass = new MaterialPass(_material, null);
    }

    internal void ApplyWorldSampler(Sampler sampler)
    {
        var oldSet = _material.MaterialResourceSet;
        _material.UpdateMaterialResourceSet(AddDisposable(_context.ShaderResources.TerrainDecal.CreateMaterialResourceSet(_texture, sampler, _constants)));
        RemoveAndDispose(ref oldSet);
    }

    public override void Render(CommandList commandList)
    {
        commandList.SetVertexBuffer(0, _vertices);
        commandList.SetIndexBuffer(_indices, IndexFormat.UInt32);
        commandList.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }
}
