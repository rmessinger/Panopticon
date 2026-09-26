#nullable enable

using System;
using System.Numerics;
using Panopticon.FileFormats.M3;

namespace Panopticon.Content.Importing.M3;

internal sealed class M3MaterialConverter
{
    private readonly string _modelPath;
    private readonly AssetImportContext _context;
    private readonly M3TextureConverter _textureConverter;
    private readonly M3ImportDiagnostics _diagnostics;

    public M3MaterialConverter(
        string modelPath,
        AssetImportContext context,
        M3TextureConverter textureConverter,
        M3ImportDiagnostics diagnostics)
    {
        _modelPath = modelPath;
        _context = context;
        _textureConverter = textureConverter;
        _diagnostics = diagnostics;
    }

    public ImportedMaterial Convert(M3MaterialReference materialReference, M3Model model)
    {
        if (materialReference.Type != 1)
        {
            throw new NotSupportedException(
                $"M3 material reference type {materialReference.Type} is not supported; only MATM type 1 standard materials are supported.");
        }

        if (materialReference.Index >= model.Materials.Count)
        {
            throw new InvalidOperationException(
                $"M3 material reference indexes material {materialReference.Index}, but the model has {model.Materials.Count} standard materials.");
        }

        var material = model.Materials[(int)materialReference.Index];
        var baseColor = ConvertLayer(material, M3MaterialLayerSlot.Diffuse, ImportedTextureRole.BaseColor, M3TextureConversionRule.BaseColor);
        var normal = ConvertLayer(material, M3MaterialLayerSlot.Normal, ImportedTextureRole.Normal, M3TextureConversionRule.Normal);
        var metallicRoughness = ConvertLayer(
            material,
            M3MaterialLayerSlot.Specular,
            ImportedTextureRole.MetallicRoughness,
            M3TextureConversionRule.Roughness);
        var emissive = ConvertLayer(material, M3MaterialLayerSlot.Emissive1, ImportedTextureRole.Emissive, M3TextureConversionRule.Emissive);

        AddIgnoredUnsupportedLayers(material);

        var hasEmissive = emissive is not null;
        var payload = new ImportedPbrMetallicRoughness(
            baseColorFactor: Vector4.One,
            baseColorTexture: baseColor,
            metallicFactor: 0.0f,
            roughnessFactor: 1.0f,
            metallicRoughnessTexture: metallicRoughness,
            normalTexture: normal,
            normalScale: 1.0f,
            emissiveFactor: hasEmissive ? Vector3.One : Vector3.Zero,
            emissiveTexture: emissive,
            alphaMode: material.AlphaTestThreshold > 0 ? ImportedAlphaMode.Mask : ImportedAlphaMode.Opaque,
            alphaCutoff: material.AlphaTestThreshold / 255.0f);

        return ImportedMaterial.CreatePbrMetallicRoughness(payload);
    }

    public bool IsOpaqueStandard(M3MaterialReference materialReference, M3Model model, int batchIndex)
    {
        if (materialReference.Type != 1)
        {
            throw new NotSupportedException(
                $"M3 material reference type {materialReference.Type} is not supported; only MATM type 1 standard materials are supported.");
        }

        if (materialReference.Index >= model.Materials.Count)
        {
            throw new InvalidOperationException(
                $"M3 material reference indexes material {materialReference.Index}, but the model has {model.Materials.Count} standard materials.");
        }

        var material = model.Materials[(int)materialReference.Index];
        if (material.BlendMode != 0)
        {
            _diagnostics.AddSkippedBatch(batchIndex, (int)materialReference.Index, material.BlendMode, "non-opaque blend mode");
            return false;
        }

        return true;
    }

    private ImportedTextureBinding? ConvertLayer(
        M3Material material,
        M3MaterialLayerSlot slot,
        ImportedTextureRole role,
        M3TextureConversionRule rule)
    {
        var layer = material.GetLayer(slot);
        if (layer is null)
        {
            return null;
        }

        if (layer.UvSource > 1)
        {
            _diagnostics.AddIgnoredLayer(material.Name, slot, layer.Path, $"unsupported UV source {layer.UvSource}");
            return null;
        }

        var texture = M3TexturePathResolver.Resolve(_modelPath, layer.Path, _context.FileSystem);
        var source = _textureConverter.Convert(
            texture.Path,
            texture.Bytes,
            rule,
            material.Specularity,
            layer.ColorChannelSelector);
        return new ImportedTextureBinding(source, (int)layer.UvSource, new ImportedSampler(), role);
    }

    private void AddIgnoredUnsupportedLayers(M3Material material)
    {
        for (var i = 0; i < material.Layers.Count; i++)
        {
            var slot = (M3MaterialLayerSlot)i;
            if (slot is M3MaterialLayerSlot.Diffuse
                or M3MaterialLayerSlot.Specular
                or M3MaterialLayerSlot.Emissive1
                or M3MaterialLayerSlot.Normal)
            {
                continue;
            }

            if (material.Layers[i] is { } layer)
            {
                _diagnostics.AddIgnoredLayer(material.Name, slot, layer.Path, "unsupported layer slot");
            }
        }
    }
}
