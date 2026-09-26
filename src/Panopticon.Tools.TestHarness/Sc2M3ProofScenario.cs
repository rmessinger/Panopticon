#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.M3;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Input;
using Panopticon.IO;
using Panopticon.Mathematics;
using Panopticon.Settings;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

internal static class Sc2M3ProofScenario
{
    private const int CaptureSize = 1024;
    private const string OutputDirectory = "sc2-m3-proof";

    public const string RangerModelName = "AIRngr_SKN";

    public static readonly (string Label, string Path)[] ProofModels =
    [
        ("Marine", "mods/liberty.sc2mod/base.sc2assets/assets/units/terran/marine/marine.m3"),
        ("Zealot", "mods/liberty.sc2mod/base.sc2assets/assets/units/protoss/zealot/zealot.m3"),
        ("Zergling", "mods/liberty.sc2mod/base.sc2assets/assets/units/zerg/zergling/zergling.m3"),
    ];

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var sc2Root = Environment.GetEnvironmentVariable("STARCRAFT_II_PATH");
        if (string.IsNullOrEmpty(sc2Root))
        {
            reporter.Error("Missing STARCRAFT_II_PATH pointing at a StarCraft II install.");
            return 1;
        }

        if (!File.Exists(Path.Combine(sc2Root, ".build.info")))
        {
            reporter.Error($"Missing StarCraft II .build.info at '{sc2Root}'.");
            return 1;
        }

        Directory.CreateDirectory(OutputDirectory);
        foreach (var existing in Directory.EnumerateFiles(OutputDirectory, "sc2-static-*.png"))
        {
            File.Delete(existing);
        }

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        using var cascFileSystem = new CascFileSystem(sc2Root);
        var importContext = new AssetImportContext(cascFileSystem, language: null);
        var importer = new M3ModelImporter();

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(cascFileSystem, gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        var ranger = game.AssetStore.Models.GetByName(RangerModelName);
        if (ranger is null)
        {
            reporter.Error($"Missing Generals Ranger proof model '{RangerModelName}'.");
            return 1;
        }

        var subjects = new List<CaptureSubject>
        {
            new("Ranger", ranger, gameLoadContext),
        };

        foreach (var (label, path) in ProofModels)
        {
            var imported = importer.ImportModel(path, importContext)
                ?? throw new InvalidOperationException($"SC2 import returned null for '{path}'.");
            var model = ImportedModelMaterializer.MaterializeModel(imported, loadContext);
            subjects.Add(new CaptureSubject(label, model, loadContext));
            reporter.Info($"Imported {label}: {path}");
        }

        var placedSubjects = PlaceSubjects(subjects);
        var warmUpPath = Path.Combine(OutputDirectory, "sc2-static-warmup.png");
        CaptureScene(
            game,
            placedSubjects,
            warmUpPath,
            new StandaloneCaptureCamera(
                OrbitRadians: 0f,
                ElevationRadians: MathF.PI / 7f,
                Distance: 130f,
                FieldOfViewRadians: MathF.PI / 5f));
        File.Delete(warmUpPath);

        CaptureScene(
            game,
            placedSubjects,
            Path.Combine(OutputDirectory, "sc2-static-front.png"),
            new StandaloneCaptureCamera(
                OrbitRadians: 0f,
                ElevationRadians: MathF.PI / 7f,
                Distance: 130f,
                FieldOfViewRadians: MathF.PI / 5f));

        CaptureScene(
            game,
            placedSubjects,
            Path.Combine(OutputDirectory, "sc2-static-three-quarter.png"),
            new StandaloneCaptureCamera(
                OrbitRadians: -MathF.PI / 4f,
                ElevationRadians: MathF.PI / 7f,
                Distance: 150f,
                FieldOfViewRadians: MathF.PI / 5f));

        WriteValidationGuide(OutputDirectory);
        reporter.Info($"SC2 M3 proof captures written to {Path.GetFullPath(OutputDirectory)}");
        reporter.Info($"Ranger model: {RangerModelName}");
        return 0;
    }

    private static IReadOnlyList<PlacedCaptureSubject> PlaceSubjects(IReadOnlyList<CaptureSubject> subjects)
    {
        var bounds = new AxisAlignedBoundingBox[subjects.Count];
        var widths = new float[subjects.Count];
        var totalWidth = 0.0f;
        const float Padding = 4.0f;

        for (var i = 0; i < subjects.Count; i++)
        {
            bounds[i] = StandaloneModelCapture.ComputeEnclosingBoundingBox(subjects[i].Model);
            widths[i] = MathF.Max(bounds[i].Max.Y - bounds[i].Min.Y, 4.0f);
            totalWidth += widths[i];
        }

        totalWidth += Padding * (subjects.Count - 1);
        var cursor = -totalWidth / 2.0f;
        var placed = new List<PlacedCaptureSubject>(subjects.Count);
        for (var i = 0; i < subjects.Count; i++)
        {
            var center = bounds[i].GetCenter();
            var y = cursor + widths[i] / 2.0f;
            var translation = new Vector3(-center.X, y - center.Y, -bounds[i].Min.Z);
            placed.Add(new PlacedCaptureSubject(subjects[i], Matrix4x4.CreateTranslation(translation)));
            cursor += widths[i] + Padding;
        }

        return placed;
    }

    private static void CaptureScene(
        Game game,
        IReadOnlyList<PlacedCaptureSubject> subjects,
        string outputPath,
        StandaloneCaptureCamera camera)
    {
        var combinedBounds = ComputeCombinedBounds(subjects);
        var cameraTarget = combinedBounds.GetCenter();
        var inputMessageBuffer = new InputMessageBuffer();
        using var scene = new Scene3D(
            game,
            inputMessageBuffer,
            () => new Viewport(0, 0, CaptureSize, CaptureSize, 0, 1),
            new ArcballCameraController(cameraTarget, camera.Distance),
            CreateProofLighting(),
            isDiagnosticScene: true);
        using var renderPipeline = new RenderPipeline(game);
        renderPipeline.ClearColor = new RgbaFloat(0.18f, 0.24f, 0.30f, 1f);
        using var renderTarget = new RenderTarget(game.GraphicsDevice);
        renderTarget.EnsureSize(new Size(CaptureSize, CaptureSize));

        var instances = new List<PlacedCaptureInstance>(subjects.Count);
        try
        {
            foreach (var subject in subjects)
            {
                var instance = subject.Subject.Model.CreateInstance(subject.Subject.LoadContext);
                instance.Update(TimeInterval.Zero);
                instances.Add(new PlacedCaptureInstance(instance, subject.WorldMatrix));
            }

            void OnBuildingRenderList(object? sender, BuildingRenderListEventArgs e)
            {
                foreach (var (instance, worldMatrix) in instances)
                {
                    instance.SetWorldMatrix(worldMatrix);
                    instance.BuildRenderList(
                        e.RenderList,
                        e.Camera,
                        true,
                        new MeshShaderResources.RenderItemConstantsPS
                        {
                            HouseColor = Vector3.One,
                            Opacity = 1.0f,
                            TintColor = Vector3.One,
                        });

                    StandaloneModelCapture.FlushMeshWorldConstants(game, instance);
                }
            }

            renderPipeline.BuildingRenderList += OnBuildingRenderList;
            try
            {
                var offset = new Vector3(
                    MathF.Cos(camera.ElevationRadians) * MathF.Cos(camera.OrbitRadians),
                    MathF.Cos(camera.ElevationRadians) * MathF.Sin(camera.OrbitRadians),
                    MathF.Sin(camera.ElevationRadians)) * camera.Distance;
                scene.Camera.SetLookAt(cameraTarget + offset, cameraTarget, Vector3.UnitZ);
                scene.Camera.FieldOfView = camera.FieldOfViewRadians;
                scene.Camera.NearPlaneDistance = MathF.Max((combinedBounds.Max - combinedBounds.Min).Length() / 100.0f, 0.05f);

                renderPipeline.Execute(new RenderContext
                {
                    ContentManager = game.ContentManager,
                    GameTime = TimeInterval.Zero,
                    GraphicsDevice = game.GraphicsDevice,
                    RenderTarget = renderTarget.Framebuffer,
                    Scene2D = null,
                    Scene3D = scene,
                });

                FrameCapturer.CaptureToPng(game.GraphicsDevice, renderTarget.ColorTarget, outputPath);
            }
            finally
            {
                renderPipeline.BuildingRenderList -= OnBuildingRenderList;
            }
        }
        finally
        {
            foreach (var (instance, _) in instances)
            {
                instance.Dispose();
            }
        }
    }

    private static AxisAlignedBoundingBox ComputeCombinedBounds(IReadOnlyList<PlacedCaptureSubject> subjects)
    {
        var points = new List<Vector3>(subjects.Count * 8);
        foreach (var subject in subjects)
        {
            var bounds = StandaloneModelCapture.ComputeEnclosingBoundingBox(subject.Subject.Model);
            AddTransformedCorners(points, bounds, subject.WorldMatrix);
        }

        return AxisAlignedBoundingBox.CreateFromPoints(points);
    }

    private static void AddTransformedCorners(List<Vector3> points, AxisAlignedBoundingBox bounds, Matrix4x4 transform)
    {
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Min.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Min.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Min.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Min.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Min.Y, bounds.Max.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Min.Y, bounds.Max.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Min.X, bounds.Max.Y, bounds.Max.Z), transform));
        points.Add(Vector3.Transform(new Vector3(bounds.Max.X, bounds.Max.Y, bounds.Max.Z), transform));
    }

    private static void WriteValidationGuide(string outputDirectory)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# SC2 M3 proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture mounts STARCRAFT_II_PATH through CascFileSystem, imports the three");
        writer.WriteLine("proof M3 models, materializes embedded PNG textures through the C3 PBR path,");
        writer.WriteLine($"and renders them beside Generals `{RangerModelName}`. No SC2 asset is copied.");
        writer.WriteLine();
        writer.WriteLine("- `sc2-static-front.png`: camera on +X. All units should face the camera.");
        writer.WriteLine("- `sc2-static-three-quarter.png`: camera between +X and -Y. Upright silhouettes,");
        writer.WriteLine("  texture detail, normal mapping, and scale versus the Ranger should be visible.");
    }

    private static WorldLighting CreateProofLighting()
    {
        var keyLight = new GlobalShaderResources.Light
        {
            Ambient = new Vector3(0.45f, 0.45f, 0.45f),
            Direction = Vector3.Normalize(new Vector3(-0.35f, 0.20f, -0.80f)),
            Color = new Vector3(0.95f, 0.95f, 1.0f),
        };
        var fillLight = new GlobalShaderResources.LightingConfiguration
        {
            Light0 = keyLight,
            Light1 = new GlobalShaderResources.Light
            {
                Ambient = new Vector3(0.25f, 0.25f, 0.25f),
                Direction = Vector3.Normalize(new Vector3(0.35f, -0.30f, 0.60f)),
                Color = new Vector3(0.55f, 0.55f, 0.60f),
            },
        };

        return new WorldLighting(
            new Dictionary<TimeOfDay, LightSettings>
            {
                [TimeOfDay.Morning] = new(fillLight, fillLight),
            },
            TimeOfDay.Morning);
    }

    private sealed record CaptureSubject(string Label, Model Model, AssetLoadContext LoadContext);

    private sealed record PlacedCaptureSubject(CaptureSubject Subject, Matrix4x4 WorldMatrix);

    private sealed record PlacedCaptureInstance(ModelInstance Instance, Matrix4x4 WorldMatrix);
}
