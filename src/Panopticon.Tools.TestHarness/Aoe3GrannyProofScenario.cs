using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Granny;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Mathematics;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Headless C10b proof: mounts the operator-supplied AoE3 DE Game/ tree through
/// BarFileSystem, imports the musketeer and hussar horse through GrannyModelImporter
/// (with AoE3 material mapping onto C3 PBR), materializes them through
/// ImportedModelMaterializer, and captures each beside a native Generals Ranger
/// (AIRNGR_SKN) in one frame through StandaloneModelCapture.CapturePair. The falconet
/// has an Oodle1-compressed section and must fail with an explicit message naming the
/// file, section index, and compression type. No AoE3 asset is copied or committed.
/// </summary>
public static class Aoe3GrannyProofScenario
{
    private const int CaptureSize = 512;

    private const string MusketeerKey = @"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2";

    private const string HorseKey = @"Art\units\cavalry\light_cavalry\light_cavalry_age4_horse.gr2";

    private const string FalconetKey = @"Art\units\artillery\falconet\falconet.gr2";

    private const string RangerModelName = "AIRNGR_SKN";

    // Generals renders in game units, not meters; the Ranger calibrates the scene.
    // A Generals infantry model is assumed to depict a 1.8 m human.
    private const float ReferenceHumanHeightMeters = 1.8f;

    // Fixed X separation between the Ranger (at the origin) and each AoE3 model; wide
    // enough that both silhouettes stay separate in the fitted camera frame.
    private const float SideOffsetUnits = 30f;

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

        // W3D header bounding boxes of skinned meshes are unreliable (inverted Z ranges on
        // this very model), so the enclosing mesh bounds overstate the Ranger by several
        // units. The rest-pose skeleton span is the honest height estimate.
        var rangerHeightUnits = ComputeSkeletonHeight(ranger);
        var unitsPerMeter = rangerHeightUnits / ReferenceHumanHeightMeters;
        reporter.Info(
            $"Ranger skeleton height {rangerHeightUnits.ToString("0.0##", CultureInfo.InvariantCulture)} units; " +
            $"{unitsPerMeter.ToString("0.0##", CultureInfo.InvariantCulture)} units per meter " +
            $"(assumes a {ReferenceHumanHeightMeters.ToString("0.0", CultureInfo.InvariantCulture)} m human).");

        var captures = new List<(string FileName, string Check)>();

        CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            importContext,
            importer,
            reporter,
            outputDirectory,
            MusketeerKey,
            "musketeer-beside-ranger.png",
            "the musketeer stands upright beside the Ranger at proportional human scale, " +
            "with the textured uniform (base color, normals, metallic bayonet) resolved from DDT bindings");
        captures.Add(("musketeer-beside-ranger.png", "see above"));

        CaptureBesideRanger(
            game,
            ranger,
            unitsPerMeter,
            loadContext,
            importContext,
            importer,
            reporter,
            outputDirectory,
            HorseKey,
            "horse-beside-ranger.png",
            "the hussar horse stands upright on all fours beside the Ranger, larger than a human, " +
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

        WriteValidationGuide(outputDirectory, captures, unitsPerMeter);
        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    private static void CaptureBesideRanger(
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
    }

    /// <summary>
    /// Rest-pose skeleton height: the Z span of the accumulated bone worlds. Skinned W3D
    /// mesh bounding boxes can carry exporter junk (inverted axes), but bone transforms
    /// come from the hierarchy chunk and describe the actual pose.
    /// </summary>
    private static float ComputeSkeletonHeight(Model model)
    {
        var bones = model.BoneHierarchy.Bones;
        if (bones.Length == 0)
        {
            throw new InvalidDataException($"Model '{model.Name}' has no bones to measure.");
        }

        var boneWorlds = new Matrix4x4[bones.Length];
        var minZ = float.MaxValue;
        var maxZ = float.MinValue;
        for (var i = 0; i < bones.Length; i++)
        {
            boneWorlds[i] = bones[i].Parent is null
                ? bones[i].Transform.Matrix
                : bones[i].Transform.Matrix * boneWorlds[bones[i].Parent.Index];
            minZ = MathF.Min(minZ, boneWorlds[i].Translation.Z);
            maxZ = MathF.Max(maxZ, boneWorlds[i].Translation.Z);
        }

        return maxZ - minZ;
    }

    private static void WriteValidationGuide(
        string outputDirectory,
        List<(string FileName, string Check)> captures,
        float unitsPerMeter)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine("# AoE3 Granny proof - validation guide");
        writer.WriteLine();
        writer.WriteLine("Every capture comes from BarFileSystem(AoE3DE_PATH/Game) ->");
        writer.WriteLine("GrannyModelImporter -> .material.xmb mapping onto C3 PBR with DDT texture");
        writer.WriteLine("bindings -> ImportedModelMaterializer -> runtime Model, rendered beside a");
        writer.WriteLine($"native Generals Ranger (AIRNGR_SKN) through CapturePair. Scale calibration: {unitsPerMeter.ToString("0.0##", CultureInfo.InvariantCulture)}");
        writer.WriteLine("Generals units per meter, from the Ranger rest-pose skeleton height divided");
        writer.WriteLine($"by an assumed {ReferenceHumanHeightMeters.ToString("0.0", CultureInfo.InvariantCulture)} m human (W3D skinned-mesh header bounds carry");
        writer.WriteLine("exporter junk and are not used). The falconet import must fail naming the file,");
        writer.WriteLine("section 0, and Oodle1; decompression is C10d. No AoE3 asset is copied or");
        writer.WriteLine("committed. Deterministic repeat: a second run must produce the same frames.");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
        }
    }
}
