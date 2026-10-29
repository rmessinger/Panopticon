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
using Panopticon.Graphics;
using Panopticon.Graphics.Animation;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
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

    // Fixed X separation between the Ranger (at the origin) and each AoE3 model; wide
    // enough that both silhouettes stay separate in the fitted camera frame.
    private const float SideOffsetUnits = 30f;

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

        var musketeerModel = CaptureBesideRanger(
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

        _ = CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            CreateRedcoatImportContext(barFileSystem, reporter),
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

        var horseModel = CaptureBesideRanger(
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

        var animatedPlans = new List<(string DisplayName, string AnimName, Model Model)>
        {
            ("musketeer-march", "Volley_walk", musketeerModel),
            ("musketeer-volley", "Volley_standing_attack", musketeerModel),
            ("horse-idle", "Idle", horseModel),
        };

        var animatedSummaries = new List<(string DisplayName, string Summary)>();
        foreach (var (displayName, animName, model) in animatedPlans)
        {
            var clipKey = (displayName.StartsWith("horse", StringComparison.Ordinal) ? hussarVisual : musketeerVisual)
                .ClipsByAnimName[animName];
            var (ok, summary) = CaptureAnimatedClip(
                game,
                model,
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

    private static Model CaptureBesideRanger(
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

        var modelBounds = StandaloneModelCapture.ComputeEnclosingBoundingBox(model);

        // Both models stand on the world ground plane; the AoE3 model is scaled from
        // meters into Generals units and offset to a fixed spot beside the Ranger.
        var scale = unitsPerMeter;
        var offset = new Vector3(SideOffsetUnits, 0f, -modelBounds.Min.Z * scale);
        var rangerWorld = Matrix4x4.Identity;
        var modelWorld = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(offset);
        var options = new StandaloneCaptureOptions(
            Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f));

        var outputPath = Path.Combine(outputDirectory, outputFileName);

        // First execution of a material pipeline variant after device creation is unstable
        // (see Ee2NifProof); warm the pair once before the real capture.
        StandaloneModelCapture.CapturePair(
            game,
            model,
            modelWorld,
            ranger,
            rangerWorld,
            loadContext,
            Path.Combine(Path.GetTempPath(), $"aoe3-granny-proof-warmup-{outputFileName}"),
            new Size(CaptureSize, CaptureSize),
            options);

        StandaloneModelCapture.CapturePair(
            game,
            model,
            modelWorld,
            ranger,
            rangerWorld,
            loadContext,
            outputPath,
            new Size(CaptureSize, CaptureSize),
            options);

        reporter.Info($"Captured {outputFileName}");
        return model;
    }

    private static (bool Ok, string Summary) CaptureAnimatedClip(
        Game game,
        Model model,
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
        var imported = animationImporter.ImportAnimation(clipKey, importContext);
        if (imported is null)
        {
            reporter.Error($"Animation import returned null for '{clipKey}'.");
            return (false, $"import failed for '{clipKey}'");
        }

        foreach (var warning in animationImporter.Diagnostics.Warnings)
        {
            reporter.Info($"Import warning for '{clipKey}': {warning}");
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
        var options = new StandaloneCaptureOptions(
            Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f));
        var durationSeconds = (float)imported.Duration.TotalSeconds;

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
