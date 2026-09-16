#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.FileFormats.W3d;

namespace OpenSage.Content.Importing.W3d;

internal sealed record W3dMaterialRun(
    uint StartIndex,
    uint IndexCount,
    uint VertexMaterialID,
    uint ShaderID,
    uint? TextureIndex0,
    uint? TextureIndex1);

internal sealed record W3dMaterialConversion(
    IReadOnlyList<ImportedMaterial> Materials,
    IReadOnlyList<ImportedMeshPart> MeshParts);

/// <summary>
/// Converts W3D material passes into CPU-only interchange values: shader-material versus
/// fixed-function selection, vertex materials, shading configuration, per-face run grouping,
/// render state, shader-set name normalization, and texture-name extraction. No GPU, asset
/// store, texture, or shader-store access; textures leave as names only.
/// </summary>
internal static class W3dMaterialConverter
{
    public static W3dMaterialConversion Convert(W3dMesh w3dMesh)
    {
        return w3dMesh.MaterialPasses.Count == 1 && w3dMesh.MaterialPasses[0].ShaderMaterialIds != null
            ? ConvertShaderMaterial(w3dMesh, w3dMesh.MaterialPasses[0])
            : ConvertFixedFunction(w3dMesh);
    }

    private static W3dMaterialConversion ConvertShaderMaterial(W3dMesh w3dMesh, W3dMaterialPass w3dMaterialPass)
    {
        if (w3dMaterialPass.ShaderMaterialIds!.Items.Length > 1)
        {
            throw new NotSupportedException(
                $"Mesh '{w3dMesh.Header.MeshName}' has more than one shader material.");
        }

        var shaderMaterialID = w3dMaterialPass.ShaderMaterialIds.Items[0];
        var w3dShaderMaterial = w3dMesh.ShaderMaterials.Items[(int)shaderMaterialID];

        var properties = w3dShaderMaterial.Properties
            .Select(p => ConvertShaderMaterialProperty(w3dMesh.Header.MeshName, p))
            .ToArray();

        var material = new ImportedMaterial(
            w3dShaderMaterial.Header.TypeName.Replace(".fx", string.Empty),
            ImportedCullMode.Back,
            depthWriteEnabled: true,
            depthComparison: ImportedDepthComparison.LessEqual,
            blendEnabled: false,
            sourceBlendFactor: ImportedBlendFactor.One,
            destinationColorBlendFactor: ImportedBlendFactor.Zero,
            destinationAlphaBlendFactor: ImportedBlendFactor.Zero,
            fixedFunctionShading: null,
            properties);

        var textureCoordinates = CopyTexCoords(w3dMaterialPass.TexCoords, w3dMesh.Header.NumVertices);

        var meshPart = new ImportedMeshPart(
            textureCoordinates,
            null,
            startIndex: 0,
            indexCount: (uint)w3dMesh.Triangles.Items.Length * 3,
            blendEnabled: false,
            materialIndex: 0);

        return new W3dMaterialConversion([material], [meshPart]);
    }

    private static ImportedMaterialProperty ConvertShaderMaterialProperty(
        string meshName,
        W3dShaderMaterialProperty property)
    {
        return property.PropertyType switch
        {
            W3dShaderMaterialPropertyType.Texture => ImportedMaterialProperty.FromTextureName(property.PropertyName, property.StringValue),

            W3dShaderMaterialPropertyType.Float => ImportedMaterialProperty.FromFloat(property.PropertyName, property.Value.Float),

            W3dShaderMaterialPropertyType.Vector2 => ImportedMaterialProperty.FromVector2(property.PropertyName, property.Value.Vector2),

            W3dShaderMaterialPropertyType.Vector3 => ImportedMaterialProperty.FromVector3(property.PropertyName, property.Value.Vector3),

            W3dShaderMaterialPropertyType.Vector4 => ImportedMaterialProperty.FromVector4(property.PropertyName, property.Value.Vector4),

            W3dShaderMaterialPropertyType.Int => ImportedMaterialProperty.FromInt32(property.PropertyName, property.Value.Int),

            W3dShaderMaterialPropertyType.Bool => ImportedMaterialProperty.FromBool(property.PropertyName, property.Value.Bool),

            _ => throw new NotSupportedException(
                $"Mesh '{meshName}' uses unsupported shader material property type '{property.PropertyType}'."),
        };
    }

    private static W3dMaterialConversion ConvertFixedFunction(W3dMesh w3dMesh)
    {
        var materials = new List<ImportedMaterial>();
        var meshParts = new List<ImportedMeshPart>();

        foreach (var w3dMaterialPass in w3dMesh.MaterialPasses)
        {
            var hasTextureStage0 = w3dMaterialPass.TextureStages.Count > 0;
            var textureStage0 = hasTextureStage0
                ? w3dMaterialPass.TextureStages[0]
                : null;

            var hasTextureStage1 = w3dMaterialPass.TextureStages.Count > 1 && w3dMaterialPass.TextureStages[1].TexCoords != null;
            var textureStage1 = hasTextureStage1
                ? w3dMaterialPass.TextureStages[1]
                : null;

            var numTextureStages = hasTextureStage0 && hasTextureStage1
                ? 2
                : hasTextureStage0 ? 1 : 0;

            var textureCoordinates0 = CopyTexCoords(textureStage0?.TexCoords, w3dMesh.Header.NumVertices);
            var textureCoordinates1 = CopyTexCoords(textureStage1?.TexCoords, w3dMesh.Header.NumVertices);

            foreach (var run in GetMaterialRuns(w3dMesh, w3dMaterialPass))
            {
                var w3dShader = w3dMesh.Shaders.Items[(int)run.ShaderID];
                var w3dVertexMaterial = w3dMesh.VertexMaterials.Items[(int)run.VertexMaterialID];
                var w3dMaterial = w3dVertexMaterial.Info;

                var cullMode = w3dMesh.Header.Attributes.HasFlag(W3dMeshFlags.TwoSided)
                    ? ImportedCullMode.None
                    : ImportedCullMode.Back;

                var blendEnabled = w3dShader.SrcBlend != W3dShaderSrcBlendFunc.One || w3dShader.DestBlend != W3dShaderDestBlendFunc.Zero;

                var shading = new ImportedFixedFunctionShading(
                    MapDiffuseLightingType(w3dShader.PrimaryGradient),
                    specularEnabled: w3dShader.SecondaryGradient == W3dShaderSecondaryGradient.Enable,
                    texturingEnabled: w3dShader.Texturing == W3dShaderTexturing.Enable,
                    secondaryTextureColorBlend: MapDetailColorFunc(w3dShader.DetailColorFunc, w3dMesh.Header.MeshName),
                    secondaryTextureAlphaBlend: MapDetailAlphaFunc(w3dShader.DetailAlphaFunc),
                    alphaTest: w3dShader.AlphaTest == W3dShaderAlphaTest.Enable,
                    ambient: w3dMaterial.Ambient.ToVector3(),
                    diffuse: w3dMaterial.Diffuse.ToVector3(),
                    specular: w3dMaterial.Specular.ToVector3(),
                    emissive: w3dMaterial.Emissive.ToVector3(),
                    shininess: w3dMaterial.Shininess,
                    opacity: w3dMaterial.Opacity,
                    textureStageCount: numTextureStages,
                    textureMappingStage0: CreateTextureMapping(
                        w3dMaterial.Stage0Mapping,
                        w3dVertexMaterial.MapperArgs0 ?? new W3dVertexMapperArgs(W3dChunkType.W3D_CHUNK_VERTEX_MAPPER_ARGS0)),
                    textureMappingStage1: CreateTextureMapping(
                        w3dMaterial.Stage1Mapping,
                        w3dVertexMaterial.MapperArgs1 ?? new W3dVertexMapperArgs(W3dChunkType.W3D_CHUNK_VERTEX_MAPPER_ARGS1)));

                var properties = new List<ImportedMaterialProperty>();
                if (run.TextureIndex0 != null)
                {
                    properties.Add(ImportedMaterialProperty.FromTextureName(
                        "Texture0",
                        GetTextureName(w3dMesh, run.TextureIndex0)));
                }
                if (run.TextureIndex1 != null)
                {
                    properties.Add(ImportedMaterialProperty.FromTextureName(
                        "Texture1",
                        GetTextureName(w3dMesh, run.TextureIndex1)));
                }

                materials.Add(new ImportedMaterial(
                    ImportedMaterial.FixedFunctionFamily,
                    cullMode,
                    depthWriteEnabled: w3dShader.DepthMask == W3dShaderDepthMask.WriteEnable,
                    depthComparison: MapDepthComparison(w3dShader.DepthCompare),
                    blendEnabled,
                    MapSourceBlendFactor(w3dShader.SrcBlend),
                    MapDestinationBlendFactor(w3dShader.DestBlend, alpha: false),
                    MapDestinationBlendFactor(w3dShader.DestBlend, alpha: true),
                    shading,
                    properties));

                meshParts.Add(new ImportedMeshPart(
                    textureCoordinates0,
                    textureCoordinates1,
                    run.StartIndex,
                    run.IndexCount,
                    blendEnabled,
                    materialIndex: materials.Count - 1));
            }
        }

        return new W3dMaterialConversion(materials, meshParts);
    }

    /// <summary>
    /// Per-face material run grouping: consecutive faces sharing vertex material, shader, and
    /// texture ids become one run, which becomes one imported mesh part.
    /// </summary>
    internal static IReadOnlyList<W3dMaterialRun> GetMaterialRuns(W3dMesh w3dMesh, W3dMaterialPass w3dMaterialPass)
    {
        var textureStage0 = w3dMaterialPass.TextureStages.Count > 0
            ? w3dMaterialPass.TextureStages[0]
            : null;
        var textureStage1 = w3dMaterialPass.TextureStages.Count > 1 && w3dMaterialPass.TextureStages[1].TexCoords != null
            ? w3dMaterialPass.TextureStages[1]
            : null;

        var runs = new List<W3dMaterialRun>();

        var vertexMaterialIds = w3dMaterialPass.VertexMaterialIds?.Items;
        var shaderIds = w3dMaterialPass.ShaderIds?.Items;

        if (vertexMaterialIds is { Length: 1 }
            && shaderIds is { Length: 1 }
            && w3dMaterialPass.TextureStages.Count == 1
            && w3dMaterialPass.TextureStages[0].TextureIds.Items.Count == 1)
        {
            runs.Add(new W3dMaterialRun(
                0,
                w3dMesh.Header.NumTris * 3,
                vertexMaterialIds[0],
                shaderIds[0],
                w3dMaterialPass.TextureStages[0].TextureIds.Items[0],
                null));
            return runs;
        }

        var combinedIds = GetExpandedVertexMaterialIds(w3dMesh, vertexMaterialIds)
            .Zip(GetExpandedShaderIds(w3dMesh, shaderIds), (x, y) => new { VertexMaterialID = x, ShaderID = y })
            .Zip(GetExpandedTextureIds(textureStage0?.TextureIds.Items, w3dMesh.Header.NumTris), (x, y) => new { x.VertexMaterialID, x.ShaderID, TextureIndex0 = y })
            .Zip(GetExpandedTextureIds(textureStage1?.TextureIds.Items, w3dMesh.Header.NumTris), (x, y) => new { x.VertexMaterialID, x.ShaderID, x.TextureIndex0, TextureIndex1 = y })
            .Select(x => new W3dMaterialRun(0, 0, x.VertexMaterialID, x.ShaderID, x.TextureIndex0, x.TextureIndex1))
            .ToArray();

        var current = combinedIds[0];
        var currentStartIndex = 0u;
        var currentIndexCount = 0u;

        foreach (var newCombinedId in combinedIds)
        {
            if (!current.HasSameMaterialAs(newCombinedId))
            {
                runs.Add(current with { StartIndex = currentStartIndex, IndexCount = currentIndexCount });

                currentStartIndex += currentIndexCount;
                currentIndexCount = 0;
            }

            current = newCombinedId;
            currentIndexCount += 3;
        }

        if (currentIndexCount > 0)
        {
            runs.Add(current with { StartIndex = currentStartIndex, IndexCount = currentIndexCount });
        }

        return runs;
    }

    private static bool HasSameMaterialAs(this W3dMaterialRun run, W3dMaterialRun other)
    {
        return run.VertexMaterialID == other.VertexMaterialID
            && run.ShaderID == other.ShaderID
            && run.TextureIndex0 == other.TextureIndex0
            && run.TextureIndex1 == other.TextureIndex1;
    }

    private static IEnumerable<uint> GetExpandedVertexMaterialIds(W3dMesh w3dMesh, uint[]? ids)
    {
        if (ids == null)
        {
            for (var i = 0; i < w3dMesh.Header.NumTris; i++)
            {
                yield return 0;
            }
        }
        else if (ids.Length == 1)
        {
            var result = ids[0];
            for (var i = 0; i < w3dMesh.Header.NumTris; i++)
            {
                yield return result;
            }
        }
        else
        {
            for (var i = 0; i < w3dMesh.Header.NumTris; i++)
            {
                var triangle = w3dMesh.Triangles.Items[i];
                var materialID0 = ids[(int)triangle.VIndex0];
                var materialID1 = ids[(int)triangle.VIndex1];
                var materialID2 = ids[(int)triangle.VIndex2];
                if (materialID0 != materialID1 || materialID1 != materialID2)
                {
                    throw new NotSupportedException(
                        $"Mesh '{w3dMesh.Header.MeshName}' has a triangle whose vertices reference " +
                        "more than one vertex material.");
                }
                yield return materialID0;
            }

            foreach (var id in ids)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<uint> GetExpandedShaderIds(W3dMesh w3dMesh, uint[]? ids)
    {
        if (ids == null)
        {
            for (var i = 0; i < w3dMesh.Header.NumTris; i++)
            {
                yield return 0;
            }
        }
        else if (ids.Length == 1)
        {
            var result = ids[0];
            for (var i = 0; i < w3dMesh.Header.NumTris; i++)
            {
                yield return result;
            }
        }
        else
        {
            foreach (var id in ids)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<uint?> GetExpandedTextureIds(IReadOnlyList<uint?>? ids, uint numTriangles)
    {
        if (ids == null)
        {
            for (var i = 0; i < numTriangles; i++)
            {
                yield return null;
            }
        }
        else if (ids.Count == 1)
        {
            var result = ids[0];
            for (var i = 0; i < numTriangles; i++)
            {
                yield return result;
            }
        }
        else
        {
            foreach (var id in ids)
            {
                yield return id;
            }
        }
    }

    private static Vector2[]? CopyTexCoords(W3dVector2List? texCoords, uint numVertices)
    {
        if (texCoords == null)
        {
            return null;
        }

        var result = new Vector2[numVertices];
        for (var i = 0; i < result.Length && i < texCoords.Items.Length; i++)
        {
            result[i] = texCoords.Items[i];
        }

        return result;
    }

    private static string GetTextureName(W3dMesh w3dMesh, uint? textureIndex)
    {
        var w3dTexture = w3dMesh.Textures.Items[(int)textureIndex!.Value];

        if (w3dTexture.TextureInfo != null && w3dTexture.TextureInfo.FrameCount != 1)
        {
            throw new NotImplementedException(
                $"Mesh '{w3dMesh.Header.MeshName}' uses multi-frame texture " +
                $"'{w3dTexture.Name.Value}', which is not supported.");
        }

        return w3dTexture.Name.Value;
    }

    private static ImportedDiffuseLightingType MapDiffuseLightingType(W3dShaderPrimaryGradient value)
    {
        return value switch
        {
            W3dShaderPrimaryGradient.Disable => ImportedDiffuseLightingType.Disable,
            W3dShaderPrimaryGradient.Modulate or W3dShaderPrimaryGradient.Enable => ImportedDiffuseLightingType.Modulate,
            W3dShaderPrimaryGradient.Add => ImportedDiffuseLightingType.Add,
            W3dShaderPrimaryGradient.BumpEnvMap => ImportedDiffuseLightingType.BumpEnvMap,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static ImportedSecondaryTextureBlend MapDetailColorFunc(W3dShaderDetailColorFunc value, string meshName)
    {
        return value switch
        {
            W3dShaderDetailColorFunc.Disable => ImportedSecondaryTextureBlend.Disable,
            W3dShaderDetailColorFunc.Detail => ImportedSecondaryTextureBlend.Detail,
            W3dShaderDetailColorFunc.Scale or W3dShaderDetailColorFunc.ScaleAlt => ImportedSecondaryTextureBlend.Scale,
            W3dShaderDetailColorFunc.InvScale or W3dShaderDetailColorFunc.InvScaleAlt => ImportedSecondaryTextureBlend.InvScale,
            W3dShaderDetailColorFunc.Add => ImportedSecondaryTextureBlend.Add,
            W3dShaderDetailColorFunc.Sub or W3dShaderDetailColorFunc.SubR or W3dShaderDetailColorFunc.Blend => throw new NotImplementedException(
                $"Mesh '{meshName}' uses unsupported detail color function '{value}'."),
            W3dShaderDetailColorFunc.DetailBlend => ImportedSecondaryTextureBlend.DetailBlend,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static ImportedSecondaryTextureBlend MapDetailAlphaFunc(W3dShaderDetailAlphaFunc value)
    {
        return value switch
        {
            W3dShaderDetailAlphaFunc.Disable => ImportedSecondaryTextureBlend.Disable,
            W3dShaderDetailAlphaFunc.Detail => ImportedSecondaryTextureBlend.Detail,
            W3dShaderDetailAlphaFunc.Scale => ImportedSecondaryTextureBlend.Scale,
            W3dShaderDetailAlphaFunc.InvScale => ImportedSecondaryTextureBlend.InvScale,
            _ => ImportedSecondaryTextureBlend.Disable,
        };
    }

    private static ImportedDepthComparison MapDepthComparison(W3dShaderDepthCompare value)
    {
        return value switch
        {
            W3dShaderDepthCompare.PassNever => ImportedDepthComparison.Never,
            W3dShaderDepthCompare.PassLess => ImportedDepthComparison.Less,
            W3dShaderDepthCompare.PassEqual => ImportedDepthComparison.Equal,
            W3dShaderDepthCompare.PassLEqual => ImportedDepthComparison.LessEqual,
            W3dShaderDepthCompare.PassGreater => ImportedDepthComparison.Greater,
            W3dShaderDepthCompare.PassNotEqual => ImportedDepthComparison.NotEqual,
            W3dShaderDepthCompare.PassGEqual => ImportedDepthComparison.GreaterEqual,
            W3dShaderDepthCompare.PassAlways => ImportedDepthComparison.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static ImportedBlendFactor MapSourceBlendFactor(W3dShaderSrcBlendFunc value)
    {
        return value switch
        {
            W3dShaderSrcBlendFunc.Zero => ImportedBlendFactor.Zero,
            W3dShaderSrcBlendFunc.One => ImportedBlendFactor.One,
            W3dShaderSrcBlendFunc.SrcAlpha => ImportedBlendFactor.SourceAlpha,
            W3dShaderSrcBlendFunc.OneMinusSrcAlpha => ImportedBlendFactor.InverseSourceAlpha,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static ImportedBlendFactor MapDestinationBlendFactor(W3dShaderDestBlendFunc value, bool alpha)
    {
        return value switch
        {
            W3dShaderDestBlendFunc.Zero => ImportedBlendFactor.Zero,
            W3dShaderDestBlendFunc.One => ImportedBlendFactor.One,
            W3dShaderDestBlendFunc.SrcAlpha => ImportedBlendFactor.SourceAlpha,
            W3dShaderDestBlendFunc.OneMinusSrcAlpha => ImportedBlendFactor.InverseSourceAlpha,
            W3dShaderDestBlendFunc.SrcColor => alpha
                ? ImportedBlendFactor.SourceAlpha
                : ImportedBlendFactor.SourceColor,
            W3dShaderDestBlendFunc.OneMinusSrcColor => alpha
                ? ImportedBlendFactor.InverseSourceAlpha
                : ImportedBlendFactor.InverseSourceColor,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static ImportedTextureMapping CreateTextureMapping(W3dVertexMappingType mappingType, W3dVertexMapperArgs args)
    {
        return new ImportedTextureMapping(
            MapTextureMappingType(mappingType),
            new Vector2(args.UPerSec, args.VPerSec),
            new Vector2(args.UScale, args.VScale),
            new Vector2(args.UCenter, args.VCenter),
            new Vector2(args.UAmp, args.VAmp),
            new Vector2(args.UFreq, args.VFreq),
            new Vector2(args.UPhase, args.VPhase),
            new Vector2(args.UStep, args.VStep),
            args.Speed,
            args.FPS,
            args.Log2Width,
            args.StepsPerSecond);
    }

    private static ImportedTextureMappingType MapTextureMappingType(W3dVertexMappingType value)
    {
        return value switch
        {
            W3dVertexMappingType.Uv => ImportedTextureMappingType.Uv,
            W3dVertexMappingType.Environment or W3dVertexMappingType.CheapEnvironment => ImportedTextureMappingType.Environment,
            W3dVertexMappingType.LinearOffset => ImportedTextureMappingType.LinearOffset,
            W3dVertexMappingType.Rotate => ImportedTextureMappingType.Rotate,
            W3dVertexMappingType.SineLinearOffset => ImportedTextureMappingType.SineLinearOffset,
            W3dVertexMappingType.StepLinearOffset => ImportedTextureMappingType.StepLinearOffset,
            W3dVertexMappingType.Screen => ImportedTextureMappingType.Screen,
            W3dVertexMappingType.Scale => ImportedTextureMappingType.Scale,
            W3dVertexMappingType.Grid => ImportedTextureMappingType.Grid,
            W3dVertexMappingType.Random => ImportedTextureMappingType.Random,
            W3dVertexMappingType.BumpEnv => ImportedTextureMappingType.BumpEnv,
            W3dVertexMappingType.WsEnvironment => ImportedTextureMappingType.WsEnvironment,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }
}
