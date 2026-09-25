#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.FileFormats.Nif;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Headless C4 proof: mounts the operator-supplied EE2 graphics.zip through
/// ZipFileSystem, imports both proof targets through NifModelImporter,
/// materializes them through ImportedModelMaterializer (runtime path:
/// NifModelImporter -> ImportedModelMaterializer -> Model), and captures one
/// static PNG per target plus C4c animated frames at fixed clip times
/// (NifAnimationImporter -> ImportedAnimationMaterializer -> C2 named binding).
/// Fixed-function diffuse renders with texture-stage count zero; source texture
/// names stay in the imported descriptors. No EE2 asset is copied or committed.
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

    /// <summary>
    /// C4c animated plan: deterministic clip times from the deep dive section 8. File names
    /// use the KF stem (which is already <model>_<clip>) plus zero-padded milliseconds.
    /// </summary>
    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] AnimatedCapturePlan =
    [
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0f,
            "start of the rowing stroke: oars squared at their first key angles"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f,
            "mid-stroke: oars swept to intermediate angles, visibly different from 0000"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f,
            "late stroke: oars at their final key angles, different from both 0000 and 0767"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf", 0f,
            "rest pose: identical to the static ee2-nwg1_galley.png capture (idle offsets are identity)"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 0f,
            "ground pose with propellers at their first key, matching the Walk channel values at time 0"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f,
            "both propellers turned a quarter turn (90 degrees) from 0000; airframe otherwise unchanged"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 0f,
            "ground pose: identical to the static ee2-ab11_TwinEngineBomber.png capture (root NonAccum carries the ground pose at time 0)"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f,
            "level raised flight pose: root keyed toward its flight rotation while root NonAccum carries the raised translation, clearly off the 0000 ground stance"),
    ];

    internal static string GetAnimatedCaptureFileName(string kfPath, float timeSeconds)
    {
        var stem = Path.GetFileNameWithoutExtension(kfPath);
        var milliseconds = (int)MathF.Round(timeSeconds * 1000f);
        return $"ee2-{stem}_{milliseconds:0000}.png";
    }

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
        var animationImporter = new NifAnimationImporter();

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

            foreach (var (_, kfPath, timeSeconds, animatedCheck) in AnimatedCapturePlan.Where(e => e.ModelKey == key))
            {
                var importedAnimation = animationImporter.ImportAnimation($"{key}#kf={kfPath}", importContext)
                    ?? throw new InvalidOperationException($"EE2 animation import returned null for '{key}#kf={kfPath}'.");
                var animation = ImportedAnimationMaterializer.Materialize(importedAnimation);

                var animatedName = GetAnimatedCaptureFileName(kfPath, timeSeconds);
                StandaloneModelCapture.Capture(
                    game,
                    model,
                    loadContext,
                    Path.Combine(outputDirectory, animatedName),
                    new Size(CaptureSize, CaptureSize),
                    configureInstance: instance => PlayToTime(instance, animation, timeSeconds),
                    worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                    options: new StandaloneCaptureOptions(
                        Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

                captures.Add((animatedName, animatedCheck));
                reporter.Info($"Captured {animatedName}");
            }
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

    private static void PlayToTime(ModelInstance instance, Panopticon.Graphics.Animation.ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, delta));
        }
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }

    internal static void WriteValidationGuide(string outputDirectory, List<(string FileName, string Check)> captures)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# EE2 NIF proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->");
        writer.WriteLine("NifModelImporter -> texcache decode -> ImportedModelMaterializer -> runtime");
        writer.WriteLine("Model -> StandaloneModelCapture. The cooked DXT mips upload block-for-block");
        writer.WriteLine("(DXT1/DXT3; no transcoding) and the models render textured. Animated frames add");
        writer.WriteLine("NifAnimationImporter -> ImportedAnimationMaterializer -> C2 named binding driven");
        writer.WriteLine("to a fixed clip time before the capture. No EE2 asset is copied or committed.");
        writer.WriteLine("Deterministic repeat: a second run must produce the same frames. Mesh world");
        writer.WriteLine("constants are uploaded synchronously before each capture's passes because");
        writer.WriteLine("hardware Vulkan drivers can otherwise apply the per-item inline updates after");
        writer.WriteLine("the draws that read them (stale world matrices on some meshes).");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
        }
    }
}
