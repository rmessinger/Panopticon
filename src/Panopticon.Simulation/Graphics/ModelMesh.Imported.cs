using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Panopticon.Content.Importing;
using Panopticon.Content.Loaders;
using Panopticon.Graphics.Shaders;
using Panopticon.Mathematics;
using Panopticon.Rendering;
using Panopticon.Utilities.Extensions;
using Veldrid;

namespace Panopticon.Graphics;

partial class ModelMesh
{
    // Format-neutral materialization constructor, called by ImportedModelMaterializer.
    // Parity: constructs the same runtime state as the W3D constructor in ModelMesh.W3d.cs.
    internal ModelMesh(string name, ImportedMesh importedMesh, AssetLoadContext loadContext)
    {
        SetNameAndInstanceId("W3DMesh", name);

        MeshParts = new List<ModelMeshPart>();
        foreach (var importedPart in importedMesh.MeshParts)
        {
            MeshParts.Add(CreateModelMeshPart(importedMesh, importedPart, loadContext));
        }

        MaterialCount = importedMesh.Materials.Count;

        _boundingBox = importedMesh.BoundingBox;
        _boundingSphere = importedMesh.BoundingSphere;

        Skinned = importedMesh.IsSkinned;
        Hidden = importedMesh.Hidden;
        CameraOriented = importedMesh.CameraOriented;

        var vertices = CreateVertices(importedMesh.Vertices);

        if (importedMesh.MorphTargetSets.Count > 0)
        {
            MorphTargetSet = ModelMorphTargetSet.Create(importedMesh.MorphTargetSets[0], vertices.Length);
            MorphBaseVertices = vertices;
        }

        VertexBuffer = AddDisposable(loadContext.GraphicsDevice.CreateStaticBuffer(
            MemoryMarshal.AsBytes(new ReadOnlySpan<MeshShaderResources.MeshVertex.Basic>(vertices)),
            BufferUsage.VertexBuffer));

        _indexBuffer = AddDisposable(loadContext.GraphicsDevice.CreateStaticBuffer(
            importedMesh.Indices.ToArray(),
            BufferUsage.IndexBuffer));

        MeshConstantsBuffer = loadContext.ShaderResources.Mesh.GetCachedMeshConstantsBuffer(
            importedMesh.IsSkinned,
            importedMesh.HasHouseColor);
    }

    // Internal for the Position1/Normal1 fallback regression test (secondary-position skinning).
    internal static MeshShaderResources.MeshVertex.Basic[] CreateVertices(IReadOnlyList<ImportedVertex> vertices)
    {
        var verticesOut = new MeshShaderResources.MeshVertex.Basic[vertices.Count];

        for (var i = 0; i < verticesOut.Length; i++)
        {
            var importedVertex = vertices[i];

            verticesOut[i] = new MeshShaderResources.MeshVertex.Basic
            {
                Position0 = importedVertex.Position,
                // The second influence evaluates in_Position1 (W3D two-position skinning);
                // sources without a secondary position deform at the primary position.
                Position1 = importedVertex.SecondaryPosition ?? importedVertex.Position,
                Normal0 = importedVertex.Normal,
                Normal1 = importedVertex.SecondaryNormal ?? importedVertex.Normal,
                Tangent = importedVertex.Tangent ?? Vector3.Zero,
                Binormal = importedVertex.Binormal ?? Vector3.Zero,
                BoneIndex0 = importedVertex.BoneIndex0,
                BoneIndex1 = importedVertex.BoneIndex1,
                BoneWeight0 = importedVertex.BoneWeight0,
                BoneWeight1 = importedVertex.BoneWeight1,
                BoneIndex2 = importedVertex.BoneIndex2,
                BoneIndex3 = importedVertex.BoneIndex3,
                BoneWeight2 = importedVertex.BoneWeight2,
                BoneWeight3 = importedVertex.BoneWeight3,
            };
        }

        return verticesOut;
    }

    private Material CreatePbrMaterial(
        ImportedPbrMetallicRoughness payload,
        AssetLoadContext context)
    {
        var textureMaterializer = ImportedTextureMaterializer.GetShared(context);
        var shaderResources = context.ShaderResources.PbrMetallicRoughness;
        var linearColourSpace = context.ShaderResources.LinearColourSpace;

        return shaderResources.GetCachedMaterial(
            payload,
            context.StandardGraphicsResources!,
            ResolveRole(payload.BaseColorTexture, textureMaterializer, linearColourSpace, isColour: true),
            ResolveRole(payload.MetallicRoughnessTexture, textureMaterializer, linearColourSpace, isColour: false),
            ResolveRole(payload.NormalTexture, textureMaterializer, linearColourSpace, isColour: false),
            ResolveRole(payload.OcclusionTexture, textureMaterializer, linearColourSpace, isColour: false),
            ResolveRole(payload.EmissiveTexture, textureMaterializer, linearColourSpace, isColour: true));
    }

    private static PbrMetallicRoughnessShaderResources.RoleBinding? ResolveRole(
        ImportedTextureBinding binding,
        ImportedTextureMaterializer textureMaterializer,
        bool linearColourSpace,
        bool isColour)
    {
        if (binding is null)
        {
            return null;
        }

        return new PbrMetallicRoughnessShaderResources.RoleBinding(
            textureMaterializer.GetTexture(
                binding.ImageSource,
                generateMipmaps: binding.Sampler.MipFilter != ImportedFilterMode.Nearest,
                srgbColour: linearColourSpace && isColour),
            textureMaterializer.GetSampler(binding.Sampler));
    }

    private ModelMeshPart CreateModelMeshPart(
        ImportedMesh importedMesh,
        ImportedMeshPart importedPart,
        AssetLoadContext context)
    {
        var material = importedMesh.Materials[importedPart.MaterialIndex];

        // Validate the material before the first GPU allocation in this part.
        MaterialPass materialPass;
        MaterialPass materialPassBlend;
        bool blendEnabled;
        if (material.ShaderFamily == ImportedMaterial.FixedFunctionFamily)
        {
            if (material.FixedFunctionShading is null)
            {
                throw new System.IO.InvalidDataException(
                    $"Mesh '{Name}' has a fixed-function material without shading values.");
            }

            blendEnabled = material.BlendEnabled;

            var materialConstants = CreateFixedFunctionMaterialConstants(material);

            var alphaTest = material.FixedFunctionShading.AlphaTest;
            var cutoutTextures = alphaTest && context.ShaderResources.AlphaTestCoverageMode;
            var alphaTestCoverage = alphaTest && context.ShaderResources.AlphaTestCoverageActive;

            var texture0 = GetFixedFunctionTexture(material, "Texture0", context, cutoutTextures)
                ?? context.StandardGraphicsResources.NullTexture;
            var texture1 = GetFixedFunctionTexture(material, "Texture1", context, false)
                ?? context.StandardGraphicsResources.NullTexture;

            var fixedFunction = context.ShaderResources.FixedFunction;

            // A2C belongs to the opaque cutout pass only; the forced-blend transparent pass
            // keeps the legacy cutoff constants.
            var cachedMaterial = fixedFunction.GetCachedMaterial(
                MapCullMode(material.CullMode),
                material.DepthWriteEnabled,
                MapDepthComparison(material.DepthComparison),
                blendEnabled,
                MapBlendFactor(material.SourceBlendFactor),
                MapBlendFactor(material.DestinationColorBlendFactor),
                MapBlendFactor(material.DestinationAlphaBlendFactor),
                materialConstants,
                texture0,
                texture1,
                alphaTestCoverage);

            var cachedMaterialBlend = fixedFunction.GetCachedMaterial(
                MapCullMode(material.CullMode),
                material.DepthWriteEnabled,
                MapDepthComparison(material.DepthComparison),
                true,
                BlendFactor.SourceAlpha,
                BlendFactor.InverseSourceAlpha,
                BlendFactor.InverseSourceAlpha,
                materialConstants,
                texture0,
                texture1);

            materialPass = new MaterialPass(cachedMaterial, context.ShaderResources.MeshDepth.Material);
            materialPassBlend = new MaterialPass(cachedMaterialBlend, context.ShaderResources.MeshDepth.Material);
        }
        else if (material.ShaderFamily == ImportedMaterial.PbrMetallicRoughnessFamily)
        {
            if (material.PbrMetallicRoughness is null)
            {
                throw new System.IO.InvalidDataException(
                    $"Mesh '{Name}' has a PBR-family material without a PBR payload.");
            }

            blendEnabled = material.PbrMetallicRoughness.AlphaMode == ImportedAlphaMode.Blend;

            materialPass = new MaterialPass(
                CreatePbrMaterial(material.PbrMetallicRoughness, context),
                context.ShaderResources.MeshDepth.Material);
            materialPassBlend = materialPass;
        }
        else
        {
            blendEnabled = false;

            var shaderResources = (ShaderMaterialShaderResources)context.ShaderResources.GetShaderMaterialResources(
                material.ShaderFamily);

            var alphaTestCoverage = material.Properties.Any(p =>
                p.Name == "AlphaTestEnable"
                && p.Kind == ImportedMaterialPropertyKind.Bool
                && p.BoolValue)
                && context.ShaderResources.AlphaTestCoverageActive;

            var cachedMaterial = shaderResources.GetCachedMaterial(material, context, alphaTestCoverage);

            materialPass = new MaterialPass(cachedMaterial, context.ShaderResources.MeshDepth.Material);
            materialPassBlend = materialPass; // TODO
        }

        var texCoords = new MeshShaderResources.MeshVertex.TexCoords[importedMesh.Vertices.Count];
        for (var i = 0; i < texCoords.Length; i++)
        {
            if (importedPart.TextureCoordinates0 != null)
            {
                texCoords[i].UV0 = importedPart.TextureCoordinates0[i];
            }

            if (importedPart.TextureCoordinates1 != null)
            {
                texCoords[i].UV1 = importedPart.TextureCoordinates1[i];
            }
        }

        var texCoordsVertexBuffer = AddDisposable(context.GraphicsDevice.CreateStaticBuffer(
            texCoords,
            BufferUsage.VertexBuffer));

        return new ModelMeshPart(
            this,
            texCoordsVertexBuffer,
            importedPart.StartIndex,
            importedPart.IndexCount,
            blendEnabled,
            materialPass,
            materialPassBlend,
            importedPart.MaterialIndex);
    }

    private static FixedFunctionShaderResources.MaterialConstantsType CreateFixedFunctionMaterialConstants(ImportedMaterial material)
    {
        var shading = material.FixedFunctionShading;

        return new FixedFunctionShaderResources.MaterialConstantsType
        {
            NumTextureStages = shading.TextureStageCount,
            Material = new FixedFunctionShaderResources.VertexMaterial
            {
                Ambient = shading.Ambient,
                Diffuse = shading.Diffuse,
                Specular = shading.Specular,
                Emissive = shading.Emissive,
                Shininess = shading.Shininess,
                Opacity = shading.Opacity,
                TextureMappingStage0 = CreateTextureMapping(shading.TextureMappingStage0),
                TextureMappingStage1 = CreateTextureMapping(shading.TextureMappingStage1),
            },
            Shading = new FixedFunctionShaderResources.ShadingConfiguration
            {
                DiffuseLightingType = (FixedFunctionShaderResources.DiffuseLightingType)shading.DiffuseLightingType,
                SpecularEnabled = shading.SpecularEnabled,
                TexturingEnabled = shading.TexturingEnabled,
                SecondaryTextureColorBlend = (FixedFunctionShaderResources.SecondaryTextureBlend)shading.SecondaryTextureColorBlend,
                SecondaryTextureAlphaBlend = (FixedFunctionShaderResources.SecondaryTextureBlend)shading.SecondaryTextureAlphaBlend,
                AlphaTest = shading.AlphaTest,
            },
        };
    }

    private static FixedFunctionShaderResources.TextureMapping CreateTextureMapping(ImportedTextureMapping mapping)
    {
        return new FixedFunctionShaderResources.TextureMapping
        {
            MappingType = (FixedFunctionShaderResources.TextureMappingType)mapping.MappingType,
            UVPerSec = mapping.UVPerSec,
            UVScale = mapping.UVScale,
            UVCenter = mapping.UVCenter,
            UVAmplitude = mapping.UVAmplitude,
            UVFrequency = mapping.UVFrequency,
            UVPhase = mapping.UVPhase,
            UVStep = mapping.UVStep,
            Speed = mapping.Speed,
            Fps = mapping.Fps,
            Log2Width = mapping.Log2Width,
            StepsPerSecond = mapping.StepsPerSecond,
        };
    }

    private static Texture GetFixedFunctionTexture(
        ImportedMaterial material,
        string propertyName,
        AssetLoadContext context,
        bool cutoutVariant)
    {
        foreach (var property in material.Properties)
        {
            if (property.Name == propertyName)
            {
                if (property.Kind != ImportedMaterialPropertyKind.TextureName)
                {
                    throw new System.IO.InvalidDataException(
                        $"Material property '{propertyName}' must hold a texture name.");
                }

                return context.AssetStore.Textures.GetByName(
                    property.TextureName,
                    cutoutVariant ? OnDemandTextureLoader.CutoutRole : string.Empty);
            }
        }

        return null;
    }

    private static FaceCullMode MapCullMode(ImportedCullMode cullMode)
    {
        return cullMode switch
        {
            ImportedCullMode.None => FaceCullMode.None,
            ImportedCullMode.Back => FaceCullMode.Back,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode)),
        };
    }

    private static ComparisonKind MapDepthComparison(ImportedDepthComparison depthComparison)
    {
        return depthComparison switch
        {
            ImportedDepthComparison.Never => ComparisonKind.Never,
            ImportedDepthComparison.Less => ComparisonKind.Less,
            ImportedDepthComparison.Equal => ComparisonKind.Equal,
            ImportedDepthComparison.LessEqual => ComparisonKind.LessEqual,
            ImportedDepthComparison.Greater => ComparisonKind.Greater,
            ImportedDepthComparison.NotEqual => ComparisonKind.NotEqual,
            ImportedDepthComparison.GreaterEqual => ComparisonKind.GreaterEqual,
            ImportedDepthComparison.Always => ComparisonKind.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(depthComparison)),
        };
    }

    private static BlendFactor MapBlendFactor(ImportedBlendFactor blendFactor)
    {
        return blendFactor switch
        {
            ImportedBlendFactor.Zero => BlendFactor.Zero,
            ImportedBlendFactor.One => BlendFactor.One,
            ImportedBlendFactor.SourceAlpha => BlendFactor.SourceAlpha,
            ImportedBlendFactor.InverseSourceAlpha => BlendFactor.InverseSourceAlpha,
            ImportedBlendFactor.SourceColor => BlendFactor.SourceColor,
            ImportedBlendFactor.InverseSourceColor => BlendFactor.InverseSourceColor,
            _ => throw new ArgumentOutOfRangeException(nameof(blendFactor)),
        };
    }
}
