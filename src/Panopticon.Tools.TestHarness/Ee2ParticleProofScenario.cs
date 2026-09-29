#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.FileFormats.Nif;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.ParticleSystems;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Input;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Settings;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Headless C4h proof: the three DDF-bound EE2 legacy particle effects import
/// through NifFile.Read -&gt; NifParticleEffectConverter -&gt;
/// ImportedParticleEffectMaterializer (world scale from the DDF binding baked
/// into template sizes, speeds, and volumes) and render through the existing
/// particle runtime (FXParticleSystemTemplate -&gt; Scene3D.ParticleSystemManager
/// -&gt; RenderPipeline particle bucket). fx_explosion_1 and fx_watersplash_1
/// spawn at fixed world origins; fx_muzzle_tank attaches to the Muzzle tag of
/// the imported lhm13_mainbattletank model through the follow-matrix overload.
/// Every frame replays from t=0 under a seeded random scope, so repeated runs
/// produce identical captures. No EE2 asset is copied or committed.
/// </summary>
internal static class Ee2ParticleProofScenario
{
    private const int CaptureSize = 512;

    public const string ArchiveRelativePath = "zips/graphics.zip";
    public const string TexturesArchiveRelativePath = "zips/textures.zip";

    /// <summary>The muzzle proof model: a cooked unit model with a Muzzle node.</summary>
    public const string MuzzleModelKey = "lhm13_mainbattletank";

    /// <summary>Presentation-only yaw, matching the other EE2 model proofs.</summary>
    public const float CaptureYawRadians = -MathF.PI / 3f;

    private const float CaptureOrbitRadians = -MathF.PI / 3f;
    private const float CaptureElevationRadians = 0.4f;
    private const float CaptureFieldOfViewRadians = MathF.PI / 4f;

    /// <summary>
    /// C4h capture plan: each DDF-bound proof effect at 0.0 s, 0.5 s, and
    /// 1.0 s. Frame 0000 is the first emitted burst (one 30 Hz tick), not an
    /// empty spawn.
    /// </summary>
    public static readonly (string EffectName, float TimeSeconds, string Check)[] CapturePlan =
    [
        ("fx_explosion_1", 0f,
            "first burst just emitted: two tiny bright additive spark clusters at the world " +
            "origin (grow-from-zero keeps the first tick's quads small)"),
        ("fx_explosion_1", 0.5f,
            "additive fireball ~180 px wide at the origin with a rim of ~10-20 px sparks; " +
            "first system (0.5 s lifetime) already dead, second system at full intensity"),
        ("fx_explosion_1", 1f,
            "dissipating smoke ring ~260 px at half the peak intensity; second system's " +
            "particles (1 s lifetime, emission stop 0.225 s) in their final frames"),
        ("fx_muzzle_tank", 0f,
            "main battle tank at the fixed three-quarter yaw; the first burst's puffs are " +
            "sub-pixel because every muzzle system grows from size zero"),
        ("fx_muzzle_tank", 0.5f,
            "small smoke puffs (tens of px) at the resolved Muzzle tag beside the barrel; " +
            "the tank itself unchanged"),
        ("fx_muzzle_tank", 1f,
            "puffs grown larger (grow windows 0.33-1.25 s at EffectScale 0.05) and spread " +
            "along the muzzle axis; still additive and anchored at the barrel"),
        ("fx_watersplash_1", 0f,
            "single small seed quad (~20 px) at the fixed splash origin: the first particle " +
            "of the 15/s column system"),
        ("fx_watersplash_1", 0.5f,
            "centered spray column of overlapping additive quads (mature sizes ~85 px at " +
            "this camera); clearly denser and wider than 0000"),
        ("fx_watersplash_1", 1f,
            "column larger and brighter than 0500: the late systems (emission start 0.5 s, " +
            "lifetimes up to 2.67 s) are now dominant and still growing"),
    ];

    internal static string GetParticleCaptureFileName(string effectName, float timeSeconds)
    {
        var milliseconds = (int)MathF.Round(timeSeconds * 1000f);
        return $"{effectName}-particle-{milliseconds:0000}.png";
    }

    /// <summary>Stable per-effect seed; each capture replays from t=0 under this scope.</summary>
    internal static int GetEffectSeed(string effectName)
    {
        return ParticleSystemUtility.ComputeDeterministicSeed("c4h-particle-proof:" + effectName, 0);
    }

    /// <summary>Camera distance for the world-spawn effects; muzzle frames fit the tank bounds.</summary>
    internal static float GetEffectCameraDistance(string effectName)
    {
        return effectName switch
        {
            "fx_explosion_1" => 6f,
            "fx_watersplash_1" => 45f,
            _ => 45f,
        };
    }

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var ee2Root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(ee2Root))
        {
            reporter.Error("Missing EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install (source effects).");
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

        const string outputDirectory = "ee2-particle-proof";
        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        using var ee2FileSystem = new ZipFileSystem(archivePath);
        using var ee2TexturesFileSystem = new ZipFileSystem(texturesArchivePath);
        var compositeFileSystem = new CompositeFileSystem(ee2FileSystem, ee2TexturesFileSystem);

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        // Import every bound effect first so texcache textures install before
        // any ParticleSystem constructor resolves its texture reference.
        var bindings = NifParticleEffectBinding.GetProofBindings();
        var effects = new List<(NifParticleEffectBinding Binding, MaterializedParticleEffect Materialized)>();
        foreach (var binding in bindings)
        {
            var effect = ImportEffect(binding, compositeFileSystem);
            var installed = Ee2TexcacheTextureInstaller.Install(
                game,
                compositeFileSystem,
                effect.Systems.Select(s => s.TextureName).Where(n => n is not null).Cast<string>().Distinct());
            reporter.Info($"Installed {installed} texcache textures for '{binding.EffectName}'.");

            var materialized = ImportedParticleEffectMaterializer.Materialize(
                effect,
                loadContext.AssetStore.Textures.GetLazyAssetReferenceByName,
                binding.WorldScale);
            effects.Add((binding, materialized));
            reporter.Info(
                $"Materialized '{binding.EffectName}' into {materialized.Systems.Count} particle systems " +
                $"at world scale {binding.WorldScale}.");
        }

        // The muzzle proof model: same import pipeline as the other EE2 model
        // proofs; its Muzzle tag hosts the follow-matrix smoke system.
        var importContext = new AssetImportContext(compositeFileSystem, language: null);
        var muzzleBinding = bindings.Single(b => b.EffectName == "fx_muzzle_tank");
        var muzzleModelImporter = new NifModelImporter();
        var muzzleImported = muzzleModelImporter.ImportModel(MuzzleModelKey, importContext)
            ?? throw new InvalidOperationException($"EE2 import returned null for '{MuzzleModelKey}'.");
        Ee2TexcacheTextureInstaller.Install(
            game,
            compositeFileSystem,
            Ee2NifProofScenario.CollectTextureNames(muzzleImported));
        var muzzleModel = ImportedModelMaterializer.MaterializeModel(muzzleImported, loadContext);
        reporter.Info($"Imported muzzle proof model '{MuzzleModelKey}'.");

        var captures = new List<(string FileName, string Check)>();
        foreach (var (binding, materialized) in effects)
        {
            // First execution of the particle material path after device
            // creation is unstable; warm one identical frame per effect.
            var warmUpPath = Path.Combine(Path.GetTempPath(), $"ee2-particle-proof-warmup-{binding.EffectName}.png");
            CaptureFrame(
                game,
                loadContext,
                binding,
                materialized,
                muzzleModel: binding.EffectName == muzzleBinding.EffectName ? muzzleModel : null,
                timeSeconds: 0f,
                ticks: 1,
                outputPath: warmUpPath,
                reporter);

            foreach (var (_, timeSeconds, check) in CapturePlan.Where(e => e.EffectName == binding.EffectName))
            {
                var fileName = GetParticleCaptureFileName(binding.EffectName, timeSeconds);
                CaptureFrame(
                    game,
                    loadContext,
                    binding,
                    materialized,
                    muzzleModel: binding.EffectName == muzzleBinding.EffectName ? muzzleModel : null,
                    timeSeconds,
                    ticks: 1 + (int)MathF.Round(timeSeconds * 30f, MidpointRounding.AwayFromZero),
                    Path.Combine(outputDirectory, fileName),
                    reporter);
                captures.Add((fileName, check));
                reporter.Info($"Captured {fileName}");
            }
        }

        Ee2NifProofScenario.WriteValidationGuide(
            outputDirectory,
            captures,
            title: "# EE2 particle proof - validation guide",
            preamble:
            [
                "Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->",
                "NifFile.Read -> NifParticleEffectConverter -> ImportedParticleEffectMaterializer ->",
                "FXParticleSystemTemplate -> Scene3D.ParticleSystemManager -> RenderPipeline particle",
                "bucket. DDF world scales (explosion 0.005, splash 0.15) are baked into template",
                "sizes, speeds, and emitter volumes because the runtime does not scale particle",
                "quad sizes by the world matrix. Muzzle frames render lhm13_mainbattletank through",
                "the C4 model pipeline with the smoke following its Muzzle tag via the",
                "follow-matrix overload; explosion and splash frames spawn at the world origin.",
                "Frame 0000 is the first emitted burst (one 30 Hz tick), not an empty spawn; each",
                "frame replays from t=0 under ParticleSystemUtility.BeginSeedScope seeded from the",
                "effect key, so a second run must produce identical frames. No EE2 asset is copied",
                "or committed.",
            ]);

        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    public static string DescribePrerequisites()
    {
        return $"EMPIRE_EARTH_II_PATH (source effects; needs {ArchiveRelativePath} and " +
            $"{TexturesArchiveRelativePath}) and the rendering host installation resolved by the harness";
    }

    private static ImportedParticleEffect ImportEffect(NifParticleEffectBinding binding, FileSystem fileSystem)
    {
        var entry = fileSystem.GetFile(binding.NifAssetKey)
            ?? throw new InvalidOperationException($"EE2 effect entry missing: {binding.NifAssetKey}");

        // Zip entry streams are not seekable; buffer before parsing.
        using var buffered = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(buffered);
        }

        buffered.Position = 0;

        var file = NifFile.Read(buffered);
        return NifParticleEffectConverter.Convert(file, binding.EffectName)
            ?? throw new InvalidOperationException(
                $"EE2 effect '{binding.EffectName}' ({binding.NifAssetKey}) contains no NiParticles blocks.");
    }

    private static void CaptureFrame(
        Game game,
        AssetLoadContext loadContext,
        NifParticleEffectBinding binding,
        MaterializedParticleEffect materialized,
        Model? muzzleModel,
        float timeSeconds,
        int ticks,
        string outputPath,
        ITestHarnessReporter reporter)
    {
        var inputMessageBuffer = new InputMessageBuffer();
        var width = (float)CaptureSize;
        var height = (float)CaptureSize;
        var cameraController = new ArcballCameraController(Vector3.Zero, 10f);
        using var scene = new Scene3D(
            game,
            inputMessageBuffer,
            () => new Viewport(0, 0, width, height, 0, 1),
            cameraController,
            WorldLighting.CreateDefault(),
            isDiagnosticScene: true);
        using var renderPipeline = new RenderPipeline(game);
        renderPipeline.ClearColor = new RgbaFloat(0.2f, 0.4f, 0.9f, 1f);
        using var renderTarget = new RenderTarget(game.GraphicsDevice);
        renderTarget.EnsureSize(new Size(CaptureSize, CaptureSize));

        ModelInstance? muzzleInstance = null;
        NifParticleAttachmentPlan? muzzlePlan = null;
        var modelWorld = Matrix4x4.CreateRotationZ(CaptureYawRadians);
        var cameraTarget = Vector3.Zero;
        var cameraDistance = GetEffectCameraDistance(binding.EffectName);

        if (muzzleModel is not null)
        {
            muzzleInstance = muzzleModel.CreateInstance(loadContext);
            MuzzleFlashBones.HideForStandaloneRender(muzzleInstance, game.AssetStore.ObjectDefinitions);
            muzzleInstance.SetWorldMatrix(modelWorld);
            muzzleInstance.Update(TimeInterval.Zero);

            var bones = muzzleModel.BoneHierarchy.Bones;
            muzzlePlan = NifParticleEffectAttachmentResolver.ResolveToBone(
                binding,
                bones,
                boneIndex => muzzleInstance.AbsoluteBoneTransforms[boneIndex]);

            var bounds = StandaloneModelCapture.ComputeEnclosingBoundingBox(muzzleModel);
            var corners = new Vector3[8];
            var index = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                corners[index++] = Vector3.Transform(
                    new Vector3(
                        (bit & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                        (bit & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                        (bit & 4) == 0 ? bounds.Min.Z : bounds.Max.Z),
                    modelWorld);
            }

            (cameraTarget, cameraDistance) = StandaloneModelCapture.FitCamera(
                AxisAlignedBoundingBox.CreateFromPoints(corners));
        }

        ((IEditorCameraController)cameraController).UpdateCamera(
            scene.Camera,
            default,
            TimeInterval.Zero);

        var offset = new Vector3(
            MathF.Cos(CaptureElevationRadians) * MathF.Cos(CaptureOrbitRadians),
            MathF.Cos(CaptureElevationRadians) * MathF.Sin(CaptureOrbitRadians),
            MathF.Sin(CaptureElevationRadians)) * cameraDistance;
        scene.Camera.SetLookAt(cameraTarget + offset, cameraTarget, Vector3.UnitZ);
        scene.Camera.FieldOfView = CaptureFieldOfViewRadians;
        scene.Camera.NearPlaneDistance = MathF.Max(cameraDistance / 100f, 0.05f);

        using (ParticleSystemUtility.BeginSeedScope(GetEffectSeed(binding.EffectName)))
        {
            var manager = scene.ParticleSystemManager;
            var created = new List<ParticleSystem>();
            foreach (var system in materialized.Systems)
            {
                if (muzzlePlan?.FollowMatrix is { } follow)
                {
                    var composed = new ComposedFollowSource();
                    created.Add(manager.Create(system.Template, composed.Bind(system.EmitterTransform, follow)));
                }
                else
                {
                    var spawn = system.EmitterTransform * Matrix4x4.CreateTranslation(cameraTarget);
                    created.Add(manager.Create(system.Template, spawn));
                }
            }

            for (var tick = 1; tick <= ticks; tick++)
            {
                scene.ParticleSystemManager.Update(
                    new TimeInterval(
                        TimeSpan.FromSeconds(tick / 30.0),
                        TimeSpan.FromSeconds(1.0 / 30.0)));
            }
        }

        void OnBuildingRenderList(object sender, BuildingRenderListEventArgs e)
        {
            if (muzzleInstance is null)
            {
                return;
            }

            muzzleInstance.SetWorldMatrix(modelWorld);
            muzzleInstance.BuildRenderList(
                e.RenderList,
                e.Camera,
                true,
                new MeshShaderResources.RenderItemConstantsPS
                {
                    HouseColor = Vector3.One,
                    Opacity = 1.0f,
                    TintColor = Vector3.One,
                });

            StandaloneModelCapture.FlushMeshWorldConstants(game, muzzleInstance);
        }

        renderPipeline.BuildingRenderList += OnBuildingRenderList;
        try
        {
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
            muzzleInstance?.Dispose();
        }
    }

    /// <summary>
    /// Composes the materializer's emitter transform in front of a resolver
    /// follow matrix on every read; the field backs the ref-returning delegate.
    /// </summary>
    private sealed class ComposedFollowSource
    {
        private Matrix4x4 _value;

        public ParticleSystem.GetMatrixReferenceDelegate Bind(
            Matrix4x4 prefix,
            ParticleSystem.GetMatrixReferenceDelegate inner)
        {
            return () =>
            {
                _value = prefix * inner();
                return ref _value;
            };
        }
    }
}
