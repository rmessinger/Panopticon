using System.Collections.Generic;
using Panopticon.Content.Loaders;
using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Rendering;

namespace Panopticon.Terrain.Decals;

internal sealed class TerrainDecalRenderer : DisposableBase
{
    private readonly AssetLoadContext _context;
    private readonly HeightMap _heightMap;
    private readonly TerrainDecalStore _store;
    private readonly RenderBucket _bucket;
    private readonly ConstantBuffer<TerrainDecalShaderResources.DecalConstants> _constants;
    private readonly List<TerrainDecalRenderObject> _objects = new();
    private int _version = -1;
    private bool _dirty;
    internal int MeshBuildCount { get; private set; }

    internal TerrainDecalRenderer(AssetLoadContext context, HeightMap heightMap, TerrainDecalStore store, RenderScene scene)
    {
        _context = context;
        _heightMap = heightMap;
        _store = store;
        _bucket = scene.CreateRenderBucket("TerrainDecals", 5);
        _constants = AddDisposable(new ConstantBuffer<TerrainDecalShaderResources.DecalConstants>(context.GraphicsDevice, "TerrainDecalConstants"));
    }

    internal void Invalidate() => _dirty = true;

    internal void ApplyWorldSampler(Veldrid.Sampler sampler)
    {
        foreach (var renderObject in _objects)
        {
            renderObject.ApplyWorldSampler(sampler);
        }
    }

    internal void Invalidate(RectangleF bounds)
    {
        foreach (var decal in _store.Decals)
        {
            var radius = decal.Radius + HeightMap.HorizontalScale;
            if (bounds.Intersects(new RectangleF(decal.Position.X - radius, decal.Position.Y - radius, radius * 2, radius * 2)))
            {
                _dirty = true;
                return;
            }
        }
    }

    internal void Update(GraphicsSettings settings)
    {
        _store.Limit = settings.TerrainDecalLimit;
        _constants.Value.TerrainLit = settings.TerrainDecalLighting == TerrainDecalLighting.TerrainLit ? 1 : 0;
        _constants.Update(_context.GraphicsDevice);
        if (_version == _store.Version && !_dirty)
        {
            return;
        }

        ClearObjects();
        // Preserve global alpha order even when neighbouring marks use different atlases.
        var end = _store.Count;
        while (end > 0)
        {
            var template = _store.Decals[end - 1].Template;
            var start = end - 1;
            while (start > 0 && _store.Decals[start - 1].Template == template)
            {
                start--;
            }

            var run = new List<TerrainDecal>(end - start);
            for (var index = start; index < end; index++)
            {
                run.Add(_store.Decals[index]);
            }

            var mesh = TerrainDecalMesher.Build(_heightMap, run);
            if (mesh.Indices.Length > 0)
            {
                var renderObject = new TerrainDecalRenderObject(_context, mesh, template, _constants.Buffer);
                _bucket.AddObject(renderObject);
                _objects.Add(renderObject);
            }

            end = start;
        }

        _version = _store.Version;
        _dirty = false;
        MeshBuildCount++;
    }

    private void ClearObjects()
    {
        foreach (var renderObject in _objects)
        {
            _bucket.RemoveObject(renderObject);
            _context.GraphicsDevice.DisposeWhenIdle(renderObject);
        }

        _objects.Clear();
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            ClearObjects();
        }

        base.Dispose(disposeManagedResources);
    }
}
