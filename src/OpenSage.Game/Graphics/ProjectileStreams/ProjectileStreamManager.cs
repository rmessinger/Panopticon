using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Content.Loaders;
using OpenSage.Graphics.Shaders;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Rendering;
using Veldrid;

namespace OpenSage.Graphics.ProjectileStreams;

/// <summary>
/// Renders the textured ribbons described by <see cref="W3dProjectileStreamDrawModuleData"/>
/// (weapon <c>ProjectileStreamName</c>: the Dragon Tank's flame, the Toxin Truck's spray, the
/// hazard-cleanup stream). A ribbon follows a live projectile object and traces its path over
/// the last MaxSegments recorded positions, camera-facing like the tracer streaks. The stream
/// object definitions are INERT drawables in the original engine; here the rendering is
/// manager-driven and consumes the same module data.
/// </summary>
internal sealed class ProjectileStreamManager : RenderObject
{
    private const int MaxRibbonPoints = 16;
    private const int MaxRibbons = 64;

    private readonly AssetLoadContext _loadContext;
    private readonly IScene3D _scene;
    private readonly RenderBucket _renderBucket;

    private readonly List<Ribbon> _ribbons = new();

    private TimeSpan _lastUpdateTime;
    private float _scrollSeconds;

    private ProjectileStreamShaderResources _shaderSet;
    private DeviceBuffer _vertexBuffer;
    private ProjectileStreamShaderResources.ProjectileStreamVertex[] _vertices;
    private DeviceBuffer _indexBuffer;
    private ushort[] _indices;

    private MaterialPass _materialPass;
    private Texture _boundTexture;

    private struct Ribbon
    {
        public ObjectId FollowObject;

        public Vector3[] Points;
        public int PointCount;

        public W3dProjectileStreamDrawModuleData Data;
        public Texture Texture;
    }

    public override string DebugName => "ProjectileStreamManager";

    public override MaterialPass MaterialPass => _materialPass;

    public override AxisAlignedBoundingBox BoundingBox =>
        AxisAlignedBoundingBox.CreateFromSphere(
            new BoundingSphere(
                _ribbons.Count > 0 ? HeadPosition(_ribbons[0]) : Vector3.Zero,
                1000));

    private static Vector3 HeadPosition(in Ribbon ribbon)
    {
        return ribbon.Points[ribbon.PointCount - 1];
    }

    public ProjectileStreamManager(IScene3D scene, AssetLoadContext loadContext)
    {
        _loadContext = loadContext;
        _scene = scene;

        // Draw after tracers.
        _renderBucket = scene.RenderScene.CreateRenderBucket("ProjectileStreams", 17);
    }

    internal void Create(GameObject projectile, W3dProjectileStreamDrawModuleData data, Texture texture)
    {
        if (_ribbons.Count >= MaxRibbons || texture == null)
        {
            return;
        }

        var ribbon = new Ribbon
        {
            FollowObject = projectile.Id,
            Points = new Vector3[MaxRibbonPoints],
            PointCount = 1,
            Data = data,
            Texture = texture,
        };
        ribbon.Points[0] = projectile.Translation;
        _ribbons.Add(ribbon);
    }

    internal void Update(in TimeInterval gameTime)
    {
        if (_lastUpdateTime != TimeSpan.Zero)
        {
            _scrollSeconds += (float)(gameTime.TotalTime - _lastUpdateTime).TotalSeconds;
        }

        _lastUpdateTime = gameTime.TotalTime;

        for (var i = _ribbons.Count - 1; i >= 0; i--)
        {
            var ribbon = _ribbons[i];

            var projectile = _scene.GameObjects.GetObjectById(ribbon.FollowObject);
            if (projectile == null || projectile.IsEffectivelyDead)
            {
                _ribbons.RemoveAt(i);
                continue;
            }

            var head = projectile.Translation;
            if (Vector3.DistanceSquared(HeadPosition(ribbon), head) > 0.01f)
            {
                if (ribbon.PointCount == MaxRibbonPoints)
                {
                    // Drop the oldest point: the ribbon is a fixed-length trail.
                    Array.Copy(ribbon.Points, 1, ribbon.Points, 0, MaxRibbonPoints - 1);
                    ribbon.PointCount--;
                }

                ribbon.Points[ribbon.PointCount++] = head;
            }

            _ribbons[i] = ribbon;
        }

        // Registration is deferred until the first ribbon: the render bucket skips objects
        // with a null material pass, and the material depends on the first ribbon's texture.
        EnsureGraphicsResources();
        if (_materialPass == null && _shaderSet != null && _ribbons.Count > 0)
        {
            var material = _shaderSet.GetMaterial(_ribbons[0].Texture);
            _materialPass = new MaterialPass(material, null);
            _boundTexture = _ribbons[0].Texture;
            _renderBucket.AddObject(this);
        }
    }

    private void EnsureGraphicsResources()
    {
        if (_shaderSet != null)
        {
            return;
        }

        var graphicsDevice = _loadContext.GraphicsDevice;
        var shaderSetStore = _loadContext.ShaderSetStore;
        if (graphicsDevice == null || shaderSetStore == null)
        {
            return;
        }

        _shaderSet = shaderSetStore.GetProjectileStreamShaderSet();

        _vertexBuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateBuffer(
            new BufferDescription(
                (uint)(ProjectileStreamShaderResources.ProjectileStreamVertex.VertexDescriptor.Stride * MaxRibbonPoints * 2),
                BufferUsage.VertexBuffer | BufferUsage.Dynamic)));
        _vertices = new ProjectileStreamShaderResources.ProjectileStreamVertex[MaxRibbonPoints * 2];

        _indexBuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateBuffer(
            new BufferDescription(
                (uint)(sizeof(ushort) * (MaxRibbonPoints - 1) * 6),
                BufferUsage.IndexBuffer | BufferUsage.Dynamic)));
        _indices = new ushort[(MaxRibbonPoints - 1) * 6];
    }

    public override void Render(CommandList commandList)
    {
        EnsureGraphicsResources();

        if (_materialPass == null || _ribbons.Count == 0)
        {
            return;
        }

        for (var r = 0; r < _ribbons.Count; r++)
        {
            var ribbon = _ribbons[r];
            if (ribbon.PointCount < 2)
            {
                continue;
            }

            // Ribbons of different weapons use different textures; rebind between groups.
            if (ribbon.Texture != _boundTexture)
            {
                var material = _shaderSet.GetMaterial(ribbon.Texture);
                _materialPass = new MaterialPass(material, null);
                commandList.SetPipeline(material.Pipeline);
                commandList.SetGraphicsResourceSet(2, material.MaterialResourceSet);
                _boundTexture = ribbon.Texture;
            }

            DrawRibbon(commandList, ribbon);
        }
    }

    private void DrawRibbon(CommandList commandList, in Ribbon ribbon)
    {
        var width = MathF.Max(ribbon.Data.Width, 0.1f);
        var tileFactor = ribbon.Data.TileFactor > 0 ? ribbon.Data.TileFactor : 1.0f;
        var scroll = _scrollSeconds * MathF.Max(ribbon.Data.ScrollRate, 0);

        var vertexIndex = 0;
        var indexCount = 0;

        // Walk tail-to-head accumulating V (texture tiles per world unit, scrolling over
        // time so the texture flows along the stream), fading the tail out; the head rides
        // the live projectile at full strength.
        var v = 0.0f;
        var previous = ribbon.Points[0];
        for (var i = 0; i < ribbon.PointCount; i++)
        {
            var point = ribbon.Points[i];
            if (i > 0)
            {
                v += Vector3.Distance(previous, point);
            }

            previous = point;

            var direction = i < ribbon.PointCount - 1
                ? ribbon.Points[i + 1] - point
                : point - ribbon.Points[i - 1];
            var directionLength = direction.Length();
            if (directionLength > 0.001f)
            {
                direction /= directionLength;
            }

            var tailFraction = i / (float)(ribbon.PointCount - 1);
            var alpha = tailFraction * tailFraction; // 0 at tail, 1 at head

            var vertex = new ProjectileStreamShaderResources.ProjectileStreamVertex
            {
                Position = point,
                Direction = direction,
                V = v * tileFactor + scroll,
                Color = new Vector4(1, 1, 1, alpha),
            };

            vertex.Side = -width * 0.5f;
            _vertices[vertexIndex++] = vertex;
            vertex.Side = width * 0.5f;
            _vertices[vertexIndex++] = vertex;

            if (i > 0)
            {
                var current = (ushort)(vertexIndex - 4); // first vertex of the previous point
                _indices[indexCount++] = (ushort)(current + 0);
                _indices[indexCount++] = (ushort)(current + 1);
                _indices[indexCount++] = (ushort)(current + 3);
                _indices[indexCount++] = (ushort)(current + 0);
                _indices[indexCount++] = (ushort)(current + 3);
                _indices[indexCount++] = (ushort)(current + 2);
            }
        }

        commandList.UpdateBuffer(_vertexBuffer, 0, _vertices.AsSpan(0, vertexIndex));
        commandList.UpdateBuffer(_indexBuffer, 0, _indices.AsSpan(0, indexCount));

        commandList.SetVertexBuffer(0, _vertexBuffer);
        commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

        commandList.DrawIndexed((uint)indexCount, 1, 0, 0, 0);
    }
}
