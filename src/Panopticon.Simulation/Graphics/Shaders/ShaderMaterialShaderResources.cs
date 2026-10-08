using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal abstract class ShaderMaterialShaderResources : ShaderSetBase
{
    private readonly Dictionary<string, Material> _materialCache = new();

    public readonly Dictionary<string, ResourceBinding> MaterialResourceBindings;

    public readonly Pipeline Pipeline;

    /// <summary>
    /// Pipeline variant for single-sample framebuffers (water maps); equals
    /// <see cref="Pipeline"/> when the store itself is single-sample.
    /// </summary>
    public readonly Pipeline SingleSamplePipeline;

    protected ShaderMaterialShaderResources(
        ShaderSetStore store,
        string shaderName,
        Func<IEnumerable<ResourceBinding>> createMaterialResourceBindings)
        : base(store, shaderName, MeshShaderResources.MeshVertex.VertexDescriptors)
    {
        var materialResourceBindings = createMaterialResourceBindings().ToArray();

        MaterialResourceBindings = materialResourceBindings.ToDictionary(x => x.Description.Name);

        Pipeline CreatePipeline(OutputDescription outputs) => AddDisposable(
            GraphicsDevice.ResourceFactory.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    BlendStateDescription.SingleDisabled,
                    DepthStencilStateDescription.DepthOnlyLessEqual,
                    RasterizerStateDescriptionUtility.DefaultFrontIsCounterClockwise,
                    PrimitiveTopology.TriangleList,
                    Description,
                    ResourceLayouts,
                    outputs)));

        Pipeline = CreatePipeline(store.OutputDescription);

        SingleSamplePipeline = store.OutputDescription.SampleCount == TextureSampleCount.Count1
            ? Pipeline
            : CreatePipeline(store.SingleSampleOutputDescription);
    }


    /// <summary>
    /// Format-neutral materialization entry: applies every imported property to the matching
    /// resource-set setter, resolving texture names through the asset store.
    /// </summary>
    public Material GetCachedMaterial(ImportedMaterial material, AssetLoadContext context)
    {
        var key = GetCacheKey(material);

        if (!_materialCache.TryGetValue(key, out var result))
        {
            var materialResourceSet = CreateMaterialResourceSet(material.Properties, context);

            result = AddDisposable(
                new Material(
                    this,
                    Pipeline,
                    SingleSamplePipeline,
                    materialResourceSet,
                    SurfaceType.Opaque)); // TODO

            _materialCache.Add(key, result);
        }

        return result;
    }



    public ResourceSet CreateMaterialResourceSet(BindableResource[] resources)
    {
        return GraphicsDevice.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(
                MaterialResourceLayout,
                resources));
    }

    private static string GetCacheKey(ImportedMaterial material)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        sha256.AppendData(BitConverter.GetBytes(material.Properties.Count));

        foreach (var property in material.Properties)
        {
            sha256.AppendData(BitConverter.GetBytes((int)property.Kind));
            sha256.AppendData(Encoding.UTF8.GetBytes(property.Name));
            sha256.AppendData(Encoding.UTF8.GetBytes(
                property.Kind == ImportedMaterialPropertyKind.TextureName
                    ? property.TextureName
                    : ""));
            AppendPropertyValue(sha256, property);
        }

        return ToHexString(sha256.GetCurrentHash());
    }

    private static void AppendPropertyValue(IncrementalHash sha256, ImportedMaterialProperty property)
    {
        switch (property.Kind)
        {
            case ImportedMaterialPropertyKind.Bool:
                sha256.AppendData(BitConverter.GetBytes(property.BoolValue));
                break;

            case ImportedMaterialPropertyKind.Float:
                sha256.AppendData(BitConverter.GetBytes(property.FloatValue));
                break;

            case ImportedMaterialPropertyKind.Vector2:
                sha256.AppendData(BitConverter.GetBytes(property.Vector2Value.X));
                sha256.AppendData(BitConverter.GetBytes(property.Vector2Value.Y));
                break;

            case ImportedMaterialPropertyKind.Vector3:
                sha256.AppendData(BitConverter.GetBytes(property.Vector3Value.X));
                sha256.AppendData(BitConverter.GetBytes(property.Vector3Value.Y));
                sha256.AppendData(BitConverter.GetBytes(property.Vector3Value.Z));
                break;

            case ImportedMaterialPropertyKind.Vector4:
                sha256.AppendData(BitConverter.GetBytes(property.Vector4Value.X));
                sha256.AppendData(BitConverter.GetBytes(property.Vector4Value.Y));
                sha256.AppendData(BitConverter.GetBytes(property.Vector4Value.Z));
                sha256.AppendData(BitConverter.GetBytes(property.Vector4Value.W));
                break;

            case ImportedMaterialPropertyKind.Int32:
                sha256.AppendData(BitConverter.GetBytes(property.Int32Value));
                break;
        }
    }

    private ResourceSet CreateMaterialResourceSet(
        IReadOnlyList<ImportedMaterialProperty> properties,
        AssetLoadContext context)
    {
        var materialResourceSetBuilder = AddDisposable(new ShaderMaterialResourceSetBuilder(
            GraphicsDevice,
            this));

        foreach (var property in properties)
        {
            switch (property.Kind)
            {
                case ImportedMaterialPropertyKind.TextureName:
                    var texture = context.AssetStore.Textures.GetByName(property.TextureName)
                        ?? context.StandardGraphicsResources.PlaceholderTexture;
                    materialResourceSetBuilder.SetTexture(property.Name, texture);
                    break;

                case ImportedMaterialPropertyKind.Bool:
                    materialResourceSetBuilder.SetConstant(property.Name, property.BoolValue);
                    break;

                case ImportedMaterialPropertyKind.Float:
                    materialResourceSetBuilder.SetConstant(property.Name, property.FloatValue);
                    break;

                case ImportedMaterialPropertyKind.Vector2:
                    materialResourceSetBuilder.SetConstant(property.Name, property.Vector2Value);
                    break;

                case ImportedMaterialPropertyKind.Vector3:
                    materialResourceSetBuilder.SetConstant(property.Name, property.Vector3Value);
                    break;

                case ImportedMaterialPropertyKind.Vector4:
                    materialResourceSetBuilder.SetConstant(property.Name, property.Vector4Value);
                    break;

                case ImportedMaterialPropertyKind.Int32:
                    materialResourceSetBuilder.SetConstant(property.Name, property.Int32Value);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported material property kind '{property.Kind}'.");
            }
        }

        return materialResourceSetBuilder.CreateResourceSet();
    }

    private static string ToHexString(byte[] hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }
}
