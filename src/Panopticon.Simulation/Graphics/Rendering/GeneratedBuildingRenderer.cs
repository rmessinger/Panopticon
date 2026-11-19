using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing.Maps;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Rendering;
using Panopticon.Utilities.Extensions;
using Veldrid;

namespace Panopticon.Graphics.Rendering;

public sealed class GeneratedBuildingRenderer : DisposableBase
{
    private const int MaxVerticesPerSegment = ushort.MaxValue;

    private readonly List<Segment> _segments = new();
    private RenderPipeline _pipeline;

    // Test-only observation point: fires once per created segment with every owned handle so
    // lifecycle tests can verify disposal and inject failures without reflection; null by default.
    internal static Action<IReadOnlyList<IDisposable>> TestSegmentHandlesCreated;

    internal GeneratedBuildingRenderer(GraphicsLoadContext loadContext, BuildingLayerSet buildings)
    {
        try
        {
            foreach (var chunk in buildings.MeshChunks)
            {
                foreach (var segment in chunk.Segments)
                {
                    _segments.Add(CreateSegment(loadContext, chunk, segment));
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Attach(RenderPipeline pipeline)
    {
        _pipeline = pipeline;
        pipeline.BuildingRenderList += OnBuildingRenderList;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (_pipeline != null)
        {
            _pipeline.BuildingRenderList -= OnBuildingRenderList;
            _pipeline = null;
        }

        base.Dispose(disposeManagedResources);
    }

    private void OnBuildingRenderList(object sender, BuildingRenderListEventArgs args)
    {
        foreach (var segment in _segments)
        {
            args.RenderList.Opaque.RenderItems.Add(new RenderItem(
                segment.DebugName,
                segment.Material,
                segment.BoundingBox,
                Matrix4x4.Identity,
                0,
                segment.IndexCount,
                segment.IndexBuffer,
                segment.BeforeRender));

            args.RenderList.Shadow.RenderItems.Add(new RenderItem(
                segment.DebugName,
                segment.ShadowMaterial,
                segment.BoundingBox,
                Matrix4x4.Identity,
                0,
                segment.IndexCount,
                segment.IndexBuffer,
                segment.BeforeRender));
        }
    }

    private Segment CreateSegment(
        GraphicsLoadContext loadContext,
        BuildingMeshChunk chunk,
        BuildingMeshSegment segment)
    {
        if (segment.Vertices.Count > MaxVerticesPerSegment)
        {
            throw new InvalidDataException(
                $"Generated building segment in chunk {chunk.ChunkX},{chunk.ChunkY} carries {segment.Vertices.Count} vertices; the limit is {MaxVerticesPerSegment}.");
        }

        var graphicsDevice = loadContext.GraphicsDevice;

        var vertices = new MeshShaderResources.MeshVertex.Basic[segment.Vertices.Count];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = segment.Vertices[i];
            var position = new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
            var normal = new Vector3((float)vertex.NormalX, (float)vertex.NormalY, (float)vertex.NormalZ);

            vertices[i] = new MeshShaderResources.MeshVertex.Basic
            {
                Position0 = position,
                Position1 = position,
                Normal0 = normal,
                Normal1 = normal,
                BoneWeight0 = 1f,
            };
        }

        var indices = new ushort[segment.Indices.Count];
        for (var i = 0; i < indices.Length; i++)
        {
            var index = segment.Indices[i];
            if (index >= vertices.Length)
            {
                throw new InvalidDataException(
                    $"Generated building segment in chunk {chunk.ChunkX},{chunk.ChunkY} index {i} references vertex {index} outside its {vertices.Length} vertices.");
            }

            indices[i] = (ushort)index;
        }

        var texCoords = new MeshShaderResources.MeshVertex.TexCoords[vertices.Length];

        var vertexBuffer = AddDisposable(graphicsDevice.CreateStaticBuffer(vertices, BufferUsage.VertexBuffer));
        var texCoordBuffer = AddDisposable(graphicsDevice.CreateStaticBuffer(texCoords, BufferUsage.VertexBuffer));
        var indexBuffer = AddDisposable(graphicsDevice.CreateStaticBuffer(indices, BufferUsage.IndexBuffer));

        var renderItemConstantsVS = AddDisposable(new ConstantBuffer<MeshShaderResources.RenderItemConstantsVS>(
            graphicsDevice,
            "GeneratedBuilding.RenderItemConstantsVS"));
        renderItemConstantsVS.Value = new MeshShaderResources.RenderItemConstantsVS
        {
            World = Matrix4x4.Identity,
        };
        renderItemConstantsVS.Update(graphicsDevice);

        var renderItemConstantsPS = AddDisposable(new ConstantBuffer<MeshShaderResources.RenderItemConstantsPS>(
            graphicsDevice,
            "GeneratedBuilding.RenderItemConstantsPS"));
        renderItemConstantsPS.Value = new MeshShaderResources.RenderItemConstantsPS
        {
            HouseColor = Vector3.One,
            Opacity = 1f,
            TintColor = Vector3.One,
        };
        renderItemConstantsPS.Update(graphicsDevice);

        var meshShaderResources = loadContext.ShaderResources.Mesh;
        var resourceSet = AddDisposable(meshShaderResources.CreateRenderItemConstantsResourceSet(
            meshShaderResources.GetCachedMeshConstantsBuffer(isSkinned: false, hasHouseColor: false),
            renderItemConstantsVS,
            loadContext.StandardGraphicsResources.GetNullStructuredBuffer(64),
            renderItemConstantsPS));

        var material = CreateForwardMaterial(loadContext, segment.MaterialVariant);

        TestSegmentHandlesCreated?.Invoke([vertexBuffer, texCoordBuffer, indexBuffer, renderItemConstantsVS, renderItemConstantsPS, resourceSet]);

        return new Segment(
            $"GeneratedBuilding chunk {chunk.ChunkX},{chunk.ChunkY} variant {segment.MaterialVariant}",
            material,
            loadContext.ShaderResources.MeshDepth.Material,
            new AxisAlignedBoundingBox(
                new Vector3((float)segment.MinX, (float)segment.MinY, (float)segment.MinZ),
                new Vector3((float)segment.MaxX, (float)segment.MaxY, (float)segment.MaxZ)),
            indexBuffer,
            (uint)indices.Length,
            (CommandList commandList, in RenderItem renderItem) =>
            {
                commandList.SetGraphicsResourceSet(3, resourceSet);
                commandList.SetVertexBuffer(0, vertexBuffer);
                commandList.SetVertexBuffer(1, texCoordBuffer);
            });
    }

    private static Material CreateForwardMaterial(GraphicsLoadContext loadContext, int materialVariant)
    {
        var diffuse = GeneratedBuildingPalette.ColorFor(materialVariant);
        var nullTexture = loadContext.StandardGraphicsResources.NullTexture;

        var constants = new FixedFunctionShaderResources.MaterialConstantsType
        {
            NumTextureStages = 0,
            Material = new FixedFunctionShaderResources.VertexMaterial
            {
                Ambient = diffuse * 0.4f,
                Diffuse = diffuse,
                Specular = Vector3.Zero,
                Emissive = Vector3.Zero,
                Shininess = 0f,
                Opacity = 1f,
            },
            Shading = new FixedFunctionShaderResources.ShadingConfiguration
            {
                DiffuseLightingType = FixedFunctionShaderResources.DiffuseLightingType.Modulate,
                SpecularEnabled = false,
                TexturingEnabled = false,
                SecondaryTextureColorBlend = FixedFunctionShaderResources.SecondaryTextureBlend.Disable,
                SecondaryTextureAlphaBlend = FixedFunctionShaderResources.SecondaryTextureBlend.Disable,
                AlphaTest = false,
            },
        };

        return loadContext.ShaderResources.FixedFunction.GetCachedMaterial(
            FaceCullMode.Back,
            depthWriteEnabled: true,
            ComparisonKind.LessEqual,
            blendEnabled: false,
            BlendFactor.One,
            BlendFactor.Zero,
            BlendFactor.Zero,
            constants,
            nullTexture,
            nullTexture);
    }

    private readonly record struct Segment(
        string DebugName,
        Material Material,
        Material ShadowMaterial,
        AxisAlignedBoundingBox BoundingBox,
        DeviceBuffer IndexBuffer,
        uint IndexCount,
        BeforeRenderDelegate BeforeRender);
}
