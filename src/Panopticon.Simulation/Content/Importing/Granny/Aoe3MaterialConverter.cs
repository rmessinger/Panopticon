#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Panopticon.Content.Importing;
using Panopticon.FileFormats.Bar;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Maps an AoE3 material name onto a C3 metallic-roughness material by resolving its
/// .material.XMB beside the model. Texture roles follow the materialdef: BaseColor (sRGB),
/// Normals, and Masks bound as glTF occlusion/roughness/metallic (R/G/B, the packing the
/// Masks textures use natively, so no channel swizzle is needed). Details and team color are
/// recorded as properties only. Missing material files, submaterials, or textures keep the
/// placeholder material and record a diagnostic.
/// </summary>
internal sealed class Aoe3MaterialConverter
{
    private const string TextureRoot = "Art/";

    private readonly AssetImportContext _context;
    private readonly GrannyImportDiagnostics _diagnostics;

    public Aoe3MaterialConverter(AssetImportContext context, GrannyImportDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _context = context;
        _diagnostics = diagnostics;
    }

    public ImportedMaterial Convert(
        string? materialName,
        string materialFileKey,
        string meshName,
        int boneBindingCount)
    {
        if (string.IsNullOrEmpty(materialName))
        {
            return CreatePlaceholderMaterial(materialName, boneBindingCount);
        }

        var submaterial = TryFindSubmaterial(materialFileKey, materialName, meshName);
        if (submaterial is null)
        {
            return CreatePlaceholderMaterial(materialName, boneBindingCount);
        }

        return BuildMaterial(submaterial, materialName, boneBindingCount, meshName);
    }

    private XElement? TryFindSubmaterial(string materialFileKey, string materialName, string meshName)
    {
        var entry = _context.FileSystem.GetFile(materialFileKey);
        if (entry is null)
        {
            _diagnostics.Warn(
                $"Mesh '{meshName}': material file '{materialFileKey}' not found; using placeholder material.");
            return null;
        }

        try
        {
            using var stream = entry.Open();
            var document = XmbDocument.Read(stream);
            foreach (var candidate in document.Root!.Elements("submaterial"))
            {
                var name = candidate.Attribute("name")?.Value;
                if (string.Equals(name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            _diagnostics.Warn(
                $"Mesh '{meshName}': material file '{materialFileKey}' has no submaterial '{materialName}'; using placeholder material.");
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            _diagnostics.Warn(
                $"Mesh '{meshName}': material file '{materialFileKey}' failed to decode: {ex.Message}; using placeholder material.");
            return null;
        }
    }

    private ImportedMaterial BuildMaterial(
        XElement submaterial,
        string materialName,
        int boneBindingCount,
        string meshName)
    {
        var textures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var floats = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in submaterial.Element("parameters")?.Elements() ?? [])
        {
            var name = parameter.Attribute("name")?.Value;
            var overrideValue = parameter.Attribute("override")?.Value;
            if (name is null || overrideValue is null)
            {
                continue;
            }

            if (parameter.Name.LocalName == "texture")
            {
                textures[name] = overrideValue;
            }
            else if (parameter.Name.LocalName == "float"
                && float.TryParse(overrideValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                floats[name] = value;
            }
        }

        var properties = new List<ImportedMaterialProperty>
        {
            ImportedMaterialProperty.FromTextureName("GrannyMaterialName", materialName),
        };
        if (boneBindingCount > 1)
        {
            properties.Add(ImportedMaterialProperty.FromBool("RigidFirstBoneBinding", true));
        }

        var materialdefName = submaterial.Element("materialdef")?.Attribute("name")?.Value;
        if (!string.IsNullOrEmpty(materialdefName))
        {
            properties.Add(ImportedMaterialProperty.FromTextureName("Aoe3Materialdef", materialdefName!));
        }

        ImportedTextureBinding? Bind(string role, string description, ImportedTextureRole bindingRole)
        {
            if (!textures.TryGetValue(role, out var overridePath))
            {
                _diagnostics.Warn($"Mesh '{meshName}': submaterial '{materialName}' has no {description} texture.");
                return null;
            }

            var virtualPath = TextureRoot + overridePath.Replace('\\', '/') + ".ddt";
            if (_context.FileSystem.GetFile(virtualPath) is null)
            {
                _diagnostics.Warn(
                    $"Mesh '{meshName}': {description} texture '{virtualPath}' not found; binding omitted.");
                return null;
            }

            return new ImportedTextureBinding(
                ImportedImageSource.CreateVirtualPath(virtualPath),
                uvSet: 0,
                new ImportedSampler(),
                bindingRole);
        }

        var baseColor = Bind("BaseColor", "base color", ImportedTextureRole.BaseColor);
        var normals = Bind("Normals", "normal", ImportedTextureRole.Normal);
        var masks = Bind("Masks", "masks", ImportedTextureRole.MetallicRoughness);

        // Masks pack ORM: R = ambient occlusion, G = roughness, B = metallic (AoE3 DE
        // modding reference; the measured R histograms are graded and AO-like). The same
        // texture is re-bound under the occlusion role so the renderer reads its R channel.
        var masksOcclusion = masks is null
            ? null
            : new ImportedTextureBinding(masks.ImageSource, masks.UvSet, masks.Sampler, ImportedTextureRole.Occlusion);
        if (textures.TryGetValue("Details", out var details))
        {
            properties.Add(ImportedMaterialProperty.FromTextureName("Aoe3DetailsTexture", details));
        }

        floats.TryGetValue("AO Intensity", out var aoIntensity);
        floats.TryGetValue("Roughness Max", out var roughnessMax);
        floats.TryGetValue("Metallic Max", out var metallicMax);
        foreach (var (name, value) in floats)
        {
            properties.Add(ImportedMaterialProperty.FromFloat("Aoe3Slider_" + name, value));
        }

        // Materials that omit AO Intensity (musketeer, redcoats) use the corpus's only
        // explicit value (0.2, light_cavalry_age4_horse mata); the engine default is
        // unverified, and full strength crushes coats whose R channel is mostly dark.
        const float DefaultAoIntensity = 0.2f;

        var payload = new ImportedPbrMetallicRoughness(
            baseColorTexture: baseColor,
            metallicFactor: 1f,
            roughnessFactor: 1f,
            metallicRoughnessTexture: masks,
            normalTexture: normals,
            occlusionTexture: masksOcclusion,
            occlusionStrength: aoIntensity > 0f ? aoIntensity : DefaultAoIntensity);

        return ImportedMaterial.CreatePbrMetallicRoughness(payload, properties);
    }

    private static ImportedMaterial CreatePlaceholderMaterial(string? materialName, int boneBindingCount)
    {
        var properties = new List<ImportedMaterialProperty>();
        if (!string.IsNullOrEmpty(materialName))
        {
            properties.Add(ImportedMaterialProperty.FromTextureName("GrannyMaterialName", materialName!));
        }

        if (boneBindingCount > 1)
        {
            properties.Add(ImportedMaterialProperty.FromBool("RigidFirstBoneBinding", true));
        }

        return ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness(), properties);
    }
}
