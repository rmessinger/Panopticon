using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>
/// CPU tests of the Sedan proof artifact reader over synthetic trees under a temp directory.
/// No real freeze, DEM, or operator path is read; every negative test asserts its specific
/// contract failure message.
/// </summary>
public sealed class SedanProofArtifactsTests : IDisposable
{
    private const string StageName = "sedan-freeze-0001";

    // _SEL_COLUMNS copied from scripts/geo/sedan_lidar_select.py lines 650-655, independent of the reader.
    private static readonly string[] SelectionColumns =
    {
        "id", "role", "rank", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y",
        "game_x", "game_y", "wgs84_lat", "wgs84_lon",
        "truth_z", "sigma_fit", "slope_deg", "curv_bound", "density",
        "n2", "n3", "n4", "n5", "n6", "n9", "n17", "stratum", "sector",
    };

    private static readonly string[] PrimaryIds = { "P0", "P1", "P2", "P3", "P4" };

    private static readonly string[] AlternateIds = { "A5", "A6" };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _workRoot;

    public SedanProofArtifactsTests()
    {
        _workRoot = Path.Combine(Path.GetTempPath(), "panopticon-sedan-proof-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workRoot))
        {
            Directory.Delete(_workRoot, recursive: true);
        }
    }

    [Fact]
    public void Load_ValidSyntheticFreeze_ReturnsFivePrimariesAndTwoAlternates()
    {
        var proof = BuildValidTree();

        using var loadout = SedanProofArtifacts.Load(proof);

        Assert.Equal(5, loadout.PrimarySites.Count);
        Assert.Equal(2, loadout.AlternateSites.Count);
        Assert.True(loadout.PrimarySites.Select(site => site.Sector).Distinct().Count() >= 3);
        Assert.Equal(1, loadout.PrimarySites[0].Rank);
        Assert.Equal("primary", loadout.PrimarySites[0].Role);
        Assert.Null(loadout.AlternateSites[0].SigmaFit);
        Assert.Equal(64, loadout.FrozenManifestCanonicalSha256.Length);
        Assert.Equal(SedanProofArtifacts.PreparedManifestRelativePath, Path.GetFileName(loadout.PreparedManifestPath));
    }

    [Fact]
    public void Load_MissingCompletedMarker_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { WriteCompleted = false });

        AssertContractFailure(proof, "no completed final freeze under");
    }

    [Fact]
    public void Load_TwoCompletedStages_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { ExtraCompletedStage = true });

        AssertContractFailure(proof, "multiple completed freezes under");
    }

    [Fact]
    public void Load_LedgerShaMismatch_Throws()
    {
        var proof = BuildValidTree();
        var candidates = Path.Combine(proof, "freeze", StageName, "final", "candidates.tsv");
        var bytes = File.ReadAllBytes(candidates);
        bytes[0] ^= 0x20;
        File.WriteAllBytes(candidates, bytes);

        AssertContractFailure(proof, "completed freeze artifact candidates_tsv sha256 drifted");
    }

    [Fact]
    public void Load_PreliminaryScope_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { Scope = "stage-1 preliminary selection" });

        AssertContractFailure(proof, "preliminary selection can never be the final freeze");
    }

    [Fact]
    public void Load_ConstantsDrift_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { E4Sigma = 0.09 });

        AssertContractFailure(proof, "frozen manifest constants.e4_sigma drifted");
    }

    [Fact]
    public void Load_FewerThanFivePrimaries_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { PrimaryCount = 4 });

        AssertContractFailure(proof, "selection.tsv has 4 primary rows; expected 5");
    }

    [Fact]
    public void Load_TwoPrimarySectors_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { PrimarySectors = new[] { "S1", "S1", "S2", "S2", "S2" } });

        AssertContractFailure(proof, "primaries cover 2 sectors; at least 3 required");
    }

    [Fact]
    public void Load_NaNInNumericTsvField_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { FirstPrimaryTruthZ = "NaN" });

        AssertContractFailure(proof, "field 'truth_z' is not a finite number");
    }

    [Fact]
    public void Load_DeclaredSourceAccuracyAboveOperatorLimit_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { AccuracyMeters = 1.5 });

        AssertContractFailure(proof, "declared source accuracy 1.5 exceeds the operator 1.0 m limit: escalate before any DEM comparison");
    }

    [Fact]
    public void Load_ExampleOnlyPlaceholderControlPoint_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { FirstVerticalName = "EXAMPLE ONLY - NOT MEASURED vertical-1" });

        AssertContractFailure(proof, "is an EXAMPLE ONLY placeholder");
    }

    [Fact]
    public void Load_VerticalToleranceNotOneMeter_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { VerticalTolerance = 0.5 });

        AssertContractFailure(proof, "tolerances.verticalMeters must be 1 (operator limit); found 0.5");
    }

    [Fact]
    public void Load_CanonicalDigestMismatch_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { ReceiptDigestOverride = new string('0', 64) });

        AssertContractFailure(proof, "canonical digest mismatch");
    }

    [Fact]
    public void Load_ProducerReceiptWrongSha_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { ProducerSha256 = new string('1', 64) });

        AssertContractFailure(proof, "producer receipt producer.sha256");
    }

    [Fact]
    public void Load_WorkerBlockWrongBaseSha_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { BaseWorkerSha256 = new string('2', 64) });

        AssertContractFailure(proof, "receipt worker.base_worker_sha256");
    }

    [Fact]
    public void Load_BudgetBLimitNotOneQuantum_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { BudgetBLimitGameUnits = 0.05 });

        AssertContractFailure(proof, "receipt budget_b.limit_game_units");
    }

    [Fact]
    public void Load_CarriedUnresolvedMissingEpochEntry_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec
        {
            CarriedUnresolved = new[] { "RGE ALTI tile-specific accuracy UNKNOWN", "2024 auto-classification bias" },
        });

        AssertContractFailure(proof, "carried_unresolved has no entry containing 'epoch mismatch'");
    }

    [Fact]
    public void Load_NumericProofPassTrue_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { NumericProofPass = true });

        AssertContractFailure(proof, "numeric_proof_pass must be false");
    }

    [Fact]
    public void Load_ExpectedFreezeDigestMismatch_Throws()
    {
        var proof = BuildValidTree();
        var expected = new SedanProofExpectedFreeze(new string('0', 64), PrimaryIds, AlternateIds);

        AssertContractFailure(proof, "expected freeze canonical digest", expected);
    }

    [Fact]
    public void Load_ExpectedFreezePinned_Passes()
    {
        var proof = BuildValidTree();

        var unpinned = SedanProofArtifacts.Load(proof);
        var digest = unpinned.FrozenManifestCanonicalSha256;
        unpinned.Dispose();

        using var pinned = SedanProofArtifacts.Load(proof, new SedanProofExpectedFreeze(digest, PrimaryIds, AlternateIds));

        Assert.Equal(digest, pinned.FrozenManifestCanonicalSha256);
        Assert.Equal(5, pinned.PrimarySites.Count);
        Assert.Equal(2, pinned.AlternateSites.Count);
        Assert.Equal(Path.Combine(proof, SedanProofArtifacts.IntegrityProducerReceiptRelativePath), pinned.ProducerReceiptPath);
        Assert.Equal(Path.Combine(proof, SedanProofArtifacts.IntegrityManifestRelativePath), pinned.IntegrityManifestPath);
        Assert.Equal(new[] { SedanProofArtifacts.BaseWorkerSha256, SedanProofArtifacts.BandedWorkerSha256 }, pinned.WorkerRegistry);
    }

    [Fact]
    public void CanonicalDigest_RawTokenRule_MatchesPythonCanonicalDigest()
    {
        // Python: m = json.loads(text); m.pop("generated_at"); sha256(json.dumps(m, sort_keys=True,
        // separators=(",", ":"), ensure_ascii=True)) yields this value.
        const string GoldenText = """{"generated_at":"2026-11-03T10:00:00+01:00","b":1.0,"a":{"z":[1e-09,null,"x\u00e9"],"y":true,"generated_at":"nested"}}""";
        const string PythonDigest = "2008aeb15a3d358fb568642b9bff394a2cf82da0bf90c05d42a8e9f367974df4";

        using var document = JsonDocument.Parse(GoldenText);

        Assert.Equal(PythonDigest, SedanProofArtifacts.CanonicalDigest(document.RootElement));
    }

    [Fact]
    public void Load_NonDenseRanks_HappyPath_ReturnsRanksInRowOrder()
    {
        var proof = BuildValidTree();

        using var loadout = SedanProofArtifacts.Load(proof);

        Assert.Equal(new[] { 1, 2, 4, 8, 17 }, loadout.PrimarySites.Select(site => site.Rank).ToArray());
        Assert.Equal(new[] { 3, 9 }, loadout.AlternateSites.Select(site => site.Rank).ToArray());
        Assert.Equal(Path.Combine(proof, SedanProofArtifacts.PreparationReceiptRelativePath), loadout.PreparationReceiptPath);
        Assert.Equal(Path.Combine(proof, SedanProofArtifacts.CommandsLedgerRelativePath), loadout.CommandsLedgerPath);
        Assert.Equal(Path.Combine(proof, SedanProofArtifacts.PreviewTexturesRelativePath), loadout.PreviewTexturesPath);
        Assert.Equal(12.5, loadout.ReceiptOriginMeters);
        Assert.True(loadout.FreezeVerifiedAt < loadout.FirstDemRasterIoAt);
    }

    [Fact]
    public void Ledger_MissingFile_Throws()
    {
        var proof = BuildValidTree();
        File.Delete(ProofLedgerPath(proof));

        AssertContractFailure(proof, SedanProofArtifacts.CommandsLedgerRelativePath);
    }

    [Fact]
    public void Ledger_NotEndingWithPreparationCompleted_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines.RemoveAt(lines.Count - 1));

        AssertContractFailure(proof, "must end with exactly one preparation_completed event");
    }

    [Fact]
    public void Ledger_FreezeValidatedNotFirst_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => (lines[0], lines[1]) = (lines[1], lines[0]));

        AssertContractFailure(proof, "unfrozen ledger opening");
    }

    [Fact]
    public void Ledger_FreezeValidatedDuplicated_Accepts()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines.Insert(1, lines[0]));

        using var loadout = SedanProofArtifacts.Load(proof);

        Assert.NotNull(loadout);
    }

    [Fact]
    public void Ledger_DemStartTimestampMismatch_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[2] = lines[2].Replace(FirstDemRasterIoAtText, "2026-11-03T10:06:00.000-05:00"));

        AssertContractFailure(proof, "ledger dem_rasterio_start.firstDemRasterIoAt");
    }

    [Fact]
    public void Ledger_HeightSemanticBeforeDemStart_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => (lines[2], lines[3]) = (lines[3], lines[2]));

        AssertContractFailure(proof, "precedes dem_rasterio_start");
    }

    [Fact]
    public void Ledger_DemStartNotImmediatelyBeforeHeightInvocation_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => (lines[1], lines[2]) = (lines[2], lines[1]));

        AssertContractFailure(proof, "must immediately precede the first height-semantic invocation");
    }

    [Fact]
    public void Ledger_DemArgvMismatch_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[3] = lines[3].Replace("EPSG:32631", "EPSG:2154"));

        AssertContractFailure(proof, "dem_rasterio_start argv does not equal the first height-semantic tool_invoked argv");
    }

    [Fact]
    public void Ledger_UnknownEvent_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[1] = lines[1].Replace("\"event\":\"tool_invoked\"", "\"event\":\"mystery_event\""));

        AssertContractFailure(proof, "carries unknown event 'mystery_event'");
    }

    [Fact]
    public void Ledger_DuplicatePropertyInLine_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[0] = lines[0].Replace("{\"event\":", "{\"event\":\"freeze_validated_complete\",\"event\":"));

        AssertContractFailure(proof, "repeats property 'event'");
    }

    [Fact]
    public void Ledger_LineCapExceeded_Throws()
    {
        var proof = BuildValidTree();
        MutateLedger(proof, _ => string.Concat(Enumerable.Repeat("{\"event\":\"x\"}\n", 4097)));

        AssertContractFailure(proof, "exceeds the 4096 line cap");
    }

    [Fact]
    public void Ledger_EmptyLine_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines.Insert(1, string.Empty));

        AssertContractFailure(proof, "is empty");
    }

    [Fact]
    public void Ledger_NotNewlineTerminated_Throws()
    {
        var proof = BuildValidTree();
        MutateLedger(proof, text => text.TrimEnd('\n'));

        AssertContractFailure(proof, "must be non-empty and newline-terminated");
    }

    [Fact]
    public void Ledger_ToolInvokedNonZeroReturnCode_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[1] = lines[1].Replace("\"returncode\":0", "\"returncode\":1"));

        AssertContractFailure(proof, "tool_invoked returncode must be 0");
    }

    [Fact]
    public void Ledger_ToolInvokedTimedOut_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[1] = lines[1].Replace("\"timedOut\":false", "\"timedOut\":true"));

        AssertContractFailure(proof, "tool_invoked timedOut must be false");
    }

    [Fact]
    public void Ledger_FreezeTimestampMismatch_Throws()
    {
        var proof = BuildValidTree();
        EditLedgerLines(proof, lines => lines[0] = lines[0].Replace(FreezeVerifiedAtText, "2026-11-03T10:01:00.000-05:00"));

        AssertContractFailure(proof, "ledger freezeVerifiedAt");
    }

    [Fact]
    public void Ledger_FreezeReceiptShaMismatch_Throws()
    {
        var proof = BuildValidTree();
        var digests = ReadFreezeDigests(proof);
        EditLedgerLines(proof, lines => lines[0] = lines[0].Replace(digests.ReceiptSha, ZeroSha));

        AssertContractFailure(proof, "ledger freezeReceiptSha256");
    }

    [Fact]
    public void Ledger_OutputShaMismatch_Throws()
    {
        var proof = BuildValidTree();
        var demSha = Sha256Hex(File.ReadAllBytes(Path.Combine(proof, SedanProofArtifacts.DemRelativePath)));
        MutateLedger(proof, text => text.Replace(demSha, ZeroSha));

        AssertContractFailure(proof, "ledger outputs.dem_tif.sha256");
    }

    [Fact]
    public void Ledger_CommandsShaMismatch_Throws()
    {
        var proof = BuildValidTree();
        var commandsSha = Sha256Hex(File.ReadAllBytes(ProofLedgerPath(proof)));
        MutateReceiptText(proof, text => text.Replace(commandsSha, ZeroSha));

        AssertContractFailure(proof, "receipt commands_sha256");
    }

    [Fact]
    public void Ledger_MultiAttemptActualShape_Accepts()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        var lines = File.ReadAllLines(ProofLedgerPath(proof));

        Assert.Equal(63, lines.Length);
        Assert.Equal(3, lines.Count(line => line.Contains("\"event\":\"freeze_validated_complete\"", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line.Contains("\"event\":\"dem_rasterio_start\"", StringComparison.Ordinal)));
        Assert.Equal(57, lines.Count(line => line.Contains("\"event\":\"tool_invoked\"", StringComparison.Ordinal)));
        Assert.Equal(1, lines.Count(line => line.Contains("\"event\":\"preparation_completed\"", StringComparison.Ordinal)));

        using var loadout = SedanProofArtifacts.Load(proof);

        Assert.NotNull(loadout);
    }

    [Fact]
    public void Ledger_LatestAttemptTruncatedAfterCompletedEarlierAttempt_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines =>
        {
            lines.Insert(34, lines[62]);
            lines.RemoveAt(lines.Count - 1);
        });

        AssertContractFailure(proof, "current attempt must end with exactly one preparation_completed event and no events after it");
    }

    [Fact]
    public void Ledger_UnknownEventInPriorAttempt_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines[5] = lines[5].Replace("\"event\":\"tool_invoked\"", "\"event\":\"mystery_event\"", StringComparison.Ordinal));

        AssertContractFailure(proof, "carries unknown event 'mystery_event'");
    }

    [Fact]
    public void Ledger_PriorAttemptHeightBeforeItsDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines =>
        {
            var height = lines[19];
            lines.RemoveAt(19);
            lines.Insert(17, height);
        });

        AssertContractFailure(proof, "height-semantic tool_invoked at ledger line 18 precedes dem_rasterio_start");
    }

    [Fact]
    public void Ledger_PriorAttemptHeightWithoutDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.RemoveAt(17));

        AssertContractFailure(proof, "but no dem_rasterio_start");
    }

    [Fact]
    public void Ledger_PriorAttemptTimestampInversion_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines[17] = lines[17].Replace(MultiFailedDemText, "2026-03-01T10:30:00.000-05:00", StringComparison.Ordinal));

        AssertContractFailure(proof, "chronology ledger timestamp inversion at ledger line 18");
    }

    [Fact]
    public void Ledger_ReceiptBoundToFirstAttemptFreeze_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        MutateReceiptText(proof, text => text.Replace(MultiCurrentFreezeText, MultiFirstFreezeText, StringComparison.Ordinal));

        AssertContractFailure(proof, "ledger freezeVerifiedAt");
    }

    [Fact]
    public void Ledger_ReceiptBoundToFailedAttemptDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        MutateReceiptText(proof, text => text.Replace(MultiCurrentDemText, MultiFailedDemText, StringComparison.Ordinal));

        AssertContractFailure(proof, "chronology order violated");
    }

    [Fact]
    public void Ledger_SliceFreezeAtOrAfterSliceDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        const string inverted = "2026-03-01T11:07:00.000-05:00";
        MutateReceiptText(proof, text => text.Replace(MultiCurrentFreezeText, inverted, StringComparison.Ordinal));
        EditLedgerLines(proof, lines => lines[34] = lines[34].Replace(MultiCurrentFreezeText, inverted, StringComparison.Ordinal));

        AssertContractFailure(proof, "chronology order violated");
    }

    [Fact]
    public void Ledger_EventAfterFinalPreparationCompleted_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.Add(lines[61]));

        AssertContractFailure(proof, "has events after its preparation_completed");
    }

    [Fact]
    public void Ledger_TwoDemStartsInCurrentAttempt_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.Insert(36, lines[35]));

        AssertContractFailure(proof, "must carry exactly one dem_rasterio_start event");
    }

    [Fact]
    public void Ledger_PreparationCompletedBeforeCurrentDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.Insert(35, lines[62]));

        AssertContractFailure(proof, "has preparation_completed before dem_rasterio_start");
    }

    [Fact]
    public void Ledger_FirstHeightNotImmediatelyAfterCurrentDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.Insert(36, Line(Invoked("gdalinfo", new[] { "gdalinfo", "synthetic-source.tif" }, heightSemantic: false))));

        AssertContractFailure(proof, "must immediately precede the first height-semantic invocation");
    }

    [Fact]
    public void Ledger_FirstHeightArgvDiffersFromCurrentDemStart_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines[36] = lines[36].Replace("EPSG:32631", "EPSG:2154", StringComparison.Ordinal));

        AssertContractFailure(proof, "dem_rasterio_start argv does not equal the first height-semantic tool_invoked argv");
    }

    [Fact]
    public void Ledger_NoFreezeEventAtAll_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { MultiAttempt = true });
        EditLedgerLines(proof, lines => lines.RemoveAll(line => line.Contains("\"event\":\"freeze_validated_complete\"", StringComparison.Ordinal)));

        AssertContractFailure(proof, "carries no freeze_validated_complete event");
    }

    [Fact]
    public void Chronology_ReceiptInverted_Throws()
    {
        var proof = BuildValidTree();
        MutateReceiptText(proof, text => text.Replace(FreezeVerifiedAtText, "2026-11-03T10:07:00.000-05:00"));

        AssertContractFailure(proof, "chronology order violated");
    }

    [Fact]
    public void Chronology_ReceiptEqualTimestamps_Throws()
    {
        var proof = BuildValidTree();
        MutateReceiptText(proof, text => text.Replace(FreezeVerifiedAtText, FirstDemRasterIoAtText));

        AssertContractFailure(proof, "chronology order violated");
    }

    [Fact]
    public void Chronology_UnparseableDemTimestamp_Throws()
    {
        var proof = BuildValidTree();
        MutateReceiptText(proof, text => text.Replace(FirstDemRasterIoAtText, "not-a-time"));

        AssertContractFailure(proof, "firstDemRasterIoAt is not an ISO-8601 timestamp");
    }

    [Fact]
    public void Hash_CanonicalDigestInReceiptWrong_Throws()
    {
        var proof = BuildValidTree();
        ReplaceInReceipt(proof, ReadFreezeDigests(proof).CanonicalDigest);

        AssertContractFailure(proof, "receipt freeze.canonical_digest");
    }

    [Fact]
    public void Hash_CandidatesShaWrong_Throws()
    {
        var proof = BuildValidTree();
        ReplaceInReceipt(proof, ReadFreezeDigests(proof).CandidatesSha);

        AssertContractFailure(proof, "receipt freeze.candidates_sha256");
    }

    [Fact]
    public void Hash_SelectionShaWrong_Throws()
    {
        var proof = BuildValidTree();
        ReplaceInReceipt(proof, ReadFreezeDigests(proof).SelectionSha);

        AssertContractFailure(proof, "receipt freeze.selection_sha256");
    }

    [Fact]
    public void Hash_ReceiptShaWrong_Throws()
    {
        var proof = BuildValidTree();
        ReplaceInReceipt(proof, ReadFreezeDigests(proof).ReceiptSha);

        AssertContractFailure(proof, "receipt freeze.receipt_sha256");
    }

    [Fact]
    public void Hash_FrozenManifestShaWrong_Throws()
    {
        var proof = BuildValidTree();
        ReplaceInReceipt(proof, ReadFreezeDigests(proof).FrozenManifestSha);

        AssertContractFailure(proof, "receipt freeze.frozen_manifest_sha256");
    }

    [Fact]
    public void Hash_OutputShaWrong_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(Child(receipt, "outputs"), "dem_tif")["sha256"] = ZeroSha);

        AssertContractFailure(proof, "receipt outputs.dem_tif.sha256 does not match");
    }

    [Fact]
    public void Hash_OutputSizeWrong_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(Child(receipt, "outputs"), "manifest")["size"] = 1L);

        AssertContractFailure(proof, "receipt outputs.manifest.size does not match");
    }

    [Fact]
    public void Receipt_OverTwoMiB_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => receipt["generated_at"] = new string('x', 2_200_000));

        AssertContractFailure(proof, "exceeds the 2097152 byte cap");
    }

    [Fact]
    public void Receipt_NaNLiteral_Throws()
    {
        var proof = BuildValidTree();
        MutateReceiptText(proof, text => text.Replace("\"budget_b_game_units\": 0.0390625", "\"budget_b_game_units\": NaN"));

        AssertContractFailure(proof, "contains a non-finite literal (NaN or Infinity)");
    }

    [Fact]
    public void Receipt_LazTileCountWrong_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(Child(receipt, "freeze"), "laz_tiles_sha256").Remove(LazTileNames[5]));

        AssertContractFailure(proof, "receipt freeze.laz_tiles_sha256 must carry 6 tiles");
    }

    [Fact]
    public void Receipt_ExtractMembers26_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "extract_receipt")["members"] = 26);

        AssertContractFailure(proof, "receipt extract_receipt.members must be 27");
    }

    [Fact]
    public void Receipt_ExtractTotalBytesWrong_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "extract_receipt")["total_bytes"] = 1L);

        AssertContractFailure(proof, "receipt extract_receipt.total_bytes must be 205266077");
    }

    [Fact]
    public void Receipt_PrimariesWrongOrder_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => receipt["primaries"] = new JsonArray("P1", "P0", "P2", "P3", "P4"));

        AssertContractFailure(proof, "must equal the selection table order");
    }

    [Fact]
    public void Receipt_AlternatesNoteAltered_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => receipt["alternates_note"] = "alternates may be substituted on failure");

        AssertContractFailure(proof, "receipt alternates_note");
    }

    [Fact]
    public void Receipt_BudgetBWrong_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "operator_policy")["budget_b_game_units"] = 0.05);

        AssertContractFailure(proof, "operator_policy.budget_b_game_units must be");
    }

    [Fact]
    public void Receipt_VerticalAccuracyKeyPresent_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "operator_policy")["vertical_accuracy_meters"] = 1.0);

        AssertContractFailure(proof, "must not carry vertical_accuracy_meters");
    }

    [Fact]
    public void Receipt_UncertaintiesFour_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => receipt["uncertainties"] = new JsonArray("one", "two", "three", "four"));

        AssertContractFailure(proof, "receipt uncertainties must have 5 entries");
    }

    [Fact]
    public void Receipt_OldSeventeenKeyShape_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => receipt.Remove("rgb_control_reference"));

        AssertContractFailure(proof, "preparation receipt must carry exactly the keys");
    }

    [Fact]
    public void Receipt_RgbBlockUnknownKey_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["extra"] = 1);

        AssertContractFailure(proof, "receipt rgb_control_reference must carry exactly the keys");
    }

    [Fact]
    public void Receipt_RgbControlPixelsWrong_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["control_pixels"] = new JsonArray(1, 2));

        AssertContractFailure(proof, "receipt rgb_control_reference.control_pixels must be [960, 2880]");
    }

    [Fact]
    public void Receipt_RgbSourceShaWrong_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["source_sha256"] = ZeroSha);

        AssertContractFailure(proof, "receipt rgb_control_reference.source_sha256");
    }

    [Fact]
    public void Receipt_RgbSourceSizeWrong_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["source_size"] = 1);

        AssertContractFailure(proof, "receipt rgb_control_reference.source_size does not match");
    }

    [Fact]
    public void Receipt_RgbSpacingFiveTenths_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt =>
        {
            var rgb = Child(receipt, "rgb_control_reference");
            rgb["output_spacing_m"] = 0.5;
            rgb["footprint_half_m"] = 0.25;
        });

        AssertContractFailure(proof, "receipt rgb_control_reference.output_spacing_m");
    }

    [Fact]
    public void Receipt_RgbFootprintIncoherent_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt =>
        {
            var rgb = Child(receipt, "rgb_control_reference");
            rgb["output_spacing_m"] = 0.5;
            rgb["footprint_half_m"] = 0.1524;
        });

        AssertContractFailure(proof, "receipt rgb_control_reference.footprint_half_m");
    }

    [Fact]
    public void Receipt_RgbToleranceEight_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["tolerance_bytes"] = 8);

        AssertContractFailure(proof, "receipt rgb_control_reference.tolerance_bytes must be 2");
    }

    [Fact]
    public void Receipt_RgbSourceRoleAltered_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["source_role"] = "altered role");

        AssertContractFailure(proof, "receipt rgb_control_reference.source_role");
    }

    [Fact]
    public void Receipt_RgbRoundingAltered_Refuses()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "rgb_control_reference")["rounding"] = "truncate toward zero");

        AssertContractFailure(proof, "receipt rgb_control_reference.rounding");
    }

    [Fact]
    public void Load_EighteenKeyReceipt_HappyPath_Accepts()
    {
        var proof = BuildValidTree();

        using var loadout = SedanProofArtifacts.Load(proof);

        Assert.NotNull(loadout);
    }

    [Fact]
    public void Receipt_OriginMetersZero_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(receipt, "vertical_policy")["originMeters"] = 0.0);

        AssertContractFailure(proof, "receipt vertical_policy.originMeters must be non-zero by construction");
    }

    [Fact]
    public void Receipt_OutputPathOutsideRoot_Throws()
    {
        var proof = BuildValidTree();
        MutateReceipt(proof, receipt => Child(Child(receipt, "outputs"), "dem_tif")["path"] = "/elsewhere/sedan-dem-32631.tif");

        AssertContractFailure(proof, "receipt outputs.dem_tif.path");
    }

    [Fact]
    public void Prep_SymlinkedLedger_Throws()
    {
        var proof = BuildValidTree();
        var ledger = ProofLedgerPath(proof);
        var target = Path.Combine(_workRoot, "ledger-target.jsonl");
        File.Move(ledger, target);
        File.CreateSymbolicLink(ledger, target);

        AssertContractFailure(proof, "is a symlink; proof artifacts must be real files under the proof root");
    }

    [Fact]
    public void Prep_MissingPreviewTextures_Throws()
    {
        var proof = BuildValidTree();
        File.Delete(Path.Combine(proof, SedanProofArtifacts.PreviewTexturesRelativePath));

        AssertContractFailure(proof, SedanProofArtifacts.PreviewTexturesRelativePath);
    }

    [Fact]
    public void Prep_WrongTextureBinding_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { GrassTexture = "GrassType2" });

        AssertContractFailure(proof, "preview-textures.json grass");
    }

    [Fact]
    public void Manifest_VerticalToleranceOnePointFive_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { VerticalTolerance = 1.5 });

        AssertContractFailure(proof, "found 1.5");
    }

    [Fact]
    public void Manifest_ImageryToleranceEight_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { ImageryTolerance = 8 });

        AssertContractFailure(proof, "imageryToleranceBytes must be exactly 2");
    }

    [Fact]
    public void Manifest_AllZeroImageryRgb_Throws()
    {
        var proof = BuildValidTree(prepared: new PreparedSpec { ImageryRgb = new[] { 0, 0, 0 } });

        AssertContractFailure(proof, "imageryRgb is all zero (placeholder)");
    }

    [Fact]
    public void Manifest_SourceAccuracyKeyOnExtent_Throws()
    {
        var proof = BuildValidTree();
        MutateManifest(proof, manifest => manifest["sources"]!.AsArray()[0]!.AsObject()["verticalAccuracyMeters"] = 0.5);

        AssertContractFailure(proof, "must carry exactly the keys");
    }

    [Fact]
    public void Manifest_AlternateIdReferenced_Throws()
    {
        var proof = BuildValidTree();
        MutateManifest(proof, manifest => manifest["sources"]!.AsArray()[0]!.AsObject()["label"] = "A5");

        AssertContractFailure(proof, "prepared manifest must never reference an alternate id");
    }

    [Fact]
    public void Rank_NonIncreasingPrimaries_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { PrimaryRanks = new[] { 1, 2, 2, 8, 17 } });

        AssertContractFailure(proof, "is not greater than the preceding rank");
    }

    [Fact]
    public void Rank_ZeroPrimaryRank_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { PrimaryRanks = new[] { 0, 2, 4, 8, 17 } });

        AssertContractFailure(proof, "must be at least 1");
    }

    [Fact]
    public void Rank_NonIncreasingAlternates_Throws()
    {
        var proof = BuildValidTree(new FreezeSpec { AlternateRanks = new[] { 9, 3 } });

        AssertContractFailure(proof, "is not greater than the preceding rank");
    }

    private static readonly string ZeroSha = new string('0', 64);

    private static string ProofLedgerPath(string proof)
    {
        return Path.Combine(proof, SedanProofArtifacts.CommandsLedgerRelativePath);
    }

    private static string ProofReceiptPath(string proof)
    {
        return Path.Combine(proof, SedanProofArtifacts.PreparationReceiptRelativePath);
    }

    private static void MutateLedger(string proof, Func<string, string> edit)
    {
        var ledgerPath = ProofLedgerPath(proof);
        File.WriteAllText(ledgerPath, edit(File.ReadAllText(ledgerPath)));
        var commandsSha = Sha256Hex(File.ReadAllBytes(ledgerPath));
        MutateReceipt(proof, receipt => receipt["commands_sha256"] = commandsSha);
    }

    private static void EditLedgerLines(string proof, Action<List<string>> edit)
    {
        MutateLedger(proof, text =>
        {
            var lines = text.TrimEnd('\n').Split('\n').ToList();
            edit(lines);
            return string.Join('\n', lines) + "\n";
        });
    }

    private static void MutateReceiptText(string proof, Func<string, string> edit)
    {
        var receiptPath = ProofReceiptPath(proof);
        File.WriteAllText(receiptPath, edit(File.ReadAllText(receiptPath)));
    }

    private static void ReplaceInReceipt(string proof, string value)
    {
        MutateReceiptText(proof, text => text.Replace(value, ZeroSha, StringComparison.Ordinal));
    }

    private static void MutateReceipt(string proof, Action<JsonObject> edit)
    {
        MutateJson(ProofReceiptPath(proof), edit);
    }

    private static void MutateManifest(string proof, Action<JsonObject> edit)
    {
        var manifestPath = Path.Combine(proof, SedanProofArtifacts.PreparedManifestRelativePath);
        var oldSha = Sha256Hex(File.ReadAllBytes(manifestPath));
        MutateJson(manifestPath, edit);
        var bytes = File.ReadAllBytes(manifestPath);
        var newSha = Sha256Hex(bytes);
        MutateLedger(proof, text => text.Replace(oldSha, newSha, StringComparison.Ordinal));
        MutateReceipt(proof, receipt =>
        {
            var entry = Child(Child(receipt, "outputs"), "manifest");
            entry["sha256"] = newSha;
            entry["size"] = (long)bytes.Length;
        });
    }

    private static void MutateJson(string path, Action<JsonObject> edit)
    {
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(document);
        File.WriteAllText(path, document.ToJsonString(Indented));
    }

    private static JsonObject Child(JsonObject parent, string name)
    {
        return parent[name]!.AsObject();
    }

    private static void AssertContractFailure(string proofRoot, string fragment, SedanProofExpectedFreeze? expected = null)
    {
        var error = Assert.Throws<SedanProofContractException>(() => SedanProofArtifacts.Load(proofRoot, expected).Dispose());
        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    private string BuildValidTree(FreezeSpec? freeze = null, PreparedSpec? prepared = null)
    {
        var proof = Path.Combine(_workRoot, "proof");
        var freezeSpec = freeze ?? new FreezeSpec();
        BuildFreeze(proof, freezeSpec);
        WriteIntegrity(proof, freezeSpec);
        WritePreparation(proof, prepared ?? new PreparedSpec());
        return proof;
    }

    private static void WriteIntegrity(string proof, FreezeSpec spec)
    {
        var manifestBytes = WriteBytes(Path.Combine(proof, "freeze", "lidar-integrity-manifest.json"), "{\"synthetic\": true}\n");

        var receipt = new Dictionary<string, object>
        {
            ["receipt_version"] = 1,
            ["manifest"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/lidar-integrity-manifest.json",
                ["sha256"] = Sha256Hex(manifestBytes),
                ["size"] = manifestBytes.Length,
            },
            ["producer"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/sedan_lidar_integrity.py",
                ["sha256"] = spec.ProducerSha256,
            },
            ["worker"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/sedan_lidar_header_worker.py",
                ["sha256"] = spec.HeaderWorkerSha256,
            },
            ["transport_state"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/state.json",
                ["sha256"] = new string('a', 64),
                ["tiles"] = new Dictionary<string, object>
                {
                    ["LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz"] = new Dictionary<string, object>
                    {
                        ["status"] = "complete",
                        ["expected_size"] = 1L,
                        ["bytes_on_disk"] = 1L,
                        ["sha256"] = new string('b', 64),
                    },
                },
            },
        };

        WriteBytes(Path.Combine(proof, "freeze", "lidar-integrity-producer-receipt.json"), JsonSerializer.Serialize(receipt, Indented));
    }

    private static void BuildFreeze(string proof, FreezeSpec spec)
    {
        var stage = Path.Combine(proof, "freeze", StageName);
        var finalDir = Path.Combine(stage, "final");
        var stateDir = Path.Combine(stage, "state");
        Directory.CreateDirectory(finalDir);
        Directory.CreateDirectory(stateDir);

        var candidatesPath = Path.Combine(finalDir, "candidates.tsv");
        var selectionPath = Path.Combine(finalDir, "selection.tsv");
        var manifestPath = Path.Combine(finalDir, "frozen-manifest.json");
        var receiptPath = Path.Combine(stage, "freeze-receipt.json");
        var statePath = Path.Combine(stateDir, "state.json");

        var candidatesBytes = WriteBytes(candidatesPath, "id\trow\tcol\nP0\t10\t20\n");
        var selectionBytes = WriteBytes(selectionPath, SelectionTsv(spec));
        var candidatesSha = Sha256Hex(candidatesBytes);
        var selectionSha = Sha256Hex(selectionBytes);

        var manifestText = ManifestJson(spec, candidatesSha, selectionSha);
        var manifestBytes = WriteBytes(manifestPath, manifestText);
        var receiptText = ReceiptJson(spec, spec.ReceiptDigestOverride ?? CanonicalDigest(manifestText), candidatesSha, selectionSha);
        var receiptBytes = WriteBytes(receiptPath, receiptText);

        var state = new
        {
            version = 1,
            mode = "freeze",
            artifacts = new
            {
                candidates_tsv = new { path = candidatesPath, sha256 = candidatesSha, size = (long)candidatesBytes.Length },
                selection_tsv = new { path = selectionPath, sha256 = selectionSha, size = (long)selectionBytes.Length },
                frozen_manifest = new { path = manifestPath, sha256 = Sha256Hex(manifestBytes) },
                receipt = new { path = receiptPath, sha256 = Sha256Hex(receiptBytes), size = (long)receiptBytes.Length },
            },
        };
        WriteBytes(statePath, JsonSerializer.Serialize(state, Indented));

        if (spec.WriteCompleted)
        {
            File.WriteAllText(Path.Combine(stage, "COMPLETED"), string.Empty);
        }

        if (spec.ExtraCompletedStage)
        {
            var extra = Path.Combine(proof, "freeze", "sedan-freeze-0002");
            Directory.CreateDirectory(extra);
            File.WriteAllText(Path.Combine(extra, "COMPLETED"), string.Empty);
        }
    }

    private static string SelectionTsv(FreezeSpec spec)
    {
        var lines = new List<string> { string.Join('\t', SelectionColumns) };

        for (var index = 0; index < spec.PrimaryCount + 2; index++)
        {
            var primary = index < spec.PrimaryCount;
            var sector = primary ? spec.PrimarySectors[index] : "S3";
            var id = (primary ? "P" : "A") + index;
            var rank = primary ? spec.PrimaryRanks[index] : spec.AlternateRanks[index - spec.PrimaryCount];
            var truthZ = index == 0 ? spec.FirstPrimaryTruthZ : "10.25";
            var sigma = primary ? "0.012" : string.Empty;
            var slope = primary ? "3.5" : string.Empty;
            var curvature = primary ? "0.01" : string.Empty;

            string[] fields =
            {
                id, primary ? "primary" : "alternate", rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "10", "20", "640000.5", "5507000.5", "840000.5", "6958000.5",
                "12.5", "34.5", "49.7", "4.9",
                truthZ, sigma, slope, curvature, "1.5",
                "3", "2", "1", "0", "0", "0", "0",
                "S1H", sector,
            };
            lines.Add(string.Join('\t', fields));
        }

        return string.Join('\n', lines) + "\n";
    }

    private static string ManifestJson(FreezeSpec spec, string candidatesSha, string selectionSha)
    {
        var constants = new Dictionary<string, object>
        {
            ["window_r"] = 2.5,
            ["half_footprint"] = 1.524,
            ["lattice"] = 3.048,
            ["e1_n2"] = 12,
            ["e23_dist"] = 3.048,
            ["e4_sigma"] = spec.E4Sigma,
            ["e5_slope_deg"] = 12.0,
            ["e6_seam"] = 3.048,
            ["e7_curv"] = 0.05,
            ["huber_c"] = 1.345,
            ["mad_scale"] = 1.4826,
            ["s_floor"] = 1e-9,
            ["s1_hard_sigma"] = 0.03,
            ["strata_bounds_deg"] = new object[] { 4.0, 8.0 },
            ["min_primary"] = 5,
            ["min_sectors"] = 3,
            ["min_spacing_m"] = 100.0,
            ["n_alternates"] = 2,
            ["seam_lines_l93"] = new object[]
            {
                new object[] { "E", 840000.0 },
                new object[] { "E", 841000.0 },
                new object[] { "N", 6958000.0 },
            },
        };

        var primaryIds = Enumerable.Range(0, spec.PrimaryCount).Select(index => "P" + index).ToArray();
        var alternateIds = new[] { "A" + spec.PrimaryCount, "A" + (spec.PrimaryCount + 1) };
        var sectors = spec.PrimarySectors.Take(spec.PrimaryCount).Distinct().ToArray();

        var manifest = new Dictionary<string, object>
        {
            ["protocol_version"] = "1.2",
            ["scope"] = spec.Scope,
            ["constants"] = constants,
            ["map_transform"] = new Dictionary<string, object> { ["projected_crs"] = "EPSG:32631" },
            ["tables"] = new Dictionary<string, object>
            {
                ["candidates_sha256"] = candidatesSha,
                ["selection_sha256"] = selectionSha,
            },
            ["python_version"] = "3.12.3",
            ["generated_at"] = "2026-11-03T10:00:00+01:00",
            ["selection"] = new Dictionary<string, object>
            {
                ["primary_ids"] = primaryIds,
                ["alternate_ids"] = alternateIds,
                ["sectors_covered"] = sectors,
            },
            ["note"] = "synthetic test tree",
        };

        return JsonSerializer.Serialize(manifest, Indented);
    }

    private static string ReceiptJson(FreezeSpec spec, string canonicalDigest, string candidatesSha, string selectionSha)
    {
        var receipt = new Dictionary<string, object>
        {
            ["receipt_version"] = 1,
            ["generated_at"] = "2026-11-03T21:29:31.672-05:00",
            ["tool"] = "sedan_lidar_freeze.py",
            ["protocol_version"] = "1.2",
            ["worker"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/sedan_lidar_probe_worker_banded.py",
                ["sha256"] = spec.BandedWorkerSha256,
                ["source"] = "manifest override",
                ["approved_registry"] = new[] { spec.BaseWorkerSha256, spec.BandedWorkerSha256 },
                ["base_worker_path"] = "synthetic/sedan_lidar_probe_worker.py",
                ["base_worker_sha256"] = spec.BaseWorkerSha256,
                ["select_module_sha256"] = spec.SelectModuleSha256,
            },
            ["operator_policy"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic",
                ["sha256"] = SedanProofArtifacts.PolicySha256,
            },
            ["budget_a"] = new Dictionary<string, object>
            {
                ["limit_m"] = 1.0,
                ["escalation"] = "a documented actual accuracy above 1.0 m escalates before any DEM comparison; the operator limit is never silently raised",
            },
            ["budget_b"] = new Dictionary<string, object>
            {
                ["limit_game_units"] = spec.BudgetBLimitGameUnits,
            },
            ["carried_unresolved"] = spec.CarriedUnresolved,
            ["numeric_proof_pass"] = spec.NumericProofPass,
            ["provenance_hash_chain"] = new Dictionary<string, object>
            {
                ["candidates_sha256"] = candidatesSha,
                ["selection_sha256"] = selectionSha,
                ["canonical_digest"] = canonicalDigest,
            },
        };

        return JsonSerializer.Serialize(receipt, Indented);
    }

    private const string FreezeVerifiedAtText = "2026-11-03T10:00:00.000-05:00";
    private const string FirstDemRasterIoAtText = "2026-11-03T10:05:00.000-05:00";
    private const string PreparationCompletedAtText = "2026-11-03T10:09:00.000-05:00";
    private const string AlternatesNoteText = "recorded only; NEVER activated (no failure-based substitution)";
    private const string ExtentAttribution = "generated extent KML (synthetic)";
    private const string ImageryAttribution = "IGN, Licence Ouverte 2.0 (synthetic)";
    private const string DemSourceDatum = "NGF-IGN69";

    private static readonly string[] ImageryNames = { "rgb-quarter-centre", "rgb-three-quarter-centre" };
    private static readonly string[] PreparationOutputLabels = { "manifest", "texture_map", "sources_txt", "extent_kml", "dem_tif", "rgb_tif" };
    private static readonly string[] LazTileNames =
    {
        "synthetic-tile-1.copc.laz", "synthetic-tile-2.copc.laz", "synthetic-tile-3.copc.laz",
        "synthetic-tile-4.copc.laz", "synthetic-tile-5.copc.laz", "synthetic-tile-6.copc.laz",
    };

    private sealed record FreezeDigests(
        string Stage,
        string CanonicalDigest,
        string FrozenManifestSha,
        string ReceiptSha,
        string IntegritySha,
        string CandidatesSha,
        string SelectionSha);

    private static FreezeDigests ReadFreezeDigests(string root)
    {
        var stage = Path.GetFullPath(Path.Combine(root, "freeze", StageName));
        var finalDir = Path.Combine(stage, "final");
        var manifestBytes = File.ReadAllBytes(Path.Combine(finalDir, "frozen-manifest.json"));

        return new FreezeDigests(
            stage,
            CanonicalDigest(Encoding.UTF8.GetString(manifestBytes)),
            Sha256Hex(manifestBytes),
            Sha256Hex(File.ReadAllBytes(Path.Combine(stage, "freeze-receipt.json"))),
            Sha256Hex(File.ReadAllBytes(Path.Combine(root, "freeze", "lidar-integrity-manifest.json"))),
            Sha256Hex(File.ReadAllBytes(Path.Combine(finalDir, "candidates.tsv"))),
            Sha256Hex(File.ReadAllBytes(Path.Combine(finalDir, "selection.tsv"))));
    }

    private static void WritePreparation(string proof, PreparedSpec spec)
    {
        var root = Path.GetFullPath(proof);
        var digests = ReadFreezeDigests(root);
        Directory.CreateDirectory(Path.Combine(root, "prepared"));
        Directory.CreateDirectory(Path.Combine(root, "preparation"));

        var relatives = new[]
        {
            SedanProofArtifacts.PreparedManifestRelativePath, SedanProofArtifacts.PreviewTexturesRelativePath,
            SedanProofArtifacts.SourcesTextRelativePath, SedanProofArtifacts.ExtentKmlRelativePath,
            SedanProofArtifacts.DemRelativePath, SedanProofArtifacts.RgbRelativePath,
        };
        var contents = new[]
        {
            PreparedManifestText(spec), $"{{\"grass\":\"{spec.GrassTexture}\"}}",
            "# Sedan operator-proof runtime input sources (synthetic)\n",
            "<kml>synthetic extent</kml>\n", "synthetic dem raster\n", "synthetic rgb raster\n",
        };

        var outputs = new Dictionary<string, object>(StringComparer.Ordinal);
        var completedOutputs = new Dictionary<string, object>(StringComparer.Ordinal);

        for (var index = 0; index < PreparationOutputLabels.Length; index++)
        {
            var path = Path.Combine(root, relatives[index]);
            var bytes = WriteBytes(path, contents[index]);
            var sha = Sha256Hex(bytes);
            outputs[PreparationOutputLabels[index]] = new Dictionary<string, object>
            {
                ["path"] = path,
                ["sha256"] = sha,
                ["size"] = (long)bytes.Length,
            };
            completedOutputs[PreparationOutputLabels[index]] = new Dictionary<string, object> { ["sha256"] = sha };
        }

        var demSize = new FileInfo(Path.Combine(root, SedanProofArtifacts.DemRelativePath)).Length;
        var rgbSize = new FileInfo(Path.Combine(root, SedanProofArtifacts.RgbRelativePath)).Length;

        var demArgv = new[] { "gdalwarp", "-t_srs", "EPSG:32631", "synthetic-source.tif", SedanProofArtifacts.DemRelativePath };
        var baselineLines = new[]
        {
            Line(new
            {
                @event = "freeze_validated_complete",
                freezeVerifiedAt = FreezeVerifiedAtText,
                freezeReceiptSha256 = digests.ReceiptSha,
                integrityManifestSha256 = digests.IntegritySha,
                selectionTables = new
                {
                    candidates_sha256 = digests.CandidatesSha,
                    selection_sha256 = digests.SelectionSha,
                    frozen_manifest_sha256 = digests.FrozenManifestSha,
                    canonical_digest = digests.CanonicalDigest,
                },
            }),
            Line(Invoked("gdalinfo", new[] { "gdalinfo", "synthetic-source.tif" }, heightSemantic: false)),
            Line(new { @event = "dem_rasterio_start", firstDemRasterIoAt = FirstDemRasterIoAtText, argv = demArgv }),
            Line(Invoked("gdalwarp", demArgv, heightSemantic: true)),
            Line(new { @event = "preparation_completed", completedAt = PreparationCompletedAtText, outputs = completedOutputs }),
        };
        var ledgerLines = spec.MultiAttempt ? MultiAttemptLedgerLines(digests, completedOutputs, demArgv) : baselineLines;
        var ledgerBytes = WriteBytes(Path.Combine(root, SedanProofArtifacts.CommandsLedgerRelativePath), string.Join('\n', ledgerLines) + "\n");

        var tiles = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var tile in LazTileNames)
        {
            tiles[tile] = Sha256Hex(Encoding.UTF8.GetBytes(tile));
        }

        var receipt = new Dictionary<string, object>
        {
            ["receipt_version"] = 1,
            ["tool"] = "sedan_prepare_inputs.py",
            ["generated_at"] = PreparationCompletedAtText,
            ["cache_root"] = root,
            ["stage"] = digests.Stage,
            ["freeze"] = new Dictionary<string, object>
            {
                ["stage"] = digests.Stage,
                ["canonical_digest"] = digests.CanonicalDigest,
                ["frozen_manifest_sha256"] = digests.FrozenManifestSha,
                ["receipt_sha256"] = digests.ReceiptSha,
                ["integrity_manifest_sha256"] = digests.IntegritySha,
                ["candidates_sha256"] = digests.CandidatesSha,
                ["selection_sha256"] = digests.SelectionSha,
                ["laz_tiles_sha256"] = tiles,
            },
            ["extract_receipt"] = new Dictionary<string, object>
            {
                ["path"] = "synthetic/extract-receipt.json",
                ["sha256"] = new string('c', 64),
                ["members"] = SedanProofArtifacts.ExtractMembers,
                ["total_bytes"] = SedanProofArtifacts.ExtractTotalBytes,
            },
            ["primaries"] = PrimaryIds,
            ["alternates"] = AlternateIds,
            ["alternates_note"] = AlternatesNoteText,
            ["operator_policy"] = new Dictionary<string, object>
            {
                ["vertical_tolerance_meters"] = 1.0,
                ["vertical_tolerance_scope"] = "engineering-only acceptance, never a certified source accuracy",
                ["budget_b_game_units"] = 0.0390625,
                ["vertical_datum_label"] = DemSourceDatum,
                ["vertical_datum_note"] = "label only: NO EPSG:5720 geokey, no vertical conversion, no vertical shift",
                ["vertical_accuracy_note"] = "verticalAccuracyMeters ABSENT: actual tile-specific source accuracy is unverified; a documented actual accuracy above 1.0 m escalates before any comparison",
                ["no_data_fill_policy"] = "nearest-valid within 3.048 m (one height lattice); edge fail; radius is pinned and never silently widened",
            },
            ["uncertainties"] = new[] { "uncertainty one", "uncertainty two", "uncertainty three", "uncertainty four", "uncertainty five" },
            ["rgb_control_reference"] = new Dictionary<string, object>
            {
                ["source_role"] = "operator-input prepared 0.2 m RGB (not the raw JP2, not the payload)",
                ["source_path"] = SedanProofArtifacts.RgbRelativePath,
                ["source_sha256"] = Sha256Hex(File.ReadAllBytes(Path.Combine(root, SedanProofArtifacts.RgbRelativePath))),
                ["source_size"] = rgbSize,
                ["pixels_per_axis"] = 3840,
                ["control_pixels"] = new[] { 960, 2880 },
                ["output_spacing_m"] = 0.3048,
                ["footprint_half_m"] = 0.1524,
                ["rounding"] = "clamp to [0, 255], then round half away from zero (Core ToByte)",
                ["tolerance_bytes"] = 2,
            },
            ["vertical_policy"] = new Dictionary<string, object>
            {
                ["originMeters"] = spec.OriginMeters,
                ["source"] = "prepared DEM extent minimum (gdalinfo -stats), never an assumed 0",
            },
            ["chronology"] = new Dictionary<string, object>
            {
                ["freezeVerifiedAt"] = spec.MultiAttempt ? MultiCurrentFreezeText : FreezeVerifiedAtText,
                ["firstDemRasterIoAt"] = spec.MultiAttempt ? MultiCurrentDemText : FirstDemRasterIoAtText,
                ["note"] = "code order guarantees freezeVerifiedAt < firstDemRasterIoAt; no hash certifies historic blindness",
            },
            ["space_estimate"] = new Dictionary<string, object>
            {
                ["dem_bytes"] = demSize,
                ["rgb_bytes"] = rgbSize,
                ["total_bytes"] = demSize + rgbSize,
            },
            ["outputs"] = outputs,
            ["commands_sha256"] = Sha256Hex(ledgerBytes),
        };

        WriteBytes(Path.Combine(root, SedanProofArtifacts.PreparationReceiptRelativePath), JsonSerializer.Serialize(receipt, Indented));
    }

    private const string MultiFirstFreezeText = "2026-03-01T10:21:56.621-05:00";
    private const string MultiSecondFreezeText = "2026-03-01T10:39:04.905-05:00";
    private const string MultiFailedDemText = "2026-03-01T10:40:01.753-05:00";
    private const string MultiCurrentFreezeText = "2026-03-01T11:05:32.733-05:00";
    private const string MultiCurrentDemText = "2026-03-01T11:06:09.949-05:00";

    private static string[] MultiAttemptLedgerLines(FreezeDigests digests, Dictionary<string, object> completedOutputs, string[] demArgv)
    {
        var failedArgv = new[] { "gdalwarp", "-t_srs", "EPSG:32631", "synthetic-failed-source.tif", SedanProofArtifacts.DemRelativePath };
        var lines = new List<string> { FreezeEventLine(digests, MultiFirstFreezeText) };

        for (var index = 0; index < 15; index++)
        {
            lines.Add(Line(Invoked("gdalinfo", new[] { "gdalinfo", $"synthetic-attempt-one-{index}.tif" }, heightSemantic: false)));
        }

        lines.Add(FreezeEventLine(digests, MultiSecondFreezeText));
        lines.Add(Line(new { @event = "dem_rasterio_start", firstDemRasterIoAt = MultiFailedDemText, argv = failedArgv }));

        for (var index = 0; index < 16; index++)
        {
            lines.Add(index is 1 or 7
                ? Line(Invoked("gdalwarp", failedArgv, heightSemantic: true))
                : Line(Invoked("gdalinfo", new[] { "gdalinfo", $"synthetic-attempt-two-{index}.tif" }, heightSemantic: false)));
        }

        lines.Add(FreezeEventLine(digests, MultiCurrentFreezeText));
        lines.Add(Line(new { @event = "dem_rasterio_start", firstDemRasterIoAt = MultiCurrentDemText, argv = demArgv }));

        for (var index = 0; index < 26; index++)
        {
            lines.Add(index is 0 or 13
                ? Line(Invoked("gdalwarp", demArgv, heightSemantic: true))
                : Line(Invoked("gdalinfo", new[] { "gdalinfo", $"synthetic-attempt-three-{index}.tif" }, heightSemantic: false)));
        }

        // Shares the current dem millisecond: equal adjacent timestamps must be accepted.
        lines.Add(Line(new { @event = "preparation_completed", completedAt = MultiCurrentDemText, outputs = completedOutputs }));

        return lines.ToArray();
    }

    private static string FreezeEventLine(FreezeDigests digests, string freezeVerifiedAt)
    {
        return Line(new
        {
            @event = "freeze_validated_complete",
            freezeVerifiedAt,
            freezeReceiptSha256 = digests.ReceiptSha,
            integrityManifestSha256 = digests.IntegritySha,
            selectionTables = new
            {
                candidates_sha256 = digests.CandidatesSha,
                selection_sha256 = digests.SelectionSha,
                frozen_manifest_sha256 = digests.FrozenManifestSha,
                canonical_digest = digests.CanonicalDigest,
            },
        });
    }

    private static string Line(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static object Invoked(string tool, string[] argv, bool heightSemantic)
    {
        return new
        {
            @event = "tool_invoked",
            tool,
            argv,
            timeoutSeconds = 600,
            heightSemantic,
            returncode = 0,
            timedOut = false,
            stdoutBytes = 0,
            stdoutSha256 = Sha256Hex(Array.Empty<byte>()),
            stderrBytes = 0,
            stderrSha256 = Sha256Hex(Array.Empty<byte>()),
        };
    }

    private static string PreparedManifestText(PreparedSpec spec)
    {
        var extent = new Dictionary<string, object>
        {
            ["id"] = "sedan-extent",
            ["kind"] = "kml",
            ["path"] = SedanProofArtifacts.ExtentKmlRelativePath,
            ["role"] = "extent",
            ["label"] = "synthetic extent",
            ["attribution"] = ExtentAttribution,
        };
        var dem = new Dictionary<string, object>
        {
            ["id"] = "sedan-dem-32631",
            ["kind"] = "geotiff",
            ["path"] = SedanProofArtifacts.DemRelativePath,
            ["role"] = "elevation",
            ["verticalDatum"] = DemSourceDatum,
            ["label"] = "synthetic elevation",
            ["attribution"] = ImageryAttribution,
        };

        if (spec.AccuracyMeters is double accuracy)
        {
            dem["verticalAccuracyMeters"] = accuracy;
        }

        var ortho = new Dictionary<string, object>
        {
            ["id"] = "sedan-ortho-32631",
            ["kind"] = "geotiff",
            ["path"] = SedanProofArtifacts.RgbRelativePath,
            ["role"] = "imagery",
            ["label"] = "synthetic imagery",
            ["attribution"] = ImageryAttribution,
        };

        var controlPoints = new List<Dictionary<string, object>>();

        for (var index = 0; index < PrimaryIds.Length; index++)
        {
            controlPoints.Add(new Dictionary<string, object>
            {
                ["name"] = index == 0 ? spec.FirstVerticalName : PrimaryIds[index],
                ["latitude"] = 10.0 + (index * 0.001),
                ["longitude"] = 20.0 + (index * 0.001),
                ["elevationMeters"] = 150.25 + index,
            });
        }

        for (var index = 0; index < ImageryNames.Length; index++)
        {
            controlPoints.Add(new Dictionary<string, object>
            {
                ["name"] = ImageryNames[index],
                ["latitude"] = 10.5 + (index * 0.001),
                ["longitude"] = 20.5 + (index * 0.001),
                ["imageryRgb"] = spec.ImageryRgb,
                ["imageryToleranceBytes"] = spec.ImageryTolerance,
            });
        }

        var manifest = new Dictionary<string, object>
        {
            ["sources"] = new object[] { extent, dem, ortho },
            ["targetCoordinateSystem"] = new Dictionary<string, object> { ["type"] = "utm", ["zone"] = 31, ["hemisphere"] = "north" },
            ["metersPerGameUnit"] = 0.3048,
            ["defaultMaterial"] = "grass",
            ["extentFeatureId"] = "sedan-extent",
            ["verticalPolicy"] = new Dictionary<string, object> { ["originMeters"] = spec.OriginMeters, ["exaggeration"] = 1.0 },
            ["rasterPolicy"] = new Dictionary<string, object>
            {
                ["noData"] = "nearest-valid",
                ["noDataSearchRadiusMeters"] = 3.048,
                ["edge"] = "fail",
                ["imageryPixelsPerSample"] = 10,
            },
            ["tolerances"] = new Dictionary<string, object> { ["verticalMeters"] = spec.VerticalTolerance },
            ["controlPoints"] = controlPoints,
        };

        return JsonSerializer.Serialize(manifest, Indented);
    }

    private static string CanonicalDigest(string manifestText)
    {
        using var document = JsonDocument.Parse(manifestText);
        return SedanProofArtifacts.CanonicalDigest(document.RootElement);
    }

    private static byte[] WriteBytes(string path, string text)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [Theory]
    [InlineData(0.0049d, true)]
    [InlineData(0.005d, true)]
    [InlineData(0.0051d, false)]
    public void FrameCrosscheckTolerance_IsInclusiveAtTheDeclaredBound(double delta, bool expected)
    {
        Assert.Equal(expected, SedanOperatorProofTests.IsWithinFrameCrosscheckTolerance(delta));
    }

    [Fact]
    public void CollectSiteSamples_VisitsEverySiteAfterFailures()
    {
        var sites = new[]
        {
            SyntheticSite("s1", 10d),
            SyntheticSite("s2", 20d),
            SyntheticSite("s3", 30d),
            SyntheticSite("s4", 40d),
            SyntheticSite("s5", 50d),
        };

        var samples = SedanOperatorProofTests.CollectSiteSamples(
            sites,
            site => site.Id == "s1" ? (0.01d, 0d) : (0d, 0d),
            site => site.Id == "s3" ? (false, 0d, "no height") : (true, 100d + site.GameX, (string?)null));

        Assert.Equal(5, samples.Count);

        Assert.StartsWith("crosscheck", samples[0].FailureReason);
        Assert.Null(samples[0].PayloadZ);

        AssertSampled(samples[1]);

        Assert.Equal("no height", samples[2].FailureReason);
        Assert.Null(samples[2].PayloadZ);

        AssertSampled(samples[3]);
        AssertSampled(samples[4]);

        var message = SedanOperatorProofTests.DescribeSampleFailures(samples);
        Assert.NotNull(message);
        Assert.Contains("failed for s1, s3 (2 of 5 primaries)", message);
    }

    private static SedanProofSite SyntheticSite(string id, double gameX)
    {
        return new SedanProofSite(
            id, "primary", 1, 0, 0, 0d, 0d, 0d, 0d, gameX, 0d, 0d, 0d, 0d, null, null, null, 0d, "stratum", "sector");
    }

    private static void AssertSampled(SedanOperatorProofTests.SedanProofSiteSample sample)
    {
        Assert.Null(sample.FailureReason);
        Assert.True(double.IsFinite(sample.PayloadZ ?? double.NaN));
    }

    private sealed class FreezeSpec
    {
        public string Scope { get; set; } = SedanProofArtifacts.FinalScope;

        public double E4Sigma { get; set; } = 0.08;

        public int PrimaryCount { get; set; } = 5;

        public string[] PrimarySectors { get; set; } = new[] { "S1", "S2", "S3", "S1", "S2" };

        public int[] PrimaryRanks { get; set; } = new[] { 1, 2, 4, 8, 17 };

        public int[] AlternateRanks { get; set; } = new[] { 3, 9 };

        public string FirstPrimaryTruthZ { get; set; } = "12.25";

        public bool WriteCompleted { get; set; } = true;

        public bool ExtraCompletedStage { get; set; }

        public string? ReceiptDigestOverride { get; set; }

        public string ProducerSha256 { get; set; } = "9fc6bacc4fe5810d4b962c64e43242093abf1a842c2a26f79df7c1d735a0f545";

        public string HeaderWorkerSha256 { get; set; } = "7688e5d3daa36534e7b1e9c1be6e831e2c69106a980e6bb688f7a35b01af4696";

        public string BandedWorkerSha256 { get; set; } = "52e935b5360c0d10563a581e650df3ac1637512eb30bc2cfc953d59cf12d2ab0";

        public string BaseWorkerSha256 { get; set; } = "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da";

        public string SelectModuleSha256 { get; set; } = "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1";

        public double BudgetBLimitGameUnits { get; set; } = 0.0390625;

        public bool NumericProofPass { get; set; }

        public string[] CarriedUnresolved { get; set; } =
        {
            "RGE ALTI tile-specific accuracy UNKNOWN",
            "2024 auto-classification bias",
            "2012-2016 versus 2024 epoch mismatch",
        };
    }

    private sealed class PreparedSpec
    {
        public double? AccuracyMeters { get; set; }

        public double VerticalTolerance { get; set; } = 1.0;

        public string FirstVerticalName { get; set; } = "P0";

        public double OriginMeters { get; set; } = 12.5;

        public string GrassTexture { get; set; } = "GrassType1";

        public int ImageryTolerance { get; set; } = 2;

        public int[] ImageryRgb { get; set; } = new[] { 10, 20, 30 };

        public bool MultiAttempt { get; set; }
    }
}
