using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Panopticon.Content;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Preview;
using Panopticon.Graphics;
using Panopticon.Input;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Scripting;
using Panopticon.Tests.Data;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// Completed-freeze operator proof (scope: Sedan operator proof on the completed final freeze).
/// ONE Core Run comes first. Its journal persists outside the owned stage on both outcomes. A failed
/// Core Run stops the test: no CLI step, no retry, no alternate activation, no tolerance bypass.
/// Budget A is the operator's 1.0 m engineering acceptance limit against frozen truth for all five
/// primaries. It is not a certified source accuracy. Carried unresolved channels: actual tile-specific
/// RGE accuracy UNKNOWN; 2024 auto-classification bias; 2012-2016 versus 2024 epoch mismatch.
/// Budget B is the independent 0.0390625 game-unit encoding guard, evaluated with the actual payload
/// verticalOriginMeters, metersPerGameUnit, verticalExaggeration, and crop minimum.
/// Frame crosscheck: 0.005 game units (1.524 mm at 0.3048 m per game unit), a predeclared frame-consistency
/// guard only; never a Budget A or Budget B allowance.
/// Chronology: freeze hashes prove integrity only. Before-DEM ordering rests on workflow code order
/// plus the promotion and first-DEM-open command ledger.
/// Preparation-ledger contract bound: preparation/receipt.json and preparation/commands.jsonl are
/// validated at load. Prepared inputs sedan-ign-proof.json and preview-textures.json are the root contract.
/// </summary>
[Collection("SerialGameTests")]
[Trait("Category", "Interactive")]
public sealed class SedanOperatorProofTests
{
    private const string ProofRootVariable = "PANOPTICON_GEO_PROOF_DIR";
    private const string PreviewShotVariable = "PANOPTICON_GEO_PREVIEW_SHOT";
    private const string CommitVariable = "PANOPTICON_GEO_PROOF_COMMIT";
    private const string RefusalMessage = "PANOPTICON_GEO_PROOF_DIR is unset or does not exist: the Sedan operator proof requires the prepared proof root (prepared manifest plus completed final freeze); refusing to continue before any DEM or Game access.";
    private const string MapName = "sedanoperatorproof";
    private const string Scope = "Sedan operator proof (completed final freeze)";
    private const int FrameCount = 300;
    private const int ExpectedImageryPoints = 2;
    private const int ChannelsPerImageryPoint = 3;
    private const float CameraHeight = 560f;
    // Predeclared before real data; not adjusted after any real-data run. Composition: 0.00328 game units
    // projection contract, 0.00033 game units WGS84/float32 allowance (float32 ulp at 3850 game units is
    // 2^-12, about 0.074 mm), and 1e-6 game units TSV print quantization.
    private const double CrossCheckToleranceGameUnits = 0.005d;

    private static readonly string[] Channels = { "red", "green", "blue" };

    private static readonly SedanProofExpectedFreeze ExpectedFreeze = new(
        "ea3e5f2e806b836c144dc33ffe6804dafcbad036abf240ffe7967aed589ea2f1",
        new[] { "r29c117", "r291c342", "r32c280", "r8c320", "r266c285" },
        new[] { "r122c275", "r69c19" });

    [GameFact(SageGame.CncGenerals)]
    public void SedanOperatorProof_CompletedFreeze_CoreFirst_CliPlan_NativeGame_Budgets()
    {
        var proofRootSetting = Environment.GetEnvironmentVariable(ProofRootVariable);

        if (string.IsNullOrEmpty(proofRootSetting) || !Directory.Exists(proofRootSetting))
        {
            Assert.Fail(RefusalMessage);
        }

        var proofRoot = proofRootSetting!;

        using var loadout = SedanProofArtifacts.Load(proofRoot, ExpectedFreeze);
        // The F1 preparation receipt and chronology ledger validation completed before any raster or Game access.
        var texturesPath = loadout.PreviewTexturesPath;
        var proofRootBaseline = HashInventory(proofRoot);

        var sandbox = Path.Combine(Path.GetTempPath(), "opencode", "sedan-operator-proof", Guid.NewGuid().ToString("N"));
        var coreDir = Path.Combine(sandbox, "core", MapName);
        var cliDir = Path.Combine(sandbox, "cli", MapName);
        var userRoot = Path.Combine(sandbox, "user");
        var mapsDir = Path.Combine(userRoot, "Maps");
        var shotsDir = Path.Combine(sandbox, "shots");
        var mapPath = Path.Combine(mapsDir, MapName + ".map");

        Directory.CreateDirectory(Path.Combine(sandbox, "core"));
        Directory.CreateDirectory(Path.Combine(sandbox, "cli"));
        Directory.CreateDirectory(mapsDir);
        Directory.CreateDirectory(shotsDir);

        var journals = new SedanProofJournalWriter(Path.Combine(sandbox, "journals"));

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = "PreparationF1",
            Action = "SedanProofArtifacts.Load",
            Success = true,
            Note = "freezeVerifiedAt: " + loadout.FreezeVerifiedAt.ToString("O", CultureInfo.InvariantCulture)
                + "; firstDemRasterIoAt: " + loadout.FirstDemRasterIoAt.ToString("O", CultureInfo.InvariantCulture),
        });

        var core = GeospatialImporter.Run(loadout.PreparedManifestPath, coreDir);
        var coreChecks = core.Report?.ValidationChecks ?? Array.Empty<PayloadValidationCheck>();

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = "CoreRun",
            Action = "GeospatialImporter.Run",
            Success = core.Success,
            PayloadPath = core.PayloadPath,
            Checks = ToJournalChecks(coreChecks),
            Diagnostics = ToJournalDiagnostics(core.Diagnostics),
            Note = "ONE Core Run first; journals persist outside the owned stage on both outcomes",
        });

        if (!core.Success)
        {
            Assert.Fail(
                $"Core import failed; STOP before any CLI step, no retry. Sandbox kept at {sandbox}. "
                + $"Failed checks: {DescribeFailedChecks(coreChecks)}. "
                + $"Diagnostics: {SummarizeDiagnostics(core.Diagnostics)}");
        }

        Assert.NotNull(core.Report);
        var coreInventory = HashInventory(coreDir);

        RunCli(journals, "CliImport", new[] { "import", "--manifest", loadout.PreparedManifestPath, "--out", cliDir }, "published: ");
        AssertInventoryUnchanged(coreInventory, HashInventory(cliDir), "CLI import payload versus Core payload");

        RunCli(journals, "CliReport", new[] { "report", "--payload", cliDir }, "payloadSha256: ");

        RunCli(journals, "CliPreview", new[] { "preview", "--payload", cliDir, "--textures", texturesPath, "--out", mapsDir }, "preview: ", MapName + ".map");
        Assert.True(File.Exists(mapPath), $"Preview did not write the map: {mapPath}");
        var mapSha256 = Sha256File(mapPath);
        var cliInventory = HashInventory(cliDir);

        using var mapStream = File.OpenRead(mapPath);
        var mapFile = MapFile.FromStream(mapStream);
        Assert.True(mapFile.BlendTileData.Textures.Length > 0, $"CLI-published map has no blend textures: {mapPath}");

        var sourceAccuracies = CollectSourceAccuracies(loadout.PreparedManifest);

        foreach (var declared in sourceAccuracies)
        {
            Assert.True(
                declared <= 1.0d,
                $"declared source accuracy {declared} m exceeds the operator 1.0 m limit: escalate before any DEM comparison");
        }

        var diagnostics = new GeoDiagnosticBag();
        var readerOrNull = GeneratedMapPayloadReader.Open(cliDir, diagnostics);
        Assert.NotNull(readerOrNull);
        using var reader = readerOrNull!;

        var transform = reader.Metadata.Grid.Transform;
        var preview = BuildPreviewTransform(reader, texturesPath, mapPath, diagnostics);
        var samples = SamplePrimaries(loadout.PrimarySites, reader, diagnostics);
        var sampleFailures = DescribeSampleFailures(samples);

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = "PayloadSample",
            Action = "GeneratedMapPayloadReader.TrySampleHeight with frame crosscheck",
            Success = sampleFailures is null,
            PayloadPath = cliDir,
            Note = DescribeSamples(samples),
        });

        if (sampleFailures is not null)
        {
            Assert.Fail(sampleFailures);
        }

        var primaries = BuildPrimaryFacts(loadout.PrimarySites, samples, reader, preview);
        var budgetANotes = string.Join(", ", primaries.Select(primary => $"{primary.Id}={primary.ResidualAMeters:F4} m"));
        var budgetAPassed = primaries.All(primary => primary.ResidualAMeters <= SedanProofBudgets.EngineeringLimitMeters);

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = "BudgetA",
            Action = "BudgetAResidualMeters against frozen truth (1.0 m engineering limit)",
            Success = budgetAPassed,
            PayloadPath = cliDir,
            Note = budgetANotes,
        });

        if (!budgetAPassed)
        {
            Assert.Fail($"Budget A failed (1.0 m engineering limit, all five primaries): {budgetANotes}; no Game was constructed.");
        }

        var imageryChannelChecks = CheckImageryChannels(loadout.PreparedManifest, core.Report);

        var shotPath = Path.Combine(shotsDir, "sedan-native-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png");
        var native = RunNativeGame(userRoot, primaries, shotPath);

        Assert.Equal(GeneratedMapPreviewBuilder.HeightQuantumGameUnits, SedanProofBudgets.BudgetBLimitGameUnits);

        var budgetBGameUnits = new double[primaries.Count];

        for (var index = 0; index < primaries.Count; index++)
        {
            var engineHeight = native.EngineHeights[index];

            budgetBGameUnits[index] = engineHeight.HasValue
                ? SedanProofBudgets.BudgetBResidualGameUnits((double)engineHeight.Value, primaries[index].PayloadZ, preview.CropMinimumGameZ)
                : double.NaN;
        }

        var budgetBPassed = budgetBGameUnits.All(residual => residual <= SedanProofBudgets.BudgetBLimitGameUnits);

        AssertInventoryUnchanged(proofRootBaseline, HashInventory(proofRoot), "proof root after the run");
        AssertInventoryUnchanged(coreInventory, HashInventory(coreDir), "Core payload after the run");
        AssertInventoryUnchanged(cliInventory, HashInventory(cliDir), "CLI payload after the run");
        Assert.Equal(mapSha256, Sha256File(mapPath));

        var metrics = new ProofMetrics(
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope,
            Environment.GetEnvironmentVariable(CommitVariable),
            loadout.FrozenManifestCanonicalSha256,
            mapSha256,
            transform.MetersPerGameUnit,
            transform.VerticalOriginMeters,
            transform.VerticalExaggeration,
            transform.OriginEastingNorthing.X,
            transform.OriginEastingNorthing.Y,
            preview.CropMinimumGameZ,
            imageryChannelChecks,
            sourceAccuracies,
            primaries,
            budgetBGameUnits,
            native.StartFrame,
            native.FrameAfterRun,
            native.Luminance,
            native.ShotPath);

        var metricsJson = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(sandbox, "sedan-operator-proof.metrics.json"), metricsJson);

        var budgetBNotes = string.Join(", ", primaries.Select((primary, index) => $"{primary.Id}={budgetBGameUnits[index]} game units"));

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = "NativeGame",
            Action = $"Game.StartSkirmishOrMultiPlayerGame, Update x{FrameCount}, FrameCapturer.CaptureToPng",
            Success = budgetBPassed,
            PayloadPath = cliDir,
            Note = $"budgetB residuals: {budgetBNotes}; cropMinimumGameZ: {preview.CropMinimumGameZ}; "
                + $"sourceAccuracies: {DescribeSourceAccuracies(sourceAccuracies)}",
        });

        Assert.True(
            budgetBPassed,
            $"Budget B failed ({SedanProofBudgets.BudgetBLimitGameUnits} game units): {budgetBNotes}; crop minimum game Z {preview.CropMinimumGameZ}.");

        CopyEnvironmentShot(native.ShotPath, metricsJson);
    }

    private static PreviewTransform BuildPreviewTransform(
        GeneratedMapPayloadReader reader,
        string texturesPath,
        string cliMapPath,
        GeoDiagnosticBag diagnostics)
    {
        var catalog = new NamedTerrainTextureCatalog(TerrainTextureCatalog.LoadNames(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals)));
        var textureMap = PreviewTextureMap.FromJson(File.ReadAllText(texturesPath, Encoding.UTF8), catalog, diagnostics);
        Assert.NotNull(textureMap);

        var build = GeneratedMapPreviewBuilder.Build(reader, textureMap!, null);
        Assert.True(
            build.Success && build.Transform is not null && build.MapFile is not null,
            $"In-process preview transform unavailable: {SummarizeDiagnostics(build.Diagnostics)}");

        using var inProcessBytes = new MemoryStream();
        build.MapFile!.WriteTo(inProcessBytes);
        var inProcessSha256 = Sha256Bytes(inProcessBytes.ToArray());

        Assert.True(
            inProcessSha256 == Sha256File(cliMapPath),
            "in-process preview map differs from the CLI-published map: determinism deviation, escalate");

        return build.Transform!;
    }

    private static IReadOnlyList<SedanProofSiteSample> SamplePrimaries(
        IReadOnlyList<SedanProofSite> sites,
        GeneratedMapPayloadReader reader,
        GeoDiagnosticBag diagnostics)
    {
        var transform = reader.Metadata.Grid.Transform;

        return CollectSiteSamples(
            sites,
            site => (
                site.GameX - SedanProofBudgets.SiteGameX(site.UtmX, transform.OriginEastingNorthing.X, transform.MetersPerGameUnit),
                site.GameY - SedanProofBudgets.SiteGameY(site.UtmY, transform.OriginEastingNorthing.Y, transform.MetersPerGameUnit)),
            site =>
            {
                var before = diagnostics.Items.Count();
                var found = reader.TrySampleHeight(site.GameX, site.GameY, out var payloadZ, diagnostics);
                return (found, payloadZ, found ? null : SummarizeDiagnostics(diagnostics.Items.Skip(before)));
            });
    }

    private static IReadOnlyList<PrimaryFact> BuildPrimaryFacts(
        IReadOnlyList<SedanProofSite> sites,
        IReadOnlyList<SedanProofSiteSample> samples,
        GeneratedMapPayloadReader reader,
        PreviewTransform preview)
    {
        var transform = reader.Metadata.Grid.Transform;
        var facts = new List<PrimaryFact>(sites.Count);

        for (var index = 0; index < sites.Count; index++)
        {
            var site = sites[index];
            var payloadZ = samples[index].PayloadZ!.Value;

            var residual = SedanProofBudgets.BudgetAResidualMeters(
                transform.VerticalOriginMeters,
                payloadZ,
                site.TruthZ,
                transform.MetersPerGameUnit,
                transform.VerticalExaggeration);

            facts.Add(new PrimaryFact(
                site.Id,
                site.GameX,
                site.GameY,
                payloadZ,
                SedanProofBudgets.EngineXyFromPayload(site.GameX, preview.CropOriginGameX),
                SedanProofBudgets.EngineXyFromPayload(site.GameY, preview.CropOriginGameY),
                residual));
        }

        return facts;
    }

    internal static IReadOnlyList<SedanProofSiteSample> CollectSiteSamples(
        IEnumerable<SedanProofSite> sites,
        Func<SedanProofSite, (double dx, double dy)> crosscheck,
        Func<SedanProofSite, (bool ok, double z, string? reason)> sampler)
    {
        var samples = new List<SedanProofSiteSample>();

        foreach (var site in sites)
        {
            var (dx, dy) = crosscheck(site);

            if (!IsWithinFrameCrosscheckTolerance(dx) || !IsWithinFrameCrosscheckTolerance(dy))
            {
                samples.Add(new SedanProofSiteSample(
                    site.Id,
                    dx,
                    dy,
                    null,
                    $"crosscheck delta game_x {dx} game_y {dy} exceeds {CrossCheckToleranceGameUnits} game units"));
                continue;
            }

            var (ok, z, reason) = sampler(site);
            samples.Add(new SedanProofSiteSample(site.Id, dx, dy, ok ? z : (double?)null, ok ? null : reason ?? "no height sampled"));
        }

        return samples;
    }

    internal static bool IsWithinFrameCrosscheckTolerance(double delta)
    {
        return Math.Abs(delta) <= CrossCheckToleranceGameUnits;
    }

    internal static string? DescribeSampleFailures(IReadOnlyList<SedanProofSiteSample> samples)
    {
        var failedIds = samples
            .Where(sample => sample.FailureReason is not null || sample.PayloadZ is null)
            .Select(sample => sample.Id)
            .ToArray();

        if (failedIds.Length == 0)
        {
            return null;
        }

        return $"Payload sampling failed for {string.Join(", ", failedIds)} ({failedIds.Length} of {samples.Count} primaries); "
            + $"no Game was constructed. All primaries: {DescribeSamples(samples)}.";
    }

    private static string DescribeSamples(IEnumerable<SedanProofSiteSample> samples)
    {
        return string.Join("; ", samples.Select(sample =>
        {
            var payloadText = sample.PayloadZ?.ToString(CultureInfo.InvariantCulture) ?? "null";
            return $"{sample.Id} dx={sample.CrossDeltaX} dy={sample.CrossDeltaY} payloadZ={payloadText} reason={sample.FailureReason ?? "none"}";
        }));
    }

    private static int CheckImageryChannels(JsonDocument prepared, GeneratedMapPayloadReport report)
    {
        var pointNames = new List<string>();

        foreach (var point in prepared.RootElement.GetProperty("controlPoints").EnumerateArray())
        {
            if (point.TryGetProperty("imageryRgb", out _))
            {
                pointNames.Add(point.GetProperty("name").GetString()!);
            }
        }

        Assert.Equal(ExpectedImageryPoints, pointNames.Count);

        var channelChecks = 0;

        foreach (var name in pointNames)
        {
            foreach (var channel in Channels)
            {
                var check = report.ValidationChecks.FirstOrDefault(candidate =>
                    candidate.Category == "control-point" && candidate.Name == name + "/" + channel);

                Assert.True(
                    check is not null && check.Status == PayloadValidationStatus.Passed,
                    $"Imagery channel check {name}/{channel} is missing or did not pass.");
                channelChecks++;
            }
        }

        // The validator emits one check per channel, so two points yield six checks.
        Assert.Equal(ExpectedImageryPoints * ChannelsPerImageryPoint, channelChecks);
        return channelChecks;
    }

    private static NativeRun RunNativeGame(string userRoot, IReadOnlyList<PrimaryFact> primaries, string shotPath)
    {
        var realUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Data");
        var realMapCache = Path.Combine(realUserDataFolder, @"Maps\MapCache.ini");
        var realFolderExistedBefore = Directory.Exists(realUserDataFolder);
        var realMapCacheWriteBefore = File.Exists(realMapCache) ? File.GetLastWriteTimeUtc(realMapCache) : (DateTime?)null;

        var game = new Game(
            InstalledFilesTestData.GetInstallation(SageGame.CncGenerals),
            preferredBackend: null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true, UserDataRootPath = userRoot },
            window: null);

        uint startFrame;
        uint frameAfterRun;
        uint frameAfterCapture;
        IReadOnlyList<float?> engineHeights;
        LuminanceStats luminance;

        try
        {
            Assert.Equal(userRoot, game.UserDataFolder);

            var playerSettings = new PlayerSetting[]
            {
                new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
                new(1, "FactionGLA", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "GLA"),
            };

            game.StartSkirmishOrMultiPlayerGame(
                $"Maps/{MapName}.map",
                new EchoConnection(),
                playerSettings,
                1,
                isMultiPlayer: false);
            game.StartRun();
            game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
            foreach (var player in game.Scene3D.Players)
            {
                player.DisableAI();
            }

            game.EnableFixedLogicTimestep();

            var empty = Array.Empty<InputMessage>();
            startFrame = game.GameLogic.CurrentFrame.Value;
            for (var i = 0; i < FrameCount; i++)
            {
                game.Update(empty);
            }

            frameAfterRun = game.GameLogic.CurrentFrame.Value;
            Assert.Equal(startFrame + FrameCount, frameAfterRun);
            Assert.True(game.InGame, "Skirmish start on the completed freeze map did not reach the in-game state.");
            Assert.NotNull(game.Scene3D.Terrain);

            engineHeights = primaries
                .Select(primary => game.Scene3D.Terrain.GetGroundHeight(new Vector2((float)primary.EngineX, (float)primary.EngineY)))
                .ToArray();

            game.IsLogicRunning = false;

            var view = game.Scene3D.TacticalView;
            view.InitHeightForMap();
            view.SetAngleAndPitchToDefault();
            view.SnapHeightAboveGround(CameraHeight);
            view.LookAt(new Vector3((float)primaries[0].EngineX, (float)primaries[0].EngineY, 0f));

            for (var i = 0; i < 3; i++)
            {
                game.Update(empty);
                game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
                game.Render();
            }

            frameAfterCapture = game.GameLogic.CurrentFrame.Value;
            Assert.Equal(startFrame + FrameCount, frameAfterCapture);

            var frame = FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
            luminance = LuminanceStats.Compute(frame.PixelData);
            Assert.True(
                luminance.BrightFraction >= 0.005d,
                $"Render is black: only {luminance.BrightFraction:P2} of pixels have luminance > 8 "
                + $"(min {luminance.Min}, max {luminance.Max}, mean {luminance.Mean:F1}).");
            Assert.True(
                luminance.StandardDeviation > 4.0d && luminance.Distinct > 32,
                $"Render is uniform: stddev {luminance.StandardDeviation:F2}, distinct values {luminance.Distinct}.");

            FrameCapturer.CaptureToPng(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target, shotPath);
        }
        finally
        {
            game.Dispose();
        }

        Assert.Equal(realFolderExistedBefore, Directory.Exists(realUserDataFolder));
        var realMapCacheWriteAfter = File.Exists(realMapCache) ? File.GetLastWriteTimeUtc(realMapCache) : (DateTime?)null;
        Assert.Equal(realMapCacheWriteBefore, realMapCacheWriteAfter);

        // Matches UserMapCache's literal @"Maps\MapCache.ini"; on Linux this is a single file name.
        var sandboxMapCache = Path.Combine(userRoot, @"Maps\MapCache.ini");
        Assert.True(File.Exists(sandboxMapCache), $"Expected the sandbox map cache: {sandboxMapCache}");
        Assert.Contains(MapName, File.ReadAllText(sandboxMapCache));

        return new NativeRun(startFrame, frameAfterRun, frameAfterCapture, engineHeights, luminance, shotPath);
    }

    private static void CopyEnvironmentShot(string shotPath, string metricsJson)
    {
        var envShotPath = Environment.GetEnvironmentVariable(PreviewShotVariable);

        if (string.IsNullOrEmpty(envShotPath))
        {
            return;
        }

        if (!File.Exists(envShotPath))
        {
            var envShotDirectory = Path.GetDirectoryName(envShotPath);

            if (!string.IsNullOrEmpty(envShotDirectory))
            {
                Directory.CreateDirectory(envShotDirectory);
            }

            File.Copy(shotPath, envShotPath);
        }

        var envMetricsPath = envShotPath + ".metrics.json";

        if (!File.Exists(envMetricsPath))
        {
            File.WriteAllText(envMetricsPath, metricsJson);
        }
    }

    private static string RunCli(SedanProofJournalWriter journals, string stage, string[] arguments, params string[] expectedFragments)
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();
        var exit = GeoImportCli.Run(arguments, standardOut, errorOut, CancellationToken.None);
        var output = standardOut.ToString();
        var errorText = errorOut.ToString();
        var succeeded = exit == 0;

        var diagnostics = succeeded
            ? Array.Empty<SedanProofJournalDiagnostic>()
            : new[] { new SedanProofJournalDiagnostic("Error", string.Empty, "Cli", errorText) };
        var payloadSha = output.Split('\n').FirstOrDefault(line => line.StartsWith("payloadSha256: ", StringComparison.Ordinal));

        journals.Write(new SedanProofValidationJournal
        {
            TimestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            Scope = Scope,
            Stage = stage,
            Action = "geoimport " + string.Join(' ', arguments),
            Success = succeeded,
            Checks = Array.Empty<SedanProofJournalCheck>(),
            Diagnostics = diagnostics,
            Note = payloadSha?.Trim(),
        });

        Assert.True(succeeded, $"geoimport {arguments[0]} exited {exit}: {errorText}");

        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, output);
        }

        return output;
    }

    private static string SummarizeDiagnostics(IEnumerable<GeoDiagnostic> items)
    {
        return string.Join("; ", items.Select(item => $"{item.Code} {item.Severity} {item.Message}"));
    }

    private static SedanProofJournalCheck[] ToJournalChecks(IEnumerable<PayloadValidationCheck> checks)
    {
        return checks
            .Select(check => new SedanProofJournalCheck(
                check.Category,
                check.Name,
                check.Status.ToString(),
                check.Expected,
                check.Actual,
                check.Tolerance,
                check.Units,
                check.Message))
            .ToArray();
    }

    private static SedanProofJournalDiagnostic[] ToJournalDiagnostics(IEnumerable<GeoDiagnostic> items)
    {
        return items
            .Select(item => new SedanProofJournalDiagnostic(item.Severity.ToString(), item.Code, item.Stage.ToString(), item.Message))
            .ToArray();
    }

    private static string DescribeFailedChecks(IEnumerable<PayloadValidationCheck> checks)
    {
        var failed = checks
            .Where(check => check.Status == PayloadValidationStatus.Failed)
            .Select(check => $"{check.Name} status={check.Status} expected={check.Expected} actual={check.Actual} tolerance={check.Tolerance} units={check.Units}")
            .ToArray();

        return failed.Length == 0 ? "none" : string.Join("; ", failed);
    }

    private static List<double> CollectSourceAccuracies(JsonDocument prepared)
    {
        var declared = new List<double>();

        foreach (var source in prepared.RootElement.GetProperty("sources").EnumerateArray())
        {
            if (source.TryGetProperty("verticalAccuracyMeters", out var accuracy))
            {
                declared.Add(accuracy.GetDouble());
            }
        }

        return declared;
    }

    private static string DescribeSourceAccuracies(IReadOnlyList<double> declared)
    {
        return declared.Count == 0 ? "absent/UNKNOWN" : string.Join(", ", declared.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string Sha256Bytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static Inventory HashInventory(string root)
    {
        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Relative = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Hash = Sha256File(path),
            })
            .OrderBy(entry => entry.Relative, StringComparer.Ordinal)
            .ToList();

        var manifest = string.Join("\n", files.Select(entry => $"{entry.Relative} {entry.Hash}"));

        return new Inventory(
            Sha256Text(manifest),
            files.ToDictionary(entry => entry.Relative, entry => entry.Hash, StringComparer.Ordinal));
    }

    private static void AssertInventoryUnchanged(Inventory before, Inventory after, string what)
    {
        if (before.Aggregate == after.Aggregate)
        {
            return;
        }

        var changes = before.Files.Keys
            .Union(after.Files.Keys)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Where(key => !string.Equals(before.Files.GetValueOrDefault(key), after.Files.GetValueOrDefault(key), StringComparison.Ordinal))
            .Select(key => $"{key}: {before.Files.GetValueOrDefault(key) ?? "absent"} -> {after.Files.GetValueOrDefault(key) ?? "absent"}");

        Assert.Fail($"{what} changed: {string.Join("; ", changes)}");
    }

    private static string Sha256File(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static string Sha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private sealed record Inventory(string Aggregate, IReadOnlyDictionary<string, string> Files);

    private sealed record PrimaryFact(
        string Id,
        double GameX,
        double GameY,
        double PayloadZ,
        double EngineX,
        double EngineY,
        double ResidualAMeters);

    internal sealed record SedanProofSiteSample(
        string Id,
        double CrossDeltaX,
        double CrossDeltaY,
        double? PayloadZ,
        string? FailureReason);

    private sealed record NativeRun(
        uint StartFrame,
        uint FrameAfterRun,
        uint FrameAfterCapture,
        IReadOnlyList<float?> EngineHeights,
        LuminanceStats Luminance,
        string ShotPath);

    private sealed record ProofMetrics(
        string TimestampLocal,
        string Scope,
        string? Commit,
        string CanonicalDigestSha256,
        string MapSha256,
        double MetersPerGameUnit,
        double VerticalOriginMeters,
        double VerticalExaggeration,
        double OriginEastingNorthingX,
        double OriginEastingNorthingY,
        double CropMinimumGameZ,
        int ImageryChannelChecks,
        IReadOnlyList<double> SourceAccuracies,
        IReadOnlyList<PrimaryFact> Primaries,
        IReadOnlyList<double> BudgetBGameUnits,
        uint StartFrame,
        uint FrameAfterRun,
        LuminanceStats Luminance,
        string ShotPath);

    private sealed record LuminanceStats(
        double Min,
        double Max,
        double Mean,
        double StandardDeviation,
        int Distinct,
        double BrightFraction)
    {
        // PixelData is tightly packed 4 bytes per pixel in BGRA order (FrameCapturer).
        public static LuminanceStats Compute(byte[] pixels)
        {
            var count = pixels.Length / 4;
            var values = new int[count];
            long sum = 0;
            var bright = 0;

            for (var i = 0; i < count; i++)
            {
                var offset = i * 4;
                var r = pixels[offset + 2];
                var g = pixels[offset + 1];
                var b = pixels[offset];
                var luminance = (int)((0.2126d * r) + (0.7152d * g) + (0.0722d * b));
                values[i] = luminance;
                sum += luminance;

                if (luminance > 8)
                {
                    bright++;
                }
            }

            var mean = (double)sum / count;
            double squaredSum = 0;

            foreach (var value in values)
            {
                var delta = value - mean;
                squaredSum += delta * delta;
            }

            return new LuminanceStats(
                values.Min(),
                values.Max(),
                mean,
                Math.Sqrt(squaredSum / count),
                values.Distinct().Count(),
                (double)bright / count);
        }
    }

    private sealed class NamedTerrainTextureCatalog : ITerrainTextureCatalog
    {
        public NamedTerrainTextureCatalog(IReadOnlyCollection<string> names)
        {
            TerrainTextureNames = names;
        }

        public IReadOnlyCollection<string> TerrainTextureNames { get; }
    }
}
