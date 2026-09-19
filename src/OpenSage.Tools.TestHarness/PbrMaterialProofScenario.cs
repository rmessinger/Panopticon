using System;
using System.IO;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Content.Loaders;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Mathematics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Deterministic TestHarness captures of the complete C3 PBR feature matrix through
/// StandaloneModelCapture. Every model, texture, and skeleton is synthetic; the Generals
/// installation only supplies the rendering host. Writes one PNG per feature case, a
/// validation_guide.md naming the expected check per capture, an emissive color-space
/// round-trip assertion (known sRGB texel within one 8-bit step), and a determinism check.
/// </summary>
internal static class PbrMaterialProofScenario
{
    private const int CaptureSize = 256;

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var outputDirectory = "pbr-material-proof";
        Directory.CreateDirectory(outputDirectory);
        foreach (var existing in Directory.EnumerateFiles(outputDirectory, "*.png"))
        {
            File.Delete(existing);
        }

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        var failures = 0;
        var determinismPath = "";

        Graphics.Model CaptureModel(ImportedMaterial material, bool skinned = false, bool withTangents = false)
        {
            var mesh = CreateQuadMesh(material, skinned, withTangents);
            return ImportedModelMaterializer.MaterializeModel(CreateImportedModel(mesh), game.AssetStore.LoadContext);
        }

        string Capture(string name, ImportedMaterial material, bool skinned = false, bool withTangents = false)
        {
            var path = Path.Combine(outputDirectory, $"pbr-{name}.png");
            StandaloneModelCapture.Capture(
                game,
                CaptureModel(material, skinned, withTangents),
                game.AssetStore.LoadContext,
                path,
                new Mathematics.Size(CaptureSize, CaptureSize));
            reporter.Info($"Captured {name} -> {path}");

            if (name == "dielectric")
            {
                // Determinism: a repeated capture must be byte-identical.
                determinismPath = Path.Combine(outputDirectory, "pbr-dielectric-repeat.png");
                StandaloneModelCapture.Capture(
                    game,
                    CaptureModel(material, skinned, withTangents),
                    game.AssetStore.LoadContext,
                    determinismPath,
                    new Mathematics.Size(CaptureSize, CaptureSize));
            }

            return path;
        }

        var flatTexture = EncodePng(200, 40, 40);
        var patternTexture = EncodeCheckerPng();
        var normalTexture = EncodeNormalPng();

        // Warm-up frame: the first pipeline execution after device creation touches lazily
        // loaded engine globals (cloud textures, mip generation), which makes that one frame
        // unstable. Render and discard it so every recorded capture is deterministic.
        StandaloneModelCapture.Capture(
            game,
            ImportedModelMaterializer.MaterializeModel(
                CreateImportedModel(CreateQuadMesh(
                    ImportedMaterial.CreatePbrMetallicRoughness(new ImportedPbrMetallicRoughness()),
                    skinned: false,
                    withTangents: false)),
                game.AssetStore.LoadContext),
            game.AssetStore.LoadContext,
            Path.Combine(Path.GetTempPath(), "pbr-proof-warmup.png"),
            new Mathematics.Size(CaptureSize, CaptureSize));
        File.Delete(Path.Combine(Path.GetTempPath(), "pbr-proof-warmup.png"));

        try
        {
            // Base color factor only (dielectric, matte).
            Capture("dielectric", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(baseColorFactor: new Vector4(0.55f, 0.16f, 0.16f, 1))));

            // Metal, smooth: sharp specular response.
            Capture("metal-smooth", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorFactor: new Vector4(0.85f, 0.85f, 0.88f, 1),
                    metallicFactor: 1.0f,
                    roughnessFactor: 0.1f)));

            // Dielectric, fully rough: broad diffuse response.
            Capture("dielectric-matte", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorFactor: new Vector4(0.85f, 0.85f, 0.88f, 1),
                    metallicFactor: 0.0f,
                    roughnessFactor: 1.0f)));

            // Packed metallic-roughness texture (linear G/B channels).
            Capture("metallic-roughness-texture", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    metallicFactor: 1.0f,
                    metallicRoughnessTexture: Bind("mr", 0, patternTexture, ImportedTextureRole.MetallicRoughness))));

            // Normal mapping with imported tangents.
            Capture("normal-imported-tangents", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    normalTexture: Bind("normal", 0, normalTexture, ImportedTextureRole.Normal))),
                withTangents: true);

            // Normal mapping with generated tangents (mesh omits them).
            Capture("normal-generated-tangents", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    normalTexture: Bind("normal", 0, normalTexture, ImportedTextureRole.Normal))));

            // Occlusion and emissive.
            Capture("occlusion-emissive", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    occlusionTexture: Bind("occ", 0, patternTexture, ImportedTextureRole.Occlusion),
                    emissiveTexture: Bind("emis", 1, flatTexture, ImportedTextureRole.Emissive))));

            // Alpha mask.
            Capture("alpha-mask", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: Bind("base", 0, flatTexture, ImportedTextureRole.BaseColor),
                    alphaMode: ImportedAlphaMode.Mask,
                    alphaCutoff: 0.5f)));

            // Alpha blend.
            Capture("alpha-blend", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorFactor: new Vector4(0.2f, 0.6f, 0.2f, 0.5f),
                    alphaMode: ImportedAlphaMode.Blend)));

            // Double-sided.
            Capture("double-sided", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(doubleSided: true)));

            // UV1 selection for the base color.
            Capture("uv1-selection", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: Bind("base1", 1, flatTexture, ImportedTextureRole.BaseColor))));

            // Sampler addressing and filtering with a checker pattern (UVs span 0..2).
            Capture("sampler-repeat", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: new ImportedTextureBinding(
                        ImportedImageSource.CreateEmbeddedBytes(patternTexture, "image/png", "checker"),
                        0,
                        new ImportedSampler(ImportedAddressMode.Repeat, ImportedAddressMode.Repeat),
                        ImportedTextureRole.BaseColor))));

            Capture("sampler-clamp", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: new ImportedTextureBinding(
                        ImportedImageSource.CreateEmbeddedBytes(patternTexture, "image/png", "checker"),
                        0,
                        new ImportedSampler(ImportedAddressMode.Clamp, ImportedAddressMode.Clamp),
                        ImportedTextureRole.BaseColor))));

            Capture("filter-nearest", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: new ImportedTextureBinding(
                        ImportedImageSource.CreateEmbeddedBytes(patternTexture, "image/png", "checker"),
                        0,
                        new ImportedSampler(minFilter: ImportedFilterMode.Nearest, magFilter: ImportedFilterMode.Nearest),
                        ImportedTextureRole.BaseColor))));

            Capture("filter-linear", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorTexture: new ImportedTextureBinding(
                        ImportedImageSource.CreateEmbeddedBytes(patternTexture, "image/png", "checker"),
                        0,
                        new ImportedSampler(minFilter: ImportedFilterMode.Linear, magFilter: ImportedFilterMode.Linear),
                        ImportedTextureRole.BaseColor))));

            // Two-influence skinned mesh through the same PBR family.
            Capture("skinned", ImportedMaterial.CreatePbrMetallicRoughness(
                new ImportedPbrMetallicRoughness(
                    baseColorFactor: new Vector4(0.3f, 0.3f, 0.7f, 1))),
                skinned: true);

            // Color-space contract: emissive-only material with a known sRGB texel. Black albedo
            // and full metal kill diffuse, ambient, and specular; the output is the emissive
            // round trip alone.
            var knownTexel = new Vector3(0.75f, 0.25f, 0.1f);
            var colorSpacePath = Capture("colorspace-emissive",
                ImportedMaterial.CreatePbrMetallicRoughness(
                    new ImportedPbrMetallicRoughness(
                        baseColorFactor: Vector4.Zero,
                        metallicFactor: 1.0f,
                        emissiveFactor: knownTexel)));

            var expected = LinearToSrgbByte(knownTexel);
            using (var image = Image.Load<Rgba32>(colorSpacePath))
            {
                var pixel = image[CaptureSize / 2, CaptureSize / 2];
                var deltaR = Math.Abs(pixel.R - expected.R);
                var deltaG = Math.Abs(pixel.G - expected.G);
                var deltaB = Math.Abs(pixel.B - expected.B);
                if (deltaR > 1 || deltaG > 1 || deltaB > 1)
                {
                    failures++;
                    reporter.Error(
                        $"Color-space round trip failed: expected ({expected.R},{expected.G},{expected.B}), got ({pixel.R},{pixel.G},{pixel.B}).");
                }
                else
                {
                    reporter.Info($"Color-space round trip passed: ({pixel.R},{pixel.G},{pixel.B}) within one step of ({expected.R},{expected.G},{expected.B}).");
                }
            }

            // Determinism result.
            if (File.ReadAllBytes(Path.Combine(outputDirectory, "pbr-dielectric.png")).SequenceEqual(
                    File.ReadAllBytes(determinismPath)))
            {
                reporter.Info("Determinism check passed: repeated dielectric capture is byte-identical.");
            }
            else
            {
                failures++;
                reporter.Error("Determinism check failed: repeated dielectric capture differs.");
            }

            // Keep the repeat capture for diagnostics; it is overwritten on the next run.

            WriteValidationGuide(outputDirectory);
            reporter.Info($"PBR feature matrix captures written to {Path.GetFullPath(outputDirectory)}");
        }
        catch (Exception exception)
        {
            failures++;
            reporter.Error($"PBR material proof failed: {exception}");
        }

        return failures == 0 ? 0 : 1;
    }

    private static ImportedMesh CreateQuadMesh(ImportedMaterial material, bool skinned, bool withTangents)
    {
        var blendEnabled = material.PbrMetallicRoughness?.AlphaMode == ImportedAlphaMode.Blend;
        var vertices = new ImportedVertex[4];
        for (var i = 0; i < 4; i++)
        {
            var position = i switch
            {
                0 => new Vector3(-100, -100, 0),
                1 => new Vector3(100, -100, 0),
                2 => new Vector3(100, 100, 0),
                _ => new Vector3(-100, 100, 0),
            };
            var tangent = withTangents ? Vector3.UnitX : (Vector3?)null;
            var binormal = withTangents ? Vector3.UnitY : (Vector3?)null;

            vertices[i] = skinned
                ? new ImportedVertex(
                    position,
                    Vector3.UnitZ,
                    tangent: tangent,
                    binormal: binormal,
                    boneIndex0: 0,
                    boneIndex1: 1,
                    boneWeight0: 0.5f,
                    boneWeight1: 0.5f)
                : new ImportedVertex(position, Vector3.UnitZ, tangent: tangent, binormal: binormal);
        }

        // UVs span 0..2 so address modes and filtering are visible.
        var uv0 = new[]
        {
            new Vector2(0, 2),
            new Vector2(2, 2),
            new Vector2(2, 0),
            new Vector2(0, 0),
        };

        var uv1 = new[]
        {
            new Vector2(0.25f, 0.75f),
            new Vector2(0.75f, 0.75f),
            new Vector2(0.75f, 0.25f),
            new Vector2(0.25f, 0.25f),
        };

        return new ImportedMesh(
            vertices,
            [0, 1, 2, 0, 2, 3],
            new AxisAlignedBoundingBox(new Vector3(-100, -100, 0), new Vector3(100, 100, 0)),
            new BoundingSphere(Vector3.Zero, 150.0f),
            hidden: false,
            cameraOriented: false,
            hasHouseColor: false,
            isSkinned: skinned,
            materials: [material],
            meshParts:
            [
                new ImportedMeshPart(
                    uv0,
                    uv1,
                    startIndex: 0,
                    indexCount: 6,
                    blendEnabled,
                    materialIndex: 0),
            ]);
    }

    private static ImportedModel CreateImportedModel(ImportedMesh mesh)
    {
        return new ImportedModel(
            "PbrProofModel",
            new ImportedSkeleton(
                "PbrProofSkeleton",
                new[]
                {
                    new ImportedBone("Root", -1, Vector3.Zero, Quaternion.Identity),
                    new ImportedBone("Child", 0, new Vector3(0, 0, 50f), Quaternion.Identity),
                }),
            [new ImportedSubObject("ProofContainer.Quad", "Quad", 0, mesh)],
            ImportedSkeletonOrigin.Embedded);
    }

    private static ImportedTextureBinding Bind(
        string name,
        int uvSet,
        byte[] pngBytes,
        ImportedTextureRole role)
    {
        return new ImportedTextureBinding(
            ImportedImageSource.CreateEmbeddedBytes(pngBytes, "image/png", name),
            uvSet,
            new ImportedSampler(),
            role);
    }

    private static byte[] EncodePng(byte r, byte g, byte b)
    {
        using var image = new Image<Rgba32>(2, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                image[x, y] = new Rgba32(r, g, b, 255);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    private static byte[] EncodeCheckerPng()
    {
        using var image = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var dark = (x / 4 + y / 4) % 2 == 0;
                image[x, y] = dark ? new Rgba32(40, 40, 40, 255) : new Rgba32(220, 220, 220, 255);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    private static byte[] EncodeNormalPng()
    {
        // Flat tangent-space normal (128, 128, 255).
        return EncodePng(128, 128, 255);
    }

    private static (byte R, byte G, byte B) LinearToSrgbByte(in Vector3 linear)
    {
        return (
            ToByte(LinearToSrgbScalar(linear.X)),
            ToByte(LinearToSrgbScalar(linear.Y)),
            ToByte(LinearToSrgbScalar(linear.Z)));

        static float LinearToSrgbScalar(float value)
        {
            return value <= 0.0031308f
                ? value * 12.92f
                : 1.055f * MathF.Pow(value, 1.0f / 2.4f) - 0.055f;
        }

        static byte ToByte(float srgb)
        {
            return (byte)Math.Clamp((int)MathF.Round(srgb * 255.0f), 0, 255);
        }
    }

    private static void WriteValidationGuide(string outputDirectory)
    {
        const string guide = """
# PBR Material Proof — Validation Guide

Every capture renders one synthetic quad through the core PBR shader family. Expected checks:

| Capture | Expected check |
|---|---|
| pbr-dielectric | Matte red-brown diffuse response under default lighting; no specular hotspot. |
| pbr-metal-smooth | Bright specular hotspot; near-black diffuse (metal kills diffuse albedo). |
| pbr-dielectric-matte | Same base color as metal-smooth but broad, flat diffuse and no hotspot. |
| pbr-metallic-roughness-texture | Roughness from the checker G channel modulates the hotspot. |
| pbr-normal-imported-tangents | Flat normal map: shading matches an unmapped quad (identity normal). |
| pbr-normal-generated-tangents | Identical to normal-imported-tangents (generated basis equals flat map). |
| pbr-occlusion-emissive | Red emissive added on top of occlusion-modulated ambient. |
| pbr-alpha-mask | Fully opaque texels (alpha 1 > cutoff 0.5): complete quad. |
| pbr-alpha-blend | Green quad at half opacity; background shows through. |
| pbr-double-sided | Front-facing quad renders; back-face culling disabled (compare with dielectric). |
| pbr-uv1-selection | Same flat red as an embedded base color, sampled from UV set 1. |
| pbr-sampler-repeat | 4x4 checker tiles repeated across the quad (UVs span 0..2). |
| pbr-sampler-clamp | Checker stretched: edge pixels smeared instead of tiled. |
| pbr-filter-nearest | Hard checker edges under magnification. |
| pbr-filter-linear | Blended checker edges under magnification (compare with nearest). |
| pbr-skinned | Blue diffuse quad, identical family and lighting to dielectric. |
| pbr-colorspace-emissive | Known emissive sRGB round trip; asserted programmatically within one 8-bit step. |

Determinism: the dielectric capture is repeated and byte-compared automatically.
""";
        File.WriteAllText(Path.Combine(outputDirectory, "validation_guide.md"), guide);
    }
}
