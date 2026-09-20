#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.Content.Loaders;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Graphics.Animation;
using OpenSage.IO;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Utilities;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Headless C4 runtime proof: imports the committed glTF fixtures through the internal glTF
/// importers, materializes them through C1-C3, binds animations through C2, and captures the
/// static PBR model, two rigid-node animation times, and three skinned animation times
/// (including one between step keys). Writes a validation guide next to the captures.
/// </summary>
internal static class GltfRuntimeProofScenario
{
    private const int CaptureSize = 256;

    public const float CaptureMagnification = 20f;

    /// <summary>
    /// Presentation-only yaw applied to captures. glTF front (+Z) maps to OpenSAGE +X, so
    /// unrotated fixtures present vertical walls edge-on to the default arcball view; the yaw
    /// turns the model toward the camera without altering converted data.
    /// </summary>
    public const float CaptureYawRadians = -MathF.PI / 3f;

    public static readonly string[] RequiredFixtures =
    [
        "pbr-materials.gltf",
        "pbr-materials.bin",
        "rigid-scene.gltf",
        "rigid-scene.bin",
        "animated-skin.glb",
    ];

    /// <summary>
    /// Fixed capture times: fixture key, animation index (null = static), and capture times
    /// in seconds. The skinned set includes 0.5, which sits between the STEP translation keys
    /// at 0 and 1.
    /// </summary>
    public static readonly (string Fixture, int? AnimationIndex, float[] Times)[] CapturePlan =
    [
        ("pbr-materials.gltf", null, [0f]),
        ("rigid-scene.gltf", 0, [0f, 1f]),
        ("animated-skin.glb", 0, [0f, 0.5f, 1f]),
    ];

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var fixtureRoot = config.GltfFixtureRoot;
        if (string.IsNullOrEmpty(fixtureRoot))
        {
            reporter.Error("Missing --gltf-fixture-root <path> (or GltfFixtureRoot in TestHarness.json).");
            return 1;
        }

        var missing = MissingFixtures(fixtureRoot);
        if (missing.Count > 0)
        {
            reporter.Error($"Fixture root '{fixtureRoot}' is missing: {string.Join(", ", missing)}.");
            return 1;
        }

        var outputDirectory = "gltf-runtime-proof";
        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        var fixtureFileSystem = new DiskFileSystem(fixtureRoot);
        var importContext = new AssetImportContext(fixtureFileSystem, language: null);
        var options = GltfImportOptions.Default;
        var modelImporter = new GltfModelImporter(options);
        var animationImporter = new GltfAnimationImporter(options);

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(fixtureFileSystem, gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        var captures = new List<(string FileName, string Check)>();

        // Warm-up frames: absorb first-execution engine-global uploads. The second frame is
        // textured, because the first textured pipeline execution after device creation is
        // unstable (see PbrMaterialProof).
        var warmUp = ImportedModelMaterializer.MaterializeModel(
            modelImporter.ImportModel("static-triangle.glb", importContext)!,
            loadContext);
        StandaloneModelCapture.Capture(
            game,
            warmUp,
            loadContext,
            Path.Combine(Path.GetTempPath(), "gltf-proof-warmup.png"),
            new Size(CaptureSize, CaptureSize));
        var texturedWarmUp = ImportedModelMaterializer.MaterializeModel(
            modelImporter.ImportModel("pbr-materials.gltf", importContext)!,
            loadContext);
        StandaloneModelCapture.Capture(
            game,
            texturedWarmUp,
            loadContext,
            Path.Combine(Path.GetTempPath(), "gltf-proof-warmup-textured.png"),
            new Size(CaptureSize, CaptureSize));

        // The first animated capture path (animation instance + per-bone transforms) is also
        // unstable on first execution; warm it up the same way.
        var animatedWarmUpModel = ImportedModelMaterializer.MaterializeModel(
            modelImporter.ImportModel("rigid-scene.gltf", importContext)!,
            loadContext);
        var animatedWarmUpAnimation = ImportedAnimationMaterializer.Materialize(
            animationImporter.ImportAnimation("rigid-scene.gltf#animation=0", importContext)!);
        StandaloneModelCapture.Capture(
            game,
            animatedWarmUpModel,
            loadContext,
            Path.Combine(Path.GetTempPath(), "gltf-proof-warmup-animated.png"),
            new Size(CaptureSize, CaptureSize),
            instance => PlayToTime(instance, animatedWarmUpAnimation, 0f));

        foreach (var (fixture, animationIndex, times) in CapturePlan)
        {
            var importedModel = modelImporter.ImportModel(fixture, importContext)
                ?? throw new InvalidOperationException($"Fixture import returned null: {fixture}");
            var model = ImportedModelMaterializer.MaterializeModel(importedModel, loadContext);

            Graphics.Animation.ModelAnimation? animation = null;
            if (animationIndex is { } index)
            {
                var importedAnimation = animationImporter.ImportAnimation($"{fixture}#animation={index}", importContext)
                    ?? throw new InvalidOperationException($"Fixture animation import returned null: {fixture}");
                animation = ImportedAnimationMaterializer.Materialize(importedAnimation);
            }

            for (var i = 0; i < times.Length; i++)
            {
                var time = times[i];
                var outputName = $"gltf-{Path.GetFileNameWithoutExtension(fixture)}-{(animation is null ? "static" : $"t{time:0.0##}".Replace('.', '_'))}.png";
                var outputPath = Path.Combine(outputDirectory, outputName);

                // Capture framing (presentation only): fixture models are ~1 meter (3.28 world
                // units), and their glTF +Z front maps to OpenSAGE +X; a uniform scale keeps
                // them past the near plane and a yaw turns them toward the camera. Converted
                // data is unchanged.
                StandaloneModelCapture.Capture(
                    game,
                    model,
                    loadContext,
                    outputPath,
                    new Size(CaptureSize, CaptureSize),
                    animation is null
                        ? null
                        : instance => PlayToTime(instance, animation, time),
                    Matrix4x4.CreateRotationZ(CaptureYawRadians) * Matrix4x4.CreateScale(CaptureMagnification));

                captures.Add((outputName, DescribeCheck(fixture, animation is not null, time)));
                reporter.Info($"Captured {outputName}");
            }
        }

        WriteValidationGuide(outputDirectory, captures);
        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    public static IReadOnlyList<string> MissingFixtures(string fixtureRoot)
    {
        var missing = new List<string>();
        foreach (var fixture in RequiredFixtures)
        {
            if (!File.Exists(Path.Combine(fixtureRoot, fixture)))
            {
                missing.Add(fixture);
            }
        }

        return missing;
    }

    private static void PlayToTime(ModelInstance instance, Graphics.Animation.ModelAnimation animation, float seconds)
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

    private static string DescribeCheck(string fixture, bool animated, float time)
    {
        return fixture switch
        {
            "pbr-materials.gltf" => "Upright Z-up model, +X forward, one-meter triangle at 3.28084 world units, base-color and emissive textures visible, alpha mask and double-sided state from the material",
            "rigid-scene.gltf" when time == 0f => "Rig rest pose: named 'arm' sub-object at its bind position; winding preserved (triangle visible from +Z camera)",
            "rigid-scene.gltf" => "Named animation moved the attached unskinned 'arm' sub-object upward (+Y glTF = +Z world) and rotated it; named binding through C2",
            "animated-skin.glb" when time == 0.5f => "Between STEP translation keys: joint holds the t=0 translation while LINEAR rotation is mid-interpolation",
            "animated-skin.glb" => "Two-influence skin deformation at a keyed time: inverse binds and non-identity rest pose position the vertices on jointB",
            _ => "Skinned model at rest",
        };
    }

    private static void WriteValidationGuide(string outputDirectory, List<(string FileName, string Check)> captures)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# glTF runtime proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture comes from GltfModelImporter -> ImportedModelMaterializer ->");
        writer.WriteLine("runtime Model -> StandaloneModelCapture; animated captures add C2 named");
        writer.WriteLine("binding through ModelInstance.CreateAnimationInstance. Deterministic repeat:");
        writer.WriteLine("a second run must produce the same frame set. All captures apply a uniform");
        writer.WriteLine($"magnification of {CaptureMagnification}x so ~1-meter fixtures clear the diagnostic");
        writer.WriteLine("camera's near plane; the one-meter check therefore reads one meter =");
        writer.WriteLine($"{CaptureMagnification} * 3.28084 world units in the rendered frame.");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
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
}
