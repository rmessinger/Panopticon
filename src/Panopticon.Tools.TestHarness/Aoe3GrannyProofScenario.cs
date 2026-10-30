using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.Data.Map;
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.Graphics.Shaders;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Settings;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Headless C10b/C10c proof: mounts the operator-supplied AoE3 DE Game/ tree through
/// BarFileSystem, resolves the Musketeer (guard and redcoat techs) and Hussar through
/// Aoe3AnimfileResolver, imports models through GrannyModelImporter with AoE3 material
/// mapping onto C3 PBR, and captures each beside a native Generals Ranger
/// (AIRNGR_SKN). The musketeer march and volley clips and the horse idle clip are
/// imported through GrannyAnimationImporter, bound to their skeletons (shared-library
/// extra tracks are dropped and reported), sampled at fixed times, and captured twice
/// per frame with byte-identical output required. The falconet has an Oodle1-compressed
/// section and must fail with an explicit message naming the file, section index, and
/// compression type. No AoE3 asset is copied or committed.
/// </summary>
public static class Aoe3GrannyProofScenario
{
    private const int CaptureSize = 512;

    private const string MusketeerUnit = "Musketeer";

    private const string HussarUnit = "Hussar";

    private const string FalconetKey = @"Art\units\artillery\falconet\falconet.gr2";

    private const string MusketeerKey = @"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2";

    private const string RedcoatMaterialKey = @"Art\units\infantry_ranged\musketeer\redcoats_age4.material.xmb";

    private const string MusketeerMaterialZipEntry = "Art/units/infantry_ranged/musketeer/musketeer_4age.material.xmb";

    private const string RangerModelName = "AIRNGR_SKN";

    // Generals renders in game units, not meters; the Ranger calibrates the scene.
    // A Generals infantry model is assumed to depict a 1.8 m human.
    private const float ReferenceHumanHeightMeters = 1.8f;

    // Visible extent of the AIRNGR-SKN body mesh (mesh header Y span), pixel-verified
    // against captures. The rest-pose skeleton span (12.03 units) undercounts because
    // helmet, boots, and weapon geometry extend past the bones; the visible mesh is the
    // honest reference for placing models beside each other.
    private const float RangerVisibleHeightUnits = 14.61f;

    // Sample fractions spread across each clip; deterministic set for the double capture.
    private static readonly float[] FrameFractions = [0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f];

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        var aoe3Root = Environment.GetEnvironmentVariable("AOE3DE_PATH");
        if (string.IsNullOrEmpty(aoe3Root))
        {
            reporter.Error("Missing AOE3DE_PATH pointing at an Age of Empires III: DE install (source models).");
            return 1;
        }

        var gameDirectory = Path.Combine(aoe3Root, "Game");
        if (!Directory.Exists(gameDirectory))
        {
            reporter.Error($"Missing AoE3 DE game directory at '{gameDirectory}'.");
            return 1;
        }

        var outputDirectory = Path.Combine("artifacts", "proofs", "aoe3-granny-proof");
        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        using var barFileSystem = new BarFileSystem(gameDirectory);
        var importContext = new AssetImportContext(barFileSystem, language: null);
        var importer = new GrannyModelImporter();
        var animationImporter = new GrannyAnimationImporter();
        var resolver = new Aoe3AnimfileResolver(importContext);

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(gameLoadContext.FileSystem, barFileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        var ranger = game.AssetStore.Models.GetByName(RangerModelName);
        if (ranger is null)
        {
            reporter.Error($"Generals Ranger model '{RangerModelName}' not found in the asset store.");
            return 3;
        }

        var unitsPerMeter = RangerVisibleHeightUnits / ReferenceHumanHeightMeters;
        reporter.Info(
            $"Ranger visible height {RangerVisibleHeightUnits.ToString("0.0#", CultureInfo.InvariantCulture)} units; " +
            $"{unitsPerMeter.ToString("0.0##", CultureInfo.InvariantCulture)} units per meter " +
            $"(assumes a {ReferenceHumanHeightMeters.ToString("0.0", CultureInfo.InvariantCulture)} m human).");

        var musketeerVisual = resolver.Resolve(MusketeerUnit, ["guardmusketeers"]);
        reporter.Info(
            $"{MusketeerUnit} (guardmusketeers) resolved to '{musketeerVisual.ModelPath}' " +
            $"with {musketeerVisual.ClipsByAnimName.Count} clips.");
        var hussarVisual = resolver.Resolve(HussarUnit, ["guardhussars"]);
        reporter.Info(
            $"{HussarUnit} (guardhussars) resolved to '{hussarVisual.ModelPath}' " +
            $"with {hussarVisual.ClipsByAnimName.Count} clips.");

        var captures = new List<(string FileName, string Check)>();

        var (musketeerModel, musketeerImported) = CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            importContext,
            importer,
            reporter,
            outputDirectory,
            musketeerVisual.ModelPath,
            "musketeer-beside-ranger.png",
            "the musketeer stands upright beside the Ranger at matched human scale; the default " +
            "matA textures are intentionally desaturated for player-color tinting (not implemented), " +
            "so a mottled grey uniform is the expected render");
        captures.Add(("musketeer-beside-ranger.png", "see above"));

        var redcoatImportContext = CreateRedcoatImportContext(barFileSystem, reporter);
        _ = CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            redcoatImportContext,
            new GrannyModelImporter(),
            reporter,
            outputDirectory,
            MusketeerKey,
            "redcoat-beside-ranger.png",
            "the same uncompressed musketeer mesh renders with its pre-colored red coat " +
            "textures; the variant .gr2 models (redcoats, blue_guard, ...) are internally " +
            "Oodle1-compressed until C10d, so the redcoats_age4 material file is overlaid " +
            "on the musketeer_4age material key through a zip file system");
        captures.Add(("redcoat-beside-ranger.png", "see above"));

        var (horseModel, horseImported) = CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            importContext,
            importer,
            reporter,
            outputDirectory,
            hussarVisual.ModelPath,
            "horse-beside-ranger.png",
            "the hussar horse stands on all fours beside the Ranger, larger than a human, " +
            "with both material groups (mata, matb default_cutout) rendered textured");
        captures.Add(("horse-beside-ranger.png", "see above"));

        var falconetFailed = false;
        try
        {
            _ = importer.ImportModel(FalconetKey, importContext);
        }
        catch (InvalidDataException exception)
        {
            falconetFailed = true;
            var message = exception.Message;
            if (message.Contains(FalconetKey, StringComparison.Ordinal) &&
                message.Contains("section 0", StringComparison.Ordinal) &&
                message.Contains("Oodle1", StringComparison.Ordinal))
            {
                reporter.Info($"Falconet rejected as expected: {message}");
            }
            else
            {
                reporter.Error($"Falconet failure message lacks the expected fields: {message}");
                return 5;
            }
        }

        if (!falconetFailed)
        {
            reporter.Error($"Falconet '{FalconetKey}' imported without failing despite compressed sections.");
            return 5;
        }

        CaptureDebugViews(game, loadContext, importContext, redcoatImportContext, importer, reporter, outputDirectory);

        var animatedPlans = new List<(string DisplayName, string AnimName, Model Model, ImportedModel Imported)>
        {
            ("musketeer-march", "Volley_walk", musketeerModel, musketeerImported),
            ("musketeer-volley", "Volley_standing_attack", musketeerModel, musketeerImported),
            ("horse-idle", "Idle", horseModel, horseImported),
        };

        var animatedSummaries = new List<(string DisplayName, string Summary)>();
        foreach (var (displayName, animName, model, importedModel) in animatedPlans)
        {
            var clipKey = (displayName.StartsWith("horse", StringComparison.Ordinal) ? hussarVisual : musketeerVisual)
                .ClipsByAnimName[animName];
            var (ok, summary) = CaptureAnimatedClip(
                game,
                model,
                importedModel,
                displayName,
                animName,
                clipKey,
                unitsPerMeter,
                loadContext,
                importContext,
                animationImporter,
                reporter,
                outputDirectory);
            animatedSummaries.Add((displayName, summary));
            if (!ok)
            {
                return 7;
            }
        }

        WriteValidationGuide(outputDirectory, captures, animatedSummaries, unitsPerMeter);
        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    /// <summary>
    /// Builds an import context whose musketeer_4age material key resolves to the
    /// redcoats_age4 material file (pre-colored textures), read out of the BAR and
    /// mounted through a zip file system ahead of the archives.
    /// </summary>
    private static AssetImportContext CreateRedcoatImportContext(
        BarFileSystem barFileSystem, ITestHarnessReporter reporter)
    {
        var materialEntry = barFileSystem.GetFile(RedcoatMaterialKey)
            ?? throw new InvalidDataException($"Missing '{RedcoatMaterialKey}' in the AoE3 archives.");
        byte[] materialBytes;
        using (var stream = materialEntry.Open())
        {
            using var buffered = new MemoryStream();
            stream.CopyTo(buffered);
            materialBytes = buffered.ToArray();
        }

        var zipPath = Path.Combine(Path.GetTempPath(), "aoe3-granny-proof-redcoat-material.zip");
        using (var archiveStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        {
            using var archive = new System.IO.Compression.ZipArchive(archiveStream, System.IO.Compression.ZipArchiveMode.Create);
            var entry = archive.CreateEntry(MusketeerMaterialZipEntry);
            using var entryStream = entry.Open();
            entryStream.Write(materialBytes, 0, materialBytes.Length);
        }

        reporter.Info($"Redcoat material overlay written to {zipPath}.");
        var zipFileSystem = new ZipFileSystem(zipPath);
        return new AssetImportContext(new CompositeFileSystem(zipFileSystem, barFileSystem), language: null);
    }

    /// <summary>
    /// Captures diagnostic material views of the musketeer at 1024 px: unlit albedo (texture
    /// detail without lighting), a UV debug pattern (island layout, orientation, mirroring),
    /// normal-only shading (verifies the tangent frame), and the Masks texture displayed as
    /// colour (channel structure). Development evidence for texture-pipeline verification.
    /// </summary>
    private static void CaptureDebugViews(
        Game game,
        AssetLoadContext loadContext,
        AssetImportContext importContext,
        AssetImportContext redcoatImportContext,
        GrannyModelImporter importer,
        ITestHarnessReporter reporter,
        string outputDirectory)
    {
        var debugDirectory = Path.Combine(outputDirectory, "debug");
        Directory.CreateDirectory(debugDirectory);

        var imported = importer.ImportModel(MusketeerKey, importContext)
            ?? throw new InvalidOperationException($"AoE3 import returned null for '{MusketeerKey}'.");

        var size = new Size(1024, 1024);
        var background = new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f);
        var views = new (string Suffix, float Yaw)[]
        {
            ("front", 0f),
            ("right", MathF.PI / 2f),
            ("back", MathF.PI),
        };

        var albedoModel = ImportedModelMaterializer.MaterializeModel(imported, loadContext);
        var unlit = new StandaloneCaptureOptions(Background: background, Lighting: CreateUnlitLighting());
        foreach (var (suffix, yaw) in views)
        {
            StandaloneModelCapture.Capture(
                game,
                albedoModel,
                loadContext,
                Path.Combine(Path.GetTempPath(), $"aoe3-granny-proof-warmup-albedo-{suffix}.png"),
                size,
                worldMatrix: Matrix4x4.CreateRotationZ(yaw),
                options: unlit);
            StandaloneModelCapture.Capture(
                game,
                albedoModel,
                loadContext,
                Path.Combine(debugDirectory, $"albedo-{suffix}.png"),
                size,
                worldMatrix: Matrix4x4.CreateRotationZ(yaw),
                options: unlit);
        }

        reporter.Info("Debug albedo views captured (full ambient, no directional light): coat, face, and chevrons should read like the decoded atlas.");

        var redcoatImported = importer.ImportModel(MusketeerKey, redcoatImportContext)
            ?? throw new InvalidOperationException($"AoE3 import returned null for '{MusketeerKey}' (redcoat overlay).");
        var redcoatAlbedoModel = ImportedModelMaterializer.MaterializeModel(redcoatImported, loadContext);
        StandaloneModelCapture.Capture(
            game,
            redcoatAlbedoModel,
            loadContext,
            Path.Combine(Path.GetTempPath(), "aoe3-granny-proof-warmup-albedo-redcoat.png"),
            size,
            options: unlit);
        StandaloneModelCapture.Capture(
            game,
            redcoatAlbedoModel,
            loadContext,
            Path.Combine(debugDirectory, "albedo-redcoat.png"),
            size,
            options: unlit);
        reporter.Info("Debug redcoat albedo view captured (unlit): the overlaid redcoats_age4 material should read as a red coat.");

        var debugPattern = EncodeUvDebugPng();
        var checkerModel = ImportedModelMaterializer.MaterializeModel(
            CloneWithMaterials(imported, payload => new ImportedPbrMetallicRoughness(
                baseColorTexture: DebugBinding(payload, debugPattern, ImportedTextureRole.BaseColor),
                metallicFactor: 0f,
                roughnessFactor: 1f,
                alphaMode: payload.AlphaMode,
                alphaCutoff: payload.AlphaCutoff,
                doubleSided: payload.DoubleSided)),
            loadContext);
        StandaloneModelCapture.Capture(
            game,
            checkerModel,
            loadContext,
            Path.Combine(debugDirectory, "uv-pattern-front.png"),
            size,
            options: new StandaloneCaptureOptions(Background: background));
        reporter.Info("Debug UV pattern view captured: checker reads texture-space orientation, gradients expose stretching and mirrored islands.");

        var normalModel = ImportedModelMaterializer.MaterializeModel(
            CloneWithMaterials(imported, payload => new ImportedPbrMetallicRoughness(
                normalTexture: payload.NormalTexture,
                normalScale: payload.NormalScale,
                metallicFactor: 0f,
                roughnessFactor: 1f,
                alphaMode: payload.AlphaMode,
                alphaCutoff: payload.AlphaCutoff,
                doubleSided: payload.DoubleSided)),
            loadContext);
        StandaloneModelCapture.Capture(
            game,
            normalModel,
            loadContext,
            Path.Combine(debugDirectory, "normal-front.png"),
            size,
            options: new StandaloneCaptureOptions(Background: background));
        reporter.Info("Debug normal-only view captured (white albedo, default lighting): smooth cloth shading verifies the tangent frame.");

        var masksModel = ImportedModelMaterializer.MaterializeModel(
            CloneWithMaterials(imported, payload => new ImportedPbrMetallicRoughness(
                baseColorTexture: payload.MetallicRoughnessTexture is { } masks
                    ? new ImportedTextureBinding(masks.ImageSource, masks.UvSet, masks.Sampler, ImportedTextureRole.BaseColor)
                    : null,
                metallicFactor: 0f,
                roughnessFactor: 1f,
                alphaMode: payload.AlphaMode,
                alphaCutoff: payload.AlphaCutoff,
                doubleSided: payload.DoubleSided)),
            loadContext);
        StandaloneModelCapture.Capture(
            game,
            masksModel,
            loadContext,
            Path.Combine(debugDirectory, "masks-front.png"),
            size,
            options: new StandaloneCaptureOptions(Background: background));
        reporter.Info("Debug Masks view captured (displayed as colour; BaseColor uploads sRGB-decode the data texture, so hue shifts but channel structure is readable).");
    }

    /// <summary>
    /// Full ambient and no directional light: the PBR ambient term carries the albedo
    /// unmodified, so captures show the raw BaseColor texture.
    /// </summary>
    private static WorldLighting CreateUnlitLighting()
    {
        var lights = new GlobalShaderResources.LightingConfiguration
        {
            Light0 = new GlobalShaderResources.Light
            {
                Ambient = Vector3.One,
                Direction = Vector3.UnitZ,
                Color = Vector3.Zero,
            },
        };

        return new WorldLighting(
            new Dictionary<TimeOfDay, LightSettings> { { TimeOfDay.Morning, new LightSettings(lights, lights) } },
            TimeOfDay.Morning);
    }

    private static ImportedTextureBinding DebugBinding(
        ImportedPbrMetallicRoughness payload,
        byte[] pngBytes,
        ImportedTextureRole role)
    {
        return new ImportedTextureBinding(
            ImportedImageSource.CreateEmbeddedBytes(pngBytes, "image/png", $"debug-{role}"),
            payload.BaseColorTexture?.UvSet ?? 0,
            new ImportedSampler(),
            role);
    }

    private static ImportedModel CloneWithMaterials(
        ImportedModel model,
        Func<ImportedPbrMetallicRoughness, ImportedPbrMetallicRoughness> transform)
    {
        var subObjects = new List<ImportedSubObject>();
        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                subObjects.Add(subObject);
                continue;
            }

            var materials = mesh.Materials
                .Select(material => material.PbrMetallicRoughness is { } payload
                    ? new ImportedMaterial(
                        material.ShaderFamily,
                        material.CullMode,
                        material.DepthWriteEnabled,
                        material.DepthComparison,
                        material.BlendEnabled,
                        material.SourceBlendFactor,
                        material.DestinationColorBlendFactor,
                        material.DestinationAlphaBlendFactor,
                        fixedFunctionShading: null,
                        properties: material.Properties,
                        pbrMetallicRoughness: transform(payload))
                    : material)
                .ToArray();
            var clonedMesh = new ImportedMesh(
                mesh.Vertices,
                mesh.Indices,
                mesh.BoundingBox,
                mesh.BoundingSphere,
                mesh.Hidden,
                mesh.CameraOriented,
                mesh.HasHouseColor,
                mesh.IsSkinned,
                materials,
                mesh.MeshParts,
                mesh.PrimitiveId,
                mesh.MorphTargetSets);
            subObjects.Add(new ImportedSubObject(subObject.FullName, subObject.DisplayName, subObject.ParentBoneIndex, clonedMesh));
        }

        return new ImportedModel(
            model.Name,
            model.Skeleton,
            subObjects,
            ImportedSkeletonOrigin.Embedded,
            [.. model.EnabledFeatureGates]);
    }

    /// <summary>
    /// 256x256 UV debug pattern: 32 px two-tone checker in red (texture-space grid), a
    /// horizontal gradient in green (u), and a vertical gradient in blue (v).
    /// </summary>
    private static byte[] EncodeUvDebugPng()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(256, 256);
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                var checker = (x / 32 + y / 32) % 2 == 0 ? 40 : 220;
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32((byte)checker, (byte)x, (byte)y, 255);
            }
        }

        using var stream = new MemoryStream();
        image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return stream.ToArray();
    }

    private static (Model Model, ImportedModel Imported) CaptureBesideRanger(
        Game game,
        Model ranger,
        float unitsPerMeter,
        AssetLoadContext loadContext,
        AssetImportContext importContext,
        GrannyModelImporter importer,
        ITestHarnessReporter reporter,
        string outputDirectory,
        string modelKey,
        string outputFileName,
        string check)
    {
        var imported = importer.ImportModel(modelKey, importContext)
            ?? throw new InvalidOperationException($"AoE3 import returned null for '{modelKey}'.");

        foreach (var warning in importer.Diagnostics.Warnings)
        {
            reporter.Info($"Import warning for '{modelKey}': {warning}");
        }

        var model = ImportedModelMaterializer.MaterializeModel(imported, loadContext);

        var options = new StandaloneCaptureOptions(
            Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f));

        var outputPath = Path.Combine(outputDirectory, outputFileName);

        // First execution of a material pipeline variant after device creation is unstable
        // (see Ee2NifProof); warm the pair once before the real capture.
        StandaloneModelCapture.CaptureGroundedPair(
            game,
            model,
            unitsPerMeter,
            ranger,
            loadContext,
            Path.Combine(Path.GetTempPath(), $"aoe3-granny-proof-warmup-{outputFileName}"),
            new Size(CaptureSize, CaptureSize),
            options);

        StandaloneModelCapture.CaptureGroundedPair(
            game,
            model,
            unitsPerMeter,
            ranger,
            loadContext,
            outputPath,
            new Size(CaptureSize, CaptureSize),
            options);

        reporter.Info($"Captured {outputFileName}");
        return (model, imported);
    }

    private static (bool Ok, string Summary) CaptureAnimatedClip(
        Game game,
        Model model,
        ImportedModel importedModel,
        string displayName,
        string animName,
        string clipKey,
        float unitsPerMeter,
        AssetLoadContext loadContext,
        AssetImportContext importContext,
        GrannyAnimationImporter animationImporter,
        ITestHarnessReporter reporter,
        string outputDirectory)
    {
        // Diagnostics accumulate on the importer across calls; report only this call's
        // entries so each clip's log section shows its own warnings and unit-scale notes.
        var warningCount = animationImporter.Diagnostics.Warnings.Count;
        var noteCount = animationImporter.Diagnostics.Notes.Count;
        var imported = animationImporter.ImportAnimation(clipKey, importContext, importedModel.Skeleton);
        if (imported is null)
        {
            reporter.Error($"Animation import returned null for '{clipKey}'.");
            return (false, $"import failed for '{clipKey}'");
        }

        foreach (var warning in animationImporter.Diagnostics.Warnings.Skip(warningCount))
        {
            reporter.Info($"Import warning for '{clipKey}': {warning}");
        }

        foreach (var note in animationImporter.Diagnostics.Notes.Skip(noteCount))
        {
            reporter.Info($"Import note for '{clipKey}': {note}");
        }

        // Shared animation-library clips carry tracks for bones beyond a given model's
        // skeleton (Ponytails, launchpoints, Toe0s); named-target binding rejects those,
        // so channels outside the model skeleton are dropped and reported, by design.
        var modelBoneNames = new HashSet<string>(
            model.BoneHierarchy.Bones.Select(bone => bone.Name),
            StringComparer.OrdinalIgnoreCase);
        var boundChannels = imported.Channels
            .Where(channel => channel.Target.Kind == ImportedAnimationTargetKind.BoneName
                && modelBoneNames.Contains(channel.Target.BoneName))
            .ToList();
        var extraChannels = imported.Channels.Count - boundChannels.Count;
        var extraBones = imported.Channels
            .Where(channel => channel.Target.Kind == ImportedAnimationTargetKind.BoneName
                && !modelBoneNames.Contains(channel.Target.BoneName))
            .Select(channel => channel.Target.BoneName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        reporter.Info(
            $"Clip '{displayName}' ({animName}, '{clipKey}'): {boundChannels.Count} of {imported.Channels.Count} " +
            $"channels bind to the skeleton; {extraChannels} extra-track channels dropped " +
            $"({string.Join(", ", extraBones)}).");

        var filtered = new ImportedAnimation(imported.Name, imported.Duration, boundChannels);
        var animation = ImportedAnimationMaterializer.Materialize(filtered);

        var framesDirectory = Path.Combine(outputDirectory, "frames", displayName);
        Directory.CreateDirectory(framesDirectory);

        var world = Matrix4x4.CreateScale(unitsPerMeter);
        var durationSeconds = (float)imported.Duration.TotalSeconds;
        var sampleSeconds = FrameFractions.Select(fraction => durationSeconds * fraction).ToArray();

        // The default camera fit sees the rest pose; animated poses (and march root motion)
        // leave those bounds. Fit instead to the union of CPU-skinned deformed bounds at the
        // exact frame times captured below, mirroring the GPU skinning formula.
        var fitBounds = ComputeAnimatedFitBounds(model, importedModel, animation, sampleSeconds, world, loadContext);
        var options = new StandaloneCaptureOptions(
            Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f),
            CameraFitBounds: fitBounds);

        StandaloneModelCapture.Capture(
            game,
            model,
            loadContext,
            Path.Combine(Path.GetTempPath(), $"aoe3-granny-proof-warmup-{displayName}.png"),
            new Size(CaptureSize, CaptureSize),
            configureInstance: instance => PlayToTime(instance, animation, durationSeconds * 0.5f),
            worldMatrix: world,
            options: options);

        for (var i = 0; i < FrameFractions.Length; i++)
        {
            var seconds = durationSeconds * FrameFractions[i];
            var framePath = Path.Combine(framesDirectory, $"frame-{i:00}.png");
            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                framePath,
                new Size(CaptureSize, CaptureSize),
                configureInstance: instance => PlayToTime(instance, animation, seconds),
                worldMatrix: world,
                options: options);

            // Determinism: every frame is captured twice and must be byte-identical.
            var repeatPath = Path.Combine(Path.GetTempPath(), $"aoe3-granny-proof-repeat-{displayName}-{i:00}.png");
            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                repeatPath,
                new Size(CaptureSize, CaptureSize),
                configureInstance: instance => PlayToTime(instance, animation, seconds),
                worldMatrix: world,
                options: options);
            var identical = File.ReadAllBytes(framePath).SequenceEqual(File.ReadAllBytes(repeatPath));
            File.Delete(repeatPath);
            if (!identical)
            {
                reporter.Error($"Frame {i} of '{displayName}' is not byte-identical across two captures.");
                return (false, $"nondeterministic frame {i}");
            }
        }

        reporter.Info($"Captured {FrameFractions.Length} deterministic frames for '{displayName}' under {framesDirectory}.");
        return (true,
            $"{boundChannels.Count} channels bound, {extraChannels} extra tracks dropped, " +
            $"{FrameFractions.Length} frames captured twice byte-identical");
    }

    /// <summary>
    /// Union of world-space vertex bounds across the sampled clip times, computed by
    /// CPU skinning with the same inverse-bind-times-relative matrices the GPU upload
    /// uses. Rigid sub-objects (positions baked into parent-bone space) transform by
    /// the animated parent bone alone; skinned sub-objects blend their influences and
    /// then take the instance world matrix.
    /// </summary>
    private static AxisAlignedBoundingBox ComputeAnimatedFitBounds(
        Model model,
        ImportedModel importedModel,
        ModelAnimation animation,
        float[] sampleSeconds,
        Matrix4x4 world,
        AssetLoadContext loadContext)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var bones = model.BoneHierarchy.Bones;

        foreach (var seconds in sampleSeconds)
        {
            using var instance = model.CreateInstance(loadContext);
            PlayToTime(instance, animation, seconds);
            instance.Update(TimeInterval.Zero);

            var relative = instance.RelativeBoneTransforms;
            for (var i = 0; i < model.SubObjects.Length && i < importedModel.SubObjects.Count; i++)
            {
                if (importedModel.SubObjects[i].RenderObject is not ImportedMesh mesh)
                {
                    continue;
                }

                var boneIndex = model.SubObjects[i].Bone.Index;
                if (mesh.IsSkinned)
                {
                    foreach (var vertex in mesh.Vertices)
                    {
                        var deformed =
                            Vector3.Transform(vertex.Position, bones[vertex.BoneIndex0].InverseBindMatrix * relative[vertex.BoneIndex0]) * vertex.BoneWeight0
                            + Vector3.Transform(vertex.Position, bones[vertex.BoneIndex1].InverseBindMatrix * relative[vertex.BoneIndex1]) * vertex.BoneWeight1
                            + Vector3.Transform(vertex.Position, bones[vertex.BoneIndex2].InverseBindMatrix * relative[vertex.BoneIndex2]) * vertex.BoneWeight2
                            + Vector3.Transform(vertex.Position, bones[vertex.BoneIndex3].InverseBindMatrix * relative[vertex.BoneIndex3]) * vertex.BoneWeight3;
                        var position = Vector3.Transform(deformed, world);
                        min = Vector3.Min(min, position);
                        max = Vector3.Max(max, position);
                    }
                }
                else
                {
                    var rigid = relative[boneIndex] * world;
                    foreach (var vertex in mesh.Vertices)
                    {
                        var position = Vector3.Transform(vertex.Position, rigid);
                        min = Vector3.Min(min, position);
                        max = Vector3.Max(max, position);
                    }
                }
            }
        }

        return new AxisAlignedBoundingBox(min, max);
    }

    private static void PlayToTime(ModelInstance instance, ModelAnimation animation, float seconds)
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

    private static void WriteValidationGuide(
        string outputDirectory,
        List<(string FileName, string Check)> captures,
        List<(string DisplayName, string Summary)> animatedSummaries,
        float unitsPerMeter)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# AoE3 Granny proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture comes from BarFileSystem(AoE3DE_PATH/Game) ->");
        writer.WriteLine("Aoe3AnimfileResolver -> GrannyModelImporter -> .material.xmb mapping onto C3");
        writer.WriteLine("PBR with DDT texture bindings -> ImportedModelMaterializer -> runtime Model,");
        writer.WriteLine("rendered beside a native Generals Ranger (AIRNGR_SKN) through CapturePair.");
        writer.WriteLine($"Scale calibration: {unitsPerMeter.ToString("0.0##", CultureInfo.InvariantCulture)} Generals units");
        writer.WriteLine("per meter, from the Ranger visible mesh height (14.61 units, pixel-verified; the");
        writer.WriteLine($"rest-pose skeleton span undercounts) divided by an assumed {ReferenceHumanHeightMeters.ToString("0.0", CultureInfo.InvariantCulture)} m human.");
        writer.WriteLine("The default matA musketeer and horse textures are intentionally desaturated for");
        writer.WriteLine("player-color tinting in the DE engine; Panopticon does not tint, so the grey render");
        writer.WriteLine("is expected, and the redcoat capture (churchthinredline tech) shows pre-colored");
        writer.WriteLine("textures through the same pipeline. The falconet import must fail naming the");
        writer.WriteLine("file, section 0, and Oodle1; decompression is C10d. No AoE3 asset is copied or");
        writer.WriteLine("committed.");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
        }

        writer.WriteLine();
        foreach (var (displayName, summary) in animatedSummaries)
        {
            writer.WriteLine($"- `frames/{displayName}/frame-00.png` .. `frame-05.png` ({summary})");
        }
    }
}
