#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Nif;
using OpenSage.Content.Loaders;
using OpenSage.Data;
using OpenSage.FileFormats.Nif;
using OpenSage.Graphics;
using OpenSage.IO;
using OpenSage.Mathematics;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Headless C4a proof: mounts the operator-supplied EE2 graphics.zip through
/// ZipFileSystem, imports both C4a targets through NifModelImporter,
/// materializes them through ImportedModelMaterializer (runtime path:
/// NifModelImporter -> ImportedModelMaterializer -> Model), and captures one
/// PNG per target plus a validation guide. Fixed-function diffuse renders
/// with texture-stage count zero; source texture names stay in the imported
/// descriptors. No EE2 asset is copied or committed.
/// </summary>
internal static class Ee2NifProofScenario
{
    private const int CaptureSize = 512;

    public const string ArchiveRelativePath = "zips/graphics.zip";
    public const string TexturesArchiveRelativePath = "zips/textures.zip";

    /// <summary>
    /// Presentation-only yaw: NIF forward maps to engine +X, so unrotated
    /// models present bow-first to the default arcball view; the yaw turns a
    /// three-quarter view without altering converted data.
    /// </summary>
    public const float CaptureYawRadians = -MathF.PI / 3f;

    public static readonly (string Key, string Check)[] CapturePlan =
    [
        ("nwg1_galley",
            "War Galley: recognizable complete hull with masts and sail, twelve oar pairs baked at " +
            "their rotated node placements, upright orientation (mast up), bow toward +X after the " +
            "source conversion, correct winding and normals (lit surfaces face the camera), " +
            "nwg1_galley.tga texture mapped coherently (no upside-down or mirrored artwork)"),
        ("ab11_TwinEngineBomber",
            "Twin-Engine Bomber: fuselage, wings, both engine props at 0.7416 baked scale, wheels, " +
            "and open cockpits; highest-detail body_LOD0 geometry (no LOD1 simplification seams); " +
            "upright and +X forward; DXT3 alpha blending on wheels/body keeps correct depth; " +
            "ab11_TwinEngineBomber.tga and af11_biplane_prop.tga mapped coherently"),
    ];

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var ee2Root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(ee2Root))
        {
            reporter.Error("Missing EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install (source models).");
            return 1;
        }

        var archivePath = Path.Combine(ee2Root, ArchiveRelativePath);
        if (!File.Exists(archivePath))
        {
            reporter.Error($"Missing EE2 graphics archive at '{archivePath}'.");
            return 1;
        }

        var texturesArchivePath = Path.Combine(ee2Root, TexturesArchiveRelativePath);
        if (!File.Exists(texturesArchivePath))
        {
            reporter.Error($"Missing EE2 textures archive at '{texturesArchivePath}'.");
            return 1;
        }

        var outputDirectory = "ee2-nif-proof";
        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        using var ee2FileSystem = new ZipFileSystem(archivePath);
        using var ee2TexturesFileSystem = new ZipFileSystem(texturesArchivePath);
        var compositeFileSystem = new CompositeFileSystem(ee2FileSystem, ee2TexturesFileSystem);
        var importContext = new AssetImportContext(compositeFileSystem, language: null);
        var importer = new NifModelImporter();

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        var captures = new List<(string FileName, string Check)>();

        foreach (var (key, check) in CapturePlan)
        {
            var imported = importer.ImportModel(key, importContext)
                ?? throw new InvalidOperationException($"EE2 import returned null for '{key}'.");

            // Decode and register the model's cooked textures under their
            // source .tga names so fixed-function materialization resolves them.
            var installed = Ee2TexcacheTextureInstaller.Install(
                game,
                compositeFileSystem,
                CollectTextureNames(imported));
            reporter.Info($"Installed {installed} texcache textures for '{key}'.");

            var model = ImportedModelMaterializer.MaterializeModel(imported, loadContext);

            var outputName = $"ee2-{key}.png";
            var outputPath = Path.Combine(outputDirectory, outputName);

            // First execution of each material pipeline variant after device
            // creation is unstable (see GltfRuntimeProof); warm every model once.
            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                Path.Combine(Path.GetTempPath(), $"ee2-nif-proof-warmup-{key}.png"),
                new Size(CaptureSize, CaptureSize),
                worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                options: new StandaloneCaptureOptions(
                    Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                outputPath,
                new Size(CaptureSize, CaptureSize),
                worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                options: new StandaloneCaptureOptions(
                    Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

            captures.Add((outputName, check));
            reporter.Info($"Captured {outputName}");
        }

        WriteValidationGuide(outputDirectory, captures);
        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    public static IEnumerable<string> CollectTextureNames(ImportedModel model)
    {
        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }
            foreach (var material in mesh.Materials)
            {
                foreach (var property in material.Properties)
                {
                    if (property.Name == "Texture0" &&
                        property.Kind == ImportedMaterialPropertyKind.TextureName)
                    {
                        yield return property.TextureName;
                    }
                }
            }
        }
    }

    public static string DescribePrerequisites()
    {
        return $"EMPIRE_EARTH_II_PATH (source models; needs {ArchiveRelativePath}) and the rendering " +
            "host installation resolved by the harness";
    }

    private static void WriteValidationGuide(string outputDirectory, List<(string FileName, string Check)> captures)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# EE2 NIF proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->");
        writer.WriteLine("NifModelImporter -> texcache decode -> ImportedModelMaterializer -> runtime");
        writer.WriteLine("Model -> StandaloneModelCapture. The cooked DXT mips upload block-for-block");
        writer.WriteLine("(DXT1/DXT3; no transcoding) and the models render textured. No EE2 asset is");
        writer.WriteLine("copied or committed. Deterministic repeat: a second run must produce the");
        writer.WriteLine("same frames.");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
        }
    }
}
