using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSage.Content.Loaders;
using OpenSage.Graphics.Shaders;
using OpenSage.Mathematics;
using OpenSage.Rendering;
using OpenSage.Utilities.Extensions;
using Veldrid;

namespace OpenSage.Graphics.Tracers;

/// <summary>
/// Client-side owner of bullet tracers spawned by <see cref="OpenSage.FX.TracerFXNugget"/>.
/// Tracers are pure visuals: no logic object is created, and the simulation state is untouched.
/// Under data-only loading (null <see cref="AssetLoadContext.GraphicsDevice"/>) the manager still
/// spawns and expires tracers, but never builds render resources.
/// </summary>
internal sealed class TracerManager : RenderObject
{
    private const int MaxTracers = 512;

    // The nugget data carries no flight range; tracers simply overshoot typical weapon
    // ranges and fade out along the way, matching the original game's look.
    private const float MaxFlightDistance = 400.0f;

    private const float DefaultSpeed = 300.0f;

    private readonly AssetLoadContext _loadContext;
    private readonly RenderBucket _renderBucket;

    private readonly List<Tracer> _tracers = new();

    private TimeSpan _lastUpdateTime;

    private DeviceBuffer _vertexBuffer;
    private TracerShaderResources.TracerVertex[] _vertices;
    private DeviceBuffer _indexBuffer;
    private bool _graphicsInitialized;

    private struct Tracer
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public Vector3 Color;
        public float Traveled;
        public float Speed;
        public float Length;
        public float Width;
        public float DecayAt;
    }

    public override string DebugName => "TracerManager";

    private MaterialPass _materialPass;

    public override MaterialPass MaterialPass => _materialPass;

    public override AxisAlignedBoundingBox BoundingBox =>
        AxisAlignedBoundingBox.CreateFromSphere(
            new BoundingSphere(
                _tracers.Count > 0 ? _tracers[0].Origin : Vector3.Zero,
                MaxFlightDistance));

    public TracerManager(IScene3D scene, AssetLoadContext loadContext)
    {
        _loadContext = loadContext;

        // Draw after particles.
        _renderBucket = scene.RenderScene.CreateRenderBucket("Tracers", 16);
    }

    internal void Create(
        in Vector3 position,
        in Vector3 direction,
        in ColorRgb color,
        float length,
        float width,
        int speed,
        float decayAt)
    {
        if (_tracers.Count >= MaxTracers)
        {
            return;
        }

        var normalizedDirection = direction.LengthSquared() < 1e-6f
            ? Vector3.UnitX
            : Vector3.Normalize(direction);

        _tracers.Add(new Tracer
        {
            Origin = position,
            Direction = normalizedDirection,
            Color = color.ToVector3(),
            Speed = speed > 0 ? speed : DefaultSpeed,
            Length = MathF.Max(length, 0.1f),
            Width = MathF.Max(width, 0.05f),
            DecayAt = decayAt > 0 && decayAt <= 1 ? decayAt : 1.0f,
        });
    }

    internal void Update(in TimeInterval gameTime)
    {
        EnsureGraphicsResources();

        if (_lastUpdateTime != TimeSpan.Zero)
        {
            var deltaTime = (float)(gameTime.TotalTime - _lastUpdateTime).TotalSeconds;
            if (deltaTime > 0)
            {
                for (var i = 0; i < _tracers.Count; i++)
                {
                    var tracer = _tracers[i];
                    tracer.Traveled += tracer.Speed * deltaTime;
                    _tracers[i] = tracer;
                }

                for (var i = _tracers.Count - 1; i >= 0; i--)
                {
                    if (_tracers[i].Traveled >= MaxFlightDistance)
                    {
                        _tracers.RemoveAt(i);
                    }
                }
            }
        }

        _lastUpdateTime = gameTime.TotalTime;
    }

    private void EnsureGraphicsResources()
    {
        if (_graphicsInitialized)
        {
            return;
        }

        var graphicsDevice = _loadContext.GraphicsDevice;
        var shaderSetStore = _loadContext.ShaderSetStore;
        if (graphicsDevice == null || shaderSetStore == null)
        {
            return;
        }

        var shaderSet = shaderSetStore.GetTracerShaderSet();

        _materialPass = new MaterialPass(shaderSet.Material, null);

        var numVertices = MaxTracers * 4;
        _vertexBuffer = AddDisposable(graphicsDevice.ResourceFactory.CreateBuffer(
            new BufferDescription(
                (uint)(TracerShaderResources.TracerVertex.VertexDescriptor.Stride * numVertices),
                BufferUsage.VertexBuffer | BufferUsage.Dynamic)));
        _vertices = new TracerShaderResources.TracerVertex[numVertices];

        _indexBuffer = AddDisposable(CreateIndexBuffer(graphicsDevice));

        _renderBucket.AddObject(this);

        _graphicsInitialized = true;
    }

    private DeviceBuffer CreateIndexBuffer(GraphicsDevice graphicsDevice)
    {
        var numIndices = (uint)MaxTracers * 2 * 3;
        var indices = new ushort[numIndices];
        var indexCounter = 0;
        for (ushort i = 0; i < MaxTracers * 4; i += 4)
        {
            indices[indexCounter++] = (ushort)(i + 0);
            indices[indexCounter++] = (ushort)(i + 2);
            indices[indexCounter++] = (ushort)(i + 1);

            indices[indexCounter++] = (ushort)(i + 1);
            indices[indexCounter++] = (ushort)(i + 2);
            indices[indexCounter++] = (ushort)(i + 3);
        }

        return graphicsDevice.CreateStaticBuffer(
            indices,
            BufferUsage.IndexBuffer);
    }

    public override void Render(CommandList commandList)
    {
        if (_tracers.Count == 0)
        {
            return;
        }

        var vertexIndex = 0;
        foreach (var tracer in _tracers)
        {
            var alpha = 1.0f;
            var flightFraction = tracer.Traveled / MaxFlightDistance;
            if (flightFraction > tracer.DecayAt)
            {
                var fadeRange = 1.0f - tracer.DecayAt;
                alpha = fadeRange > 0
                    ? MathF.Max(1.0f - (flightFraction - tracer.DecayAt) / fadeRange, 0.0f)
                    : 0.0f;
            }

            var head = tracer.Origin + tracer.Direction * tracer.Traveled;

            var tracerVertex = new TracerShaderResources.TracerVertex
            {
                Position = head,
                Direction = tracer.Direction,
                Color = new Vector4(tracer.Color, alpha),
                Length = MathF.Min(tracer.Length, tracer.Traveled),
                Width = tracer.Width,
            };

            _vertices[vertexIndex++] = tracerVertex;
            _vertices[vertexIndex++] = tracerVertex;
            _vertices[vertexIndex++] = tracerVertex;
            _vertices[vertexIndex++] = tracerVertex;

            if (vertexIndex >= _vertices.Length)
            {
                break;
            }
        }

        commandList.UpdateBuffer(_vertexBuffer, 0, _vertices);

        commandList.SetVertexBuffer(0, _vertexBuffer);

        commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

        var numIndices = (uint)(vertexIndex / 4) * 6;
        commandList.DrawIndexed(numIndices, 1, 0, 0, 0);
    }
}
