using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Panopticon.Tests.Tools.GeoImport;

/// <summary>Operator-facing contract violation while reading the Sedan proof artifacts.</summary>
public sealed class SedanProofContractException : Exception
{
    public SedanProofContractException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Consumer contract for the final all-tiles Sedan freeze written by
/// <c>scripts/geo/sedan_lidar_freeze.py</c> and for the prepared proof manifest.
/// <see cref="Load"/> throws <see cref="SedanProofContractException"/> on every violation and
/// never returns a partial result.
/// </summary>
public sealed class SedanProofArtifacts
{
    /// <summary>
    /// PROPOSED contract for the future prepare owner: the prepared manifest location under
    /// the proof root. Not yet produced by any accepted tool.
    /// </summary>
    public const string PreparedManifestRelativePath = "sedan-ign-proof.json";

    public const string FreezeDirName = "freeze";
    public const string StagePrefix = "sedan-freeze-";
    public const string CompletedMarkerName = "COMPLETED";
    public const string StateRelativePath = "state/state.json";
    public const string PolicySha256 = "d4e25a8f94da4f3228ed22377bb738881be50f9e20342af10bf595c675d57cbc";
    public const string FinalProtocolVersion = "1.2";
    public const string FinalScope = "final all-tiles selection freeze";

    public const string PreparationReceiptRelativePath = "preparation/receipt.json";
    public const string CommandsLedgerRelativePath = "preparation/commands.jsonl";
    public const string PreviewTexturesRelativePath = "preview-textures.json";
    public const string SourcesTextRelativePath = "sources.txt";
    public const string ExtentKmlRelativePath = "prepared/sedan-extent.kml";
    public const string DemRelativePath = "prepared/sedan-dem-32631.tif";
    public const string RgbRelativePath = "prepared/sedan-ortho-32631.tif";
    public const int ExtractMembers = 27;
    public const long ExtractTotalBytes = 205266077L;

    public const string IntegrityProducerReceiptRelativePath = "freeze/lidar-integrity-producer-receipt.json";
    public const string IntegrityManifestRelativePath = "freeze/lidar-integrity-manifest.json";
    public const string IntegrityProducerSha256 = "9fc6bacc4fe5810d4b962c64e43242093abf1a842c2a26f79df7c1d735a0f545";
    public const string HeaderWorkerSha256 = "7688e5d3daa36534e7b1e9c1be6e831e2c69106a980e6bb688f7a35b01af4696";
    public const string BandedWorkerSha256 = "52e935b5360c0d10563a581e650df3ac1637512eb30bc2cfc953d59cf12d2ab0";
    public const string BaseWorkerSha256 = "5db920802e4a2da484aee02b598774bb40eb8e5152ed6328f5ff711fe0d3a6da";
    public const string SelectModuleSha256 = "4570c3507c6fb99442af0691f010a7406f64306622587abce140728873e913a1";

    private const string ToolName = "sedan_lidar_freeze.py";
    private const string ReceiptDigestPath = "provenance_hash_chain.canonical_digest";
    private const double FloatTolerance = 1e-12;
    private const double OperatorVerticalLimitMeters = 1.0;
    private const double BudgetBLimitGameUnits = 0.0390625;
    private const string BudgetAEscalationFragment = "escalates before any DEM comparison";
    private const string ExamplePlaceholderMarker = "EXAMPLE ONLY";

    private static readonly string[] CarriedUnresolvedFragments = { "accuracy UNKNOWN", "auto-classification bias", "epoch mismatch" };

    // Column order of _SEL_COLUMNS in scripts/geo/sedan_lidar_select.py (lines 650-655).
    private static readonly string[] SelectionColumns =
    {
        "id", "role", "rank", "row", "col", "utm_x", "utm_y", "l93_x", "l93_y",
        "game_x", "game_y", "wgs84_lat", "wgs84_lon",
        "truth_z", "sigma_fit", "slope_deg", "curv_bound", "density",
        "n2", "n3", "n4", "n5", "n6", "n9", "n17", "stratum", "sector",
    };

    // Frozen protocol constants transcribed from scripts/geo/sedan_lidar_select.py lines 40-115.
    private static readonly Dictionary<string, object> ExpectedConstants = new(StringComparer.Ordinal)
    {
        ["window_r"] = 2.5,
        ["half_footprint"] = 1.524,
        ["lattice"] = 3.048,
        ["e1_n2"] = 12L,
        ["e23_dist"] = 3.048,
        ["e4_sigma"] = 0.08,
        ["e5_slope_deg"] = 12.0,
        ["e6_seam"] = 3.048,
        ["e7_curv"] = 0.05,
        ["huber_c"] = 1.345,
        ["mad_scale"] = 1.4826,
        ["s_floor"] = 1e-9,
        ["s1_hard_sigma"] = 0.03,
        ["strata_bounds_deg"] = new object[] { 4.0, 8.0 },
        ["min_primary"] = 5L,
        ["min_sectors"] = 3L,
        ["min_spacing_m"] = 100.0,
        ["n_alternates"] = 2L,
        ["seam_lines_l93"] = new object[]
        {
            new object[] { "E", 840000.0 },
            new object[] { "E", 841000.0 },
            new object[] { "N", 6958000.0 },
        },
    };

    private const int ExpectedPrimaryCount = 5;
    private const int ExpectedAlternateCount = 2;
    private const int ExpectedVerticalControlPoints = 5;
    private const int ExpectedImageryControlPoints = 2;
    private const int MinSectors = 3;
    private const long JsonSizeCapBytes = 2L * 1024 * 1024;
    private const long LedgerSizeCapBytes = 8L * 1024 * 1024;
    private const int LedgerLineCap = 4096;

    private SedanProofArtifacts()
    {
    }

    /// <summary>
    /// Validates the completed freeze and the prepared manifest. The consumer binds the augmented
    /// producer and retains both base and banded worker provenance; the pre-augmentation producer
    /// hash lives only in the external ledger and is never compared here.
    /// </summary>
    public static SedanProofLoadout Load(string proofRoot, SedanProofExpectedFreeze? expected = null)
    {
        if (string.IsNullOrWhiteSpace(proofRoot))
        {
            throw new SedanProofContractException("PANOPTICON_GEO_PROOF_DIR is empty; set it to the proof root");
        }

        var root = Path.GetFullPath(proofRoot);

        if (!Directory.Exists(root))
        {
            throw new SedanProofContractException($"proof root does not exist: {root}");
        }

        if (IsSymlink(root))
        {
            throw new SedanProofContractException($"proof root must not be a symlink: {root}");
        }

        var stage = FindCompletedStage(root);
        var ledger = ReadLedger(stage);

        var candidatesSha = VerifyLedgerArtifact("candidates_tsv", ledger);
        var selectionSha = VerifyLedgerArtifact("selection_tsv", ledger);
        var frozenManifestSha = VerifyLedgerArtifact("frozen_manifest", ledger);
        var freezeReceiptSha = VerifyLedgerArtifact("receipt", ledger);

        var selection = ParseSelection(ledger["selection_tsv"].Path);
        var manifestDigest = VerifyFrozenManifest(ledger["frozen_manifest"].Path, candidatesSha, selectionSha, selection);
        VerifyReceipt(ledger["receipt"].Path, manifestDigest, candidatesSha, selectionSha);
        VerifyIntegrityEvidence(root);
        VerifyExpectedFreeze(expected, manifestDigest, selection);

        var integritySha = Sha256File(Path.Combine(root, IntegrityManifestRelativePath));
        var freeze = new FreezeIdentity(stage, candidatesSha, selectionSha, frozenManifestSha, freezeReceiptSha,
            integritySha, manifestDigest);
        var preparation = VerifyPreparation(root, freeze, selection);

        var preparedPath = Path.Combine(root, PreparedManifestRelativePath);
        var prepared = VerifyPreparedManifest(preparedPath, preparation, selection);

        return new SedanProofLoadout(root, stage, ledger["selection_tsv"].Path, ledger["frozen_manifest"].Path,
            ledger["receipt"].Path, preparedPath,
            Path.Combine(root, IntegrityProducerReceiptRelativePath), Path.Combine(root, IntegrityManifestRelativePath),
            candidatesSha, selectionSha, manifestDigest,
            selection.Primaries, selection.Alternates, new[] { BaseWorkerSha256, BandedWorkerSha256 },
            preparation.ReceiptPath, preparation.LedgerPath, preparation.PreviewTexturesPath,
            preparation.OriginMeters, preparation.FreezeVerifiedAt, preparation.FirstDemRasterIoAt, prepared);
    }

    private static string FindCompletedStage(string root)
    {
        var freezeRoot = Path.Combine(root, FreezeDirName);
        var completed = new List<string>();

        if (Directory.Exists(freezeRoot) && !IsSymlink(freezeRoot))
        {
            foreach (var entry in Directory.EnumerateDirectories(freezeRoot))
            {
                var name = Path.GetFileName(entry);

                if (!name.StartsWith(StagePrefix, StringComparison.Ordinal) || IsSymlink(entry))
                {
                    continue;
                }

                if (File.Exists(Path.Combine(entry, CompletedMarkerName)))
                {
                    completed.Add(entry);
                }
            }
        }

        if (completed.Count == 0)
        {
            throw new SedanProofContractException(
                $"no completed final freeze under {freezeRoot}; run the final freeze before the proof");
        }

        if (completed.Count > 1)
        {
            completed.Sort(StringComparer.Ordinal);
            throw new SedanProofContractException(
                $"multiple completed freezes under {freezeRoot}; resolve which one stands manually: "
                + string.Join(", ", completed));
        }

        return completed[0];
    }

    private static JsonDocument ReadState(string stage)
    {
        var statePath = Path.Combine(stage, StateRelativePath);

        if (!File.Exists(statePath) || IsSymlink(statePath))
        {
            throw new SedanProofContractException($"freeze state missing or not a regular file: {statePath}");
        }

        var document = ParseJson(statePath);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new SedanProofContractException($"freeze state is not a JSON object: {statePath}");
        }

        return document;
    }

    private static Dictionary<string, LedgerArtifact> ReadLedger(string stage)
    {
        using var state = ReadState(stage);
        var statePath = Path.Combine(stage, StateRelativePath);

        if (!state.RootElement.TryGetProperty("artifacts", out var artifacts)
            || artifacts.ValueKind != JsonValueKind.Object)
        {
            throw new SedanProofContractException($"freeze state carries no artifacts ledger: {statePath}");
        }

        var ledger = new Dictionary<string, LedgerArtifact>(StringComparer.Ordinal);

        foreach (var label in new[] { "candidates_tsv", "selection_tsv", "frozen_manifest", "receipt" })
        {
            if (!artifacts.TryGetProperty(label, out var artifact) || artifact.ValueKind != JsonValueKind.Object)
            {
                throw new SedanProofContractException($"freeze state artifact {label} is missing or malformed");
            }

            if (!artifact.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String
                || !artifact.TryGetProperty("sha256", out var shaElement) || shaElement.ValueKind != JsonValueKind.String)
            {
                throw new SedanProofContractException($"freeze state artifact {label} is missing a string path or sha256");
            }

            long? size = null;

            if (artifact.TryGetProperty("size", out var sizeElement))
            {
                if (sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out var parsed))
                {
                    throw new SedanProofContractException($"freeze state artifact {label} size is not an integer");
                }

                size = parsed;
            }

            // Path.Combine keeps absolute ledger paths unchanged, matching reverify_completed.
            var path = Path.Combine(stage, pathElement.GetString()!);
            ledger[label] = new LedgerArtifact(path, shaElement.GetString()!, size);
        }

        return ledger;
    }

    private static string VerifyLedgerArtifact(string label, Dictionary<string, LedgerArtifact> ledger)
    {
        var artifact = ledger[label];

        if (IsSymlink(artifact.Path) || !File.Exists(artifact.Path))
        {
            throw new SedanProofContractException(
                $"completed freeze artifact {label} missing or not a regular file at {artifact.Path}");
        }

        var info = new FileInfo(artifact.Path);

        if (artifact.Size is long size && info.Length != size)
        {
            throw new SedanProofContractException(
                $"completed freeze artifact {label} size drifted ({info.Length} vs recorded {size})");
        }

        var digest = Sha256Hex(File.ReadAllBytes(artifact.Path));

        if (!string.Equals(digest, artifact.Sha256, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"completed freeze artifact {label} sha256 drifted ({digest} vs recorded {artifact.Sha256})");
        }

        return digest;
    }

    private static bool IsSymlink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is not null;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static byte[] ReadBounded(string path, long maxBytes)
    {
        if (!File.Exists(path) || IsSymlink(path))
        {
            throw new SedanProofContractException($"file missing or not a regular file: {path}");
        }

        var length = new FileInfo(path).Length;

        if (length > maxBytes)
        {
            throw new SedanProofContractException($"file {path} is {length} bytes; exceeds the {maxBytes} byte cap");
        }

        return File.ReadAllBytes(path);
    }

    private static JsonDocument ParseJson(string path)
    {
        return StrictJson.Parse(ReadBounded(path, JsonSizeCapBytes), path);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static SelectionTable ParseSelection(string path)
    {
        var lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
        var lastIndex = lines.Length - 1;

        if (lines[lastIndex].Length == 0)
        {
            Array.Resize(ref lines, lastIndex);
        }

        if (lines.Length == 0)
        {
            throw new SedanProofContractException($"selection.tsv is empty: {path}");
        }

        var expectedHeader = string.Join('\t', SelectionColumns);

        if (!string.Equals(lines[0].TrimEnd('\r'), expectedHeader, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"selection.tsv header does not match the {SelectionColumns.Length}-column _SEL_COLUMNS contract: {path}");
        }

        var primaries = new List<SedanProofSite>();
        var alternates = new List<SedanProofSite>();

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var fields = line.Split('\t');

            if (fields.Length != SelectionColumns.Length)
            {
                throw new SedanProofContractException(
                    $"selection.tsv row {index} has {fields.Length} fields; expected {SelectionColumns.Length}: {path}");
            }

            var site = ParseSite(fields, index, path);

            if (site.Rank < 1)
            {
                throw new SedanProofContractException(
                    $"selection.tsv row {index} rank {site.Rank} must be at least 1: {path}");
            }

            if (site.Role == "primary")
            {
                RequireStrictlyIncreasingRank(primaries, site, index, path);
                primaries.Add(site);
            }
            else if (site.Role == "alternate")
            {
                RequireStrictlyIncreasingRank(alternates, site, index, path);
                alternates.Add(site);
            }
            else
            {
                throw new SedanProofContractException(
                    $"selection.tsv row {index} has unknown role '{site.Role}'; expected primary or alternate");
            }
        }

        if (primaries.Count != ExpectedPrimaryCount)
        {
            throw new SedanProofContractException(
                $"selection.tsv has {primaries.Count} primary rows; expected {ExpectedPrimaryCount}");
        }

        if (alternates.Count != ExpectedAlternateCount)
        {
            throw new SedanProofContractException(
                $"selection.tsv has {alternates.Count} alternate rows; expected {ExpectedAlternateCount}");
        }

        var sectors = primaries.Select(site => site.Sector).Distinct(StringComparer.Ordinal).Count();

        if (sectors < MinSectors)
        {
            throw new SedanProofContractException(
                $"selection.tsv primaries cover {sectors} sectors; at least {MinSectors} required");
        }

        return new SelectionTable(primaries, alternates);
    }

    private static void RequireStrictlyIncreasingRank(List<SedanProofSite> rows, SedanProofSite site, int rowIndex, string path)
    {
        if (rows.Count > 0 && site.Rank <= rows[^1].Rank)
        {
            throw new SedanProofContractException(
                $"selection.tsv row {rowIndex} {site.Role} rank {site.Rank} is not greater than the preceding rank "
                + $"{rows[^1].Rank}: ranks must be strictly increasing in row order with no renumbering: {path}");
        }
    }

    private static SedanProofSite ParseSite(string[] fields, int rowIndex, string path)
    {
        double Finite(string name)
        {
            var token = fields[Array.IndexOf(SelectionColumns, name)];
            return ParseFinite(name, token, rowIndex, path);
        }

        double? FitOptional(string name)
        {
            var token = fields[Array.IndexOf(SelectionColumns, name)];
            return token.Length == 0 ? null : ParseFinite(name, token, rowIndex, path);
        }

        int Integer(string name)
        {
            var token = fields[Array.IndexOf(SelectionColumns, name)];

            if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                throw new SedanProofContractException(
                    $"selection.tsv row {rowIndex} field '{name}' is not an integer: '{token}' ({path})");
            }

            return value;
        }

        return new SedanProofSite(
            Id: fields[0],
            Role: fields[1],
            Rank: Integer("rank"),
            Row: Integer("row"),
            Col: Integer("col"),
            UtmX: Finite("utm_x"),
            UtmY: Finite("utm_y"),
            L93X: Finite("l93_x"),
            L93Y: Finite("l93_y"),
            GameX: Finite("game_x"),
            GameY: Finite("game_y"),
            Wgs84Lat: Finite("wgs84_lat"),
            Wgs84Lon: Finite("wgs84_lon"),
            TruthZ: Finite("truth_z"),
            SigmaFit: FitOptional("sigma_fit"),
            SlopeDeg: FitOptional("slope_deg"),
            CurvBound: FitOptional("curv_bound"),
            Density: Finite("density"),
            Stratum: fields[Array.IndexOf(SelectionColumns, "stratum")],
            Sector: fields[Array.IndexOf(SelectionColumns, "sector")]);
    }

    private static double ParseFinite(string name, string token, int rowIndex, string path)
    {
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw new SedanProofContractException(
                $"selection.tsv row {rowIndex} field '{name}' is not a finite number: '{token}' ({path})");
        }

        return value;
    }

    private static string VerifyFrozenManifest(string path, string candidatesSha, string selectionSha, SelectionTable selection)
    {
        using var document = ParseJson(path);
        var root = RequireRootObject(document, path);

        var protocol = RequireString(root, "protocol_version", path);

        if (!string.Equals(protocol, FinalProtocolVersion, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"frozen manifest protocol_version '{protocol}' is not {FinalProtocolVersion}: {path}");
        }

        var scope = RequireString(root, "scope", path);

        if (!string.Equals(scope, FinalScope, StringComparison.Ordinal))
        {
            if (scope.Contains("preliminary", StringComparison.OrdinalIgnoreCase))
            {
                throw new SedanProofContractException(
                    $"preliminary selection can never be the final freeze (manifest scope '{scope}'): {path}");
            }

            throw new SedanProofContractException($"frozen manifest scope '{scope}' is not '{FinalScope}': {path}");
        }

        VerifyConstants(RequireObject(root, "constants", path), path);

        var selectionObject = RequireObject(root, "selection", path);
        var primaryIds = RequireStringArray(selectionObject, "primary_ids", path);

        if (primaryIds.Count != ExpectedPrimaryCount)
        {
            throw new SedanProofContractException(
                $"frozen manifest primary_ids has {primaryIds.Count} entries; expected {ExpectedPrimaryCount}: {path}");
        }

        if (!primaryIds.SequenceEqual(selection.Primaries.Select(site => site.Id), StringComparer.Ordinal))
        {
            throw new SedanProofContractException(
                $"frozen manifest primary_ids disagree with the selection.tsv primary rows in rank order: {path}");
        }

        var alternateIds = RequireStringArray(selectionObject, "alternate_ids", path);

        if (alternateIds.Count != ExpectedAlternateCount)
        {
            throw new SedanProofContractException(
                $"frozen manifest alternate_ids has {alternateIds.Count} entries; expected {ExpectedAlternateCount}: {path}");
        }

        if (!alternateIds.SequenceEqual(selection.Alternates.Select(site => site.Id), StringComparer.Ordinal))
        {
            throw new SedanProofContractException(
                $"frozen manifest alternate_ids disagree with the selection.tsv alternate rows in rank order: {path}");
        }

        var sectors = RequireStringArray(selectionObject, "sectors_covered", path);

        if (sectors.Count < MinSectors)
        {
            throw new SedanProofContractException(
                $"frozen manifest sectors_covered has {sectors.Count} sectors; at least {MinSectors} required: {path}");
        }

        var tables = RequireObject(root, "tables", path);

        if (!string.Equals(RequireString(tables, "candidates_sha256", path), candidatesSha, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"frozen manifest tables.candidates_sha256 disagrees with the candidates.tsv digest: {path}");
        }

        if (!string.Equals(RequireString(tables, "selection_sha256", path), selectionSha, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"frozen manifest tables.selection_sha256 disagrees with the selection.tsv digest: {path}");
        }

        return CanonicalDigest(root);
    }

    private static void VerifyConstants(JsonElement constants, string path)
    {
        foreach (var (key, expected) in ExpectedConstants)
        {
            if (!constants.TryGetProperty(key, out var actual))
            {
                throw new SedanProofContractException($"frozen manifest constants.{key} is missing: {path}");
            }

            CompareConstant(key, expected, actual, path);
        }

        foreach (var property in constants.EnumerateObject())
        {
            if (!ExpectedConstants.ContainsKey(property.Name))
            {
                throw new SedanProofContractException($"frozen manifest constants.{property.Name} is not a frozen protocol constant: {path}");
            }
        }
    }

    private static void CompareConstant(string key, object expected, JsonElement actual, string path)
    {
        if (!MatchesConstant(expected, actual))
        {
            throw new SedanProofContractException(
                $"frozen manifest constants.{key} drifted: manifest {actual.GetRawText()} vs protocol {FormatExpected(expected)}: {path}");
        }
    }

    private static bool MatchesConstant(object expected, JsonElement actual)
    {
        switch (expected)
        {
            case long integer:
                return actual.ValueKind == JsonValueKind.Number && actual.TryGetInt64(out var value) && value == integer;
            case double real:
                return actual.ValueKind == JsonValueKind.Number && actual.TryGetDouble(out var number)
                    && Math.Abs(number - real) <= FloatTolerance;
            case string text:
                return actual.ValueKind == JsonValueKind.String && string.Equals(actual.GetString(), text, StringComparison.Ordinal);
            case object[] items:
                if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != items.Length)
                {
                    return false;
                }

                var index = 0;

                foreach (var element in actual.EnumerateArray())
                {
                    if (!MatchesConstant(items[index++], element))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static string FormatExpected(object expected)
    {
        return expected switch
        {
            object[] items => "[" + string.Join(",", items.Select(FormatExpected)) + "]",
            double real => real.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static void VerifyReceipt(string path, string manifestDigest, string candidatesSha, string selectionSha)
    {
        using var document = ParseJson(path);
        var root = RequireRootObject(document, path);

        if (!RequireElement(root, "receipt_version", path).TryGetInt64(out var version) || version != 1)
        {
            throw new SedanProofContractException($"receipt_version is not 1: {path}");
        }

        var tool = RequireString(root, "tool", path);

        if (!string.Equals(tool, ToolName, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"receipt tool '{tool}' is not {ToolName}: {path}");
        }

        var protocol = RequireString(root, "protocol_version", path);

        if (!string.Equals(protocol, FinalProtocolVersion, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"receipt protocol_version '{protocol}' is not {FinalProtocolVersion}: {path}");
        }

        var policy = RequireObject(root, "operator_policy", path);
        var policyDigest = RequireString(policy, "sha256", path);

        if (!string.Equals(policyDigest, PolicySha256, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"receipt operator policy sha256 {policyDigest} does not equal the pinned {PolicySha256}: {path}");
        }

        var chain = RequireObject(root, "provenance_hash_chain", path);
        var recordedDigest = RequireString(chain, "canonical_digest", path);

        if (!string.Equals(recordedDigest, manifestDigest, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"canonical digest mismatch: frozen manifest recomputes to {manifestDigest} but {ReceiptDigestPath} "
                + $"records {recordedDigest} ({path})");
        }

        if (!string.Equals(RequireString(chain, "candidates_sha256", path), candidatesSha, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"receipt candidates_sha256 disagrees with the candidates.tsv digest: {path}");
        }

        if (!string.Equals(RequireString(chain, "selection_sha256", path), selectionSha, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"receipt selection_sha256 disagrees with the selection.tsv digest: {path}");
        }

        var worker = RequireObject(root, "worker", path);
        RequirePinnedSha(worker, "sha256", BandedWorkerSha256, "receipt worker.sha256", path);
        RequirePinnedSha(worker, "base_worker_sha256", BaseWorkerSha256, "receipt worker.base_worker_sha256", path);
        RequirePinnedSha(worker, "select_module_sha256", SelectModuleSha256, "receipt worker.select_module_sha256", path);

        var registry = RequireStringArray(worker, "approved_registry", path);

        if (registry.Count != 2 || !registry.Contains(BaseWorkerSha256, StringComparer.Ordinal) || !registry.Contains(BandedWorkerSha256, StringComparer.Ordinal))
        {
            throw new SedanProofContractException($"receipt worker.approved_registry must be exactly the base and banded worker sha256 values: {path}");
        }

        var budgetB = RequireObject(root, "budget_b", path);
        var limit = RequireNumber(budgetB, "limit_game_units", path);

        if (Math.Abs(limit - BudgetBLimitGameUnits) > FloatTolerance)
        {
            throw new SedanProofContractException($"receipt budget_b.limit_game_units {limit} must be {BudgetBLimitGameUnits}: {path}");
        }

        var budgetA = RequireObject(root, "budget_a", path);

        if (!RequireString(budgetA, "escalation", path).Contains(BudgetAEscalationFragment, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"receipt budget_a.escalation no longer escalates before any DEM comparison: {path}");
        }

        if (RequireBoolean(root, "numeric_proof_pass", path))
        {
            throw new SedanProofContractException($"receipt numeric_proof_pass must be false: the freeze never computes the numeric residual: {path}");
        }

        var carried = RequireStringArray(root, "carried_unresolved", path);

        foreach (var fragment in CarriedUnresolvedFragments)
        {
            if (!carried.Any(entry => entry.Contains(fragment, StringComparison.Ordinal)))
            {
                throw new SedanProofContractException($"receipt carried_unresolved has no entry containing '{fragment}': {path}");
            }
        }
    }

    private static void VerifyIntegrityEvidence(string root)
    {
        var manifestPath = Path.Combine(root, IntegrityManifestRelativePath);

        if (!File.Exists(manifestPath) || IsSymlink(manifestPath))
        {
            throw new SedanProofContractException($"integrity manifest is missing or a symlink: {manifestPath}");
        }

        var path = Path.Combine(root, IntegrityProducerReceiptRelativePath);

        if (!File.Exists(path) || IsSymlink(path))
        {
            throw new SedanProofContractException($"producer receipt is missing or a symlink: {path}");
        }

        using var document = ParseJson(path);
        var receipt = RequireRootObject(document, path);

        RequirePinnedSha(RequireObject(receipt, "producer", path), "sha256", IntegrityProducerSha256, "producer receipt producer.sha256", path);
        RequirePinnedSha(RequireObject(receipt, "worker", path), "sha256", HeaderWorkerSha256, "producer receipt worker.sha256", path);

        var transport = RequireObject(receipt, "transport_state", path);
        RequireString(transport, "path", path);

        if (!RequireObject(transport, "tiles", path).EnumerateObject().Any())
        {
            throw new SedanProofContractException($"producer receipt transport_state.tiles has no per-tile entries: {path}");
        }
    }

    private static void VerifyExpectedFreeze(SedanProofExpectedFreeze? expected, string manifestDigest, SelectionTable selection)
    {
        if (expected is null)
        {
            return;
        }

        if (!string.Equals(expected.CanonicalDigestSha256, manifestDigest, StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"expected freeze canonical digest {expected.CanonicalDigestSha256} does not match the completed freeze {manifestDigest}");
        }

        var primaries = selection.Primaries.Select(site => site.Id).ToArray();

        if (!SameIdSet(expected.PrimaryIds, primaries))
        {
            throw new SedanProofContractException(
                $"expected primary ids [{string.Join(", ", expected.PrimaryIds)}] do not match the completed freeze primaries [{string.Join(", ", primaries)}]");
        }

        var alternates = selection.Alternates.Select(site => site.Id).ToArray();

        if (!SameIdSet(expected.AlternateIds, alternates))
        {
            throw new SedanProofContractException(
                $"expected alternate ids [{string.Join(", ", expected.AlternateIds)}] do not match the completed freeze alternates [{string.Join(", ", alternates)}]");
        }
    }

    private static bool SameIdSet(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        return expected.Count == actual.Count
            && expected.Distinct(StringComparer.Ordinal).Count() == expected.Count
            && expected.All(id => actual.Contains(id, StringComparer.Ordinal));
    }

    private static void RequirePinnedSha(JsonElement parent, string key, string expected, string label, string path)
    {
        var actual = RequireString(parent, key, path);

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"{label} {actual} does not equal the pinned {expected}: {path}");
        }
    }

    private static bool RequireBoolean(JsonElement parent, string name, string path)
    {
        var element = RequireElement(parent, name, path);

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        throw new SedanProofContractException($"{name} is not a boolean: {path}");
    }

    private static readonly string[] PreparationReceiptKeys =
    {
        "receipt_version", "tool", "generated_at", "cache_root", "stage", "freeze", "extract_receipt",
        "primaries", "alternates", "alternates_note", "operator_policy", "uncertainties", "vertical_policy",
        "chronology", "space_estimate", "outputs", "rgb_control_reference", "commands_sha256",
    };

    private static readonly string[] RgbControlReferenceKeys =
    {
        "source_role", "source_path", "source_sha256", "source_size", "pixels_per_axis", "control_pixels",
        "output_spacing_m", "footprint_half_m", "rounding", "tolerance_bytes",
    };

    private static readonly string[] FreezeBindingKeys =
    {
        "stage", "canonical_digest", "frozen_manifest_sha256", "receipt_sha256", "integrity_manifest_sha256",
        "candidates_sha256", "selection_sha256", "laz_tiles_sha256",
    };

    private static readonly string[] ExtractReceiptKeys = { "path", "sha256", "members", "total_bytes" };

    private static readonly string[] OperatorPolicyKeys =
    {
        "vertical_tolerance_meters", "vertical_tolerance_scope", "budget_b_game_units", "vertical_datum_label",
        "vertical_datum_note", "vertical_accuracy_note", "no_data_fill_policy",
    };

    private static readonly string[] VerticalPolicyKeys = { "originMeters", "source" };
    private static readonly string[] ChronologyKeys = { "freezeVerifiedAt", "firstDemRasterIoAt", "note" };
    private static readonly string[] SpaceEstimateKeys = { "dem_bytes", "rgb_bytes", "total_bytes" };
    private static readonly string[] OutputEntryKeys = { "path", "sha256", "size" };
    private static readonly string[] OutputLabels = { "manifest", "texture_map", "sources_txt", "extent_kml", "dem_tif", "rgb_tif" };
    private static readonly string[] FreezeEventKeys = { "event", "freezeVerifiedAt", "freezeReceiptSha256", "integrityManifestSha256", "selectionTables" };
    private static readonly string[] SelectionTablesKeys = { "candidates_sha256", "selection_sha256", "frozen_manifest_sha256", "canonical_digest" };
    private static readonly string[] DemStartKeys = { "event", "firstDemRasterIoAt", "argv" };
    private static readonly string[] ToolInvokedKeys =
    {
        "event", "tool", "argv", "timeoutSeconds", "heightSemantic", "returncode", "timedOut",
        "stdoutBytes", "stdoutSha256", "stderrBytes", "stderrSha256",
    };
    private static readonly string[] CompletedKeys = { "event", "completedAt", "outputs" };
    private static readonly string[] CompletedOutputKeys = { "sha256" };
    private static readonly string[] LedgerEventNames =
    {
        "freeze_validated_complete", "dem_rasterio_start", "tool_invoked", "preparation_completed",
    };

    private static readonly string[] ExpectedOutputRelativePaths =
    {
        PreparedManifestRelativePath, PreviewTexturesRelativePath, SourcesTextRelativePath,
        ExtentKmlRelativePath, DemRelativePath, RgbRelativePath,
    };

    private const int ExpectedLazTileCount = 6;
    private const string PreparationToolName = "sedan_prepare_inputs.py";
    private const string AlternatesNote = "recorded only; NEVER activated (no failure-based substitution)";

    private sealed record FreezeIdentity(
        string Stage,
        string CandidatesSha256,
        string SelectionSha256,
        string FrozenManifestSha256,
        string FreezeReceiptSha256,
        string IntegrityManifestSha256,
        string CanonicalDigest);

    private sealed record PreparationBinding(
        string ReceiptPath,
        string LedgerPath,
        string PreviewTexturesPath,
        double OriginMeters,
        DateTimeOffset FreezeVerifiedAt,
        DateTimeOffset FirstDemRasterIoAt,
        IReadOnlyDictionary<string, string> OutputShas256,
        double RgbOutputSpacingMeters,
        int RgbToleranceBytes);

    private static PreparationBinding VerifyPreparation(string root, FreezeIdentity freeze, SelectionTable selection)
    {
        var receiptPath = RequireNoSymlinkPath(root, PreparationReceiptRelativePath);
        var ledgerPath = RequireNoSymlinkPath(root, CommandsLedgerRelativePath);
        var previewPath = RequireNoSymlinkPath(root, PreviewTexturesRelativePath);

        var ledgerBytes = ReadBounded(ledgerPath, LedgerSizeCapBytes);
        var commandsSha = Sha256Hex(ledgerBytes);

        using var receiptDocument = ParseJson(receiptPath);
        var receipt = RequireRootObject(receiptDocument, receiptPath);
        RequireExactKeys(receipt, PreparationReceiptKeys, "preparation receipt", receiptPath);

        if (!RequireElement(receipt, "receipt_version", receiptPath).TryGetInt64(out var version) || version != 1)
        {
            throw new SedanProofContractException($"preparation receipt receipt_version is not 1: {receiptPath}");
        }

        RequireEqual(RequireString(receipt, "tool", receiptPath), PreparationToolName, "preparation receipt tool", receiptPath);
        RequireString(receipt, "generated_at", receiptPath);
        RequireEqual(Path.TrimEndingDirectorySeparator(RequireAbsolutePath(receipt, "cache_root", receiptPath)),
            Path.TrimEndingDirectorySeparator(root), "preparation receipt cache_root", receiptPath);
        RequireAbsolutePath(receipt, "stage", receiptPath);

        VerifyFreezeBinding(RequireObject(receipt, "freeze", receiptPath), freeze, receiptPath);
        VerifyExtractReceipt(RequireObject(receipt, "extract_receipt", receiptPath), receiptPath);
        RequireStringArrayEquals(receipt, "primaries", selection.Primaries.Select(site => site.Id).ToArray(), "primaries", receiptPath);
        RequireStringArrayEquals(receipt, "alternates", selection.Alternates.Select(site => site.Id).ToArray(), "alternates", receiptPath);
        RequireEqual(RequireString(receipt, "alternates_note", receiptPath), AlternatesNote, "receipt alternates_note", receiptPath);
        VerifyOperatorPolicy(RequireObject(receipt, "operator_policy", receiptPath), receiptPath);
        RequireStringCount(receipt, "uncertainties", 5, receiptPath);
        var originMeters = VerifyVerticalPolicy(RequireObject(receipt, "vertical_policy", receiptPath), receiptPath);
        var (freezeVerifiedAt, firstDemRasterIoAt) = VerifyReceiptChronology(RequireObject(receipt, "chronology", receiptPath), receiptPath);
        VerifySpaceEstimate(RequireObject(receipt, "space_estimate", receiptPath), receiptPath);

        var outputs = VerifyOutputs(root, RequireObject(receipt, "outputs", receiptPath), receiptPath);
        var (rgbSpacingMeters, rgbToleranceBytes) = VerifyRgbControlReference(
            RequireObject(receipt, "rgb_control_reference", receiptPath), root, outputs, receiptPath);
        RequireEqual(RequireString(receipt, "commands_sha256", receiptPath), commandsSha, "receipt commands_sha256", receiptPath);

        var ledger = ParseLedgerEvents(ledgerBytes, ledgerPath);

        try
        {
            VerifyLedgerEvents(ledger, ledgerPath, freeze, freezeVerifiedAt, firstDemRasterIoAt, outputs);
        }
        finally
        {
            foreach (var document in ledger)
            {
                document.Dispose();
            }
        }

        using var previewDocument = ParseJson(previewPath);
        VerifyPreviewTextures(previewDocument, previewPath);

        return new PreparationBinding(receiptPath, ledgerPath, previewPath, originMeters,
            ParseChronology(freezeVerifiedAt, "freezeVerifiedAt", receiptPath),
            ParseChronology(firstDemRasterIoAt, "firstDemRasterIoAt", receiptPath),
            outputs, rgbSpacingMeters, rgbToleranceBytes);
    }

    private static JsonDocument VerifyPreparedManifest(string path, PreparationBinding preparation, SelectionTable selection)
    {
        if (!File.Exists(path) || IsSymlink(path))
        {
            throw new SedanProofContractException(
                $"prepared manifest missing or not a regular file at {path}; the prepare step must produce it before the proof");
        }

        var document = ParseJson(path);

        try
        {
            var root = RequireRootObject(document, path);
            VerifyPreparedContent(root, path, preparation, selection);
            VerifyAlternatesAbsent(root, selection, path);
        }
        catch
        {
            document.Dispose();
            throw;
        }

        return document;
    }

    private static void VerifyPreparedContent(JsonElement root, string path, PreparationBinding preparation, SelectionTable selection)
    {
        RequireExactKeys(root, PreparedManifestKeys, "prepared manifest", path);
        VerifyTopLevelBlocks(root, preparation, path);
        var tolerances = RequireObject(root, "tolerances", path);
        var vertical = RequireNumber(tolerances, "verticalMeters", path);

        if (Math.Abs(vertical - OperatorVerticalLimitMeters) > FloatTolerance)
        {
            throw new SedanProofContractException(
                $"tolerances.verticalMeters must be {OperatorVerticalLimitMeters.ToString("R", CultureInfo.InvariantCulture)} "
                + $"(operator limit); found {vertical.ToString("R", CultureInfo.InvariantCulture)}: {path}");
        }

        VerifyDeclaredAccuracy(RequireArray(root, "sources", path), path);
        VerifySources(RequireArray(root, "sources", path), path);

        var verticalCount = 0;
        var imageryCount = 0;

        var points = RequireArray(root, "controlPoints", path).EnumerateArray().ToList();

        if (points.Count != ExpectedVerticalControlPoints + ExpectedImageryControlPoints)
        {
            throw new SedanProofContractException(
                $"prepared manifest has {points.Count} controlPoints; expected {ExpectedVerticalControlPoints + ExpectedImageryControlPoints}: {path}");
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];

            if (point.ValueKind != JsonValueKind.Object)
            {
                throw new SedanProofContractException($"controlPoints entries must be objects: {path}");
            }

            var name = RequireString(point, "name", path);

            if (name.Contains(ExamplePlaceholderMarker, StringComparison.Ordinal))
            {
                throw new SedanProofContractException(
                    $"control point '{name}' is an EXAMPLE ONLY placeholder; real runs require measured control points: {path}");
            }

            var latitude = RequireNumber(point, "latitude", path);
            var longitude = RequireNumber(point, "longitude", path);

            if (latitude < -90d || latitude > 90d || longitude < -180d || longitude > 180d)
            {
                throw new SedanProofContractException($"control point '{name}' has an invalid latitude/longitude: {path}");
            }

            var hasElevation = point.TryGetProperty("elevationMeters", out _);
            var hasImagery = point.TryGetProperty("imageryRgb", out _);

            if (hasElevation == hasImagery)
            {
                throw new SedanProofContractException(
                    $"control point '{name}' must carry exactly one of elevationMeters or imageryRgb: {path}");
            }

            if (index < ExpectedVerticalControlPoints)
            {
                if (!hasElevation)
                {
                    throw new SedanProofContractException($"control point {index} must be a vertical point with elevationMeters: {path}");
                }

                RequireExactKeys(point, VerticalPointKeys, $"control point '{name}'", path);
                RequireEqual(name, selection.Primaries[index].Id, "prepared manifest vertical control point name", path);
                var elevation = RequireNumber(point, "elevationMeters", path);

                if (elevation == 0d)
                {
                    throw new SedanProofContractException($"control point '{name}' has a zero elevationMeters placeholder: {path}");
                }

                verticalCount++;
            }
            else
            {
                if (!hasImagery)
                {
                    throw new SedanProofContractException($"control point {index} must be an imagery point with imageryRgb: {path}");
                }

                RequireExactKeys(point, ImageryPointKeys, $"control point '{name}'", path);
                RequireEqual(name, ImageryPointNames[index - ExpectedVerticalControlPoints],
                    "prepared manifest imagery control point name", path);
                VerifyImageryPoint(point, name, path);

                if (!RequireElement(point, "imageryToleranceBytes", path).TryGetInt32(out var pointTolerance)
                    || pointTolerance != preparation.RgbToleranceBytes)
                {
                    throw new SedanProofContractException(
                        $"control point '{name}' imageryToleranceBytes does not equal receipt rgb_control_reference.tolerance_bytes "
                        + $"{preparation.RgbToleranceBytes}: {path}");
                }

                imageryCount++;
            }
        }

        if (verticalCount != ExpectedVerticalControlPoints)
        {
            throw new SedanProofContractException(
                $"prepared manifest has {verticalCount} vertical control points; expected {ExpectedVerticalControlPoints}: {path}");
        }

        if (imageryCount != ExpectedImageryControlPoints)
        {
            throw new SedanProofContractException(
                $"prepared manifest has {imageryCount} imagery control points; expected {ExpectedImageryControlPoints}: {path}");
        }
    }

    private static readonly string[] PreparedManifestKeys =
    {
        "sources", "targetCoordinateSystem", "metersPerGameUnit", "defaultMaterial", "extentFeatureId",
        "verticalPolicy", "rasterPolicy", "tolerances", "controlPoints",
    };

    private static readonly string[] TargetCoordinateKeys = { "type", "zone", "hemisphere" };
    private static readonly string[] ManifestVerticalPolicyKeys = { "originMeters", "exaggeration" };
    private static readonly string[] RasterPolicyKeys = { "noData", "noDataSearchRadiusMeters", "edge", "imageryPixelsPerSample" };
    private static readonly string[] ToleranceKeys = { "verticalMeters" };
    private static readonly string[] SourceKeys = { "id", "kind", "path", "role", "label", "attribution" };
    private static readonly string[] ElevationSourceKeys = { "id", "kind", "path", "role", "verticalDatum", "label", "attribution" };
    private static readonly string[] VerticalPointKeys = { "name", "latitude", "longitude", "elevationMeters" };
    private static readonly string[] ImageryPointKeys = { "name", "latitude", "longitude", "imageryRgb", "imageryToleranceBytes" };
    private static readonly string[] ImageryPointNames = { "rgb-quarter-centre", "rgb-three-quarter-centre" };
    private const int ExpectedImageryToleranceBytes = 2;
    private const double ExpectedMetersPerGameUnit = 0.3048;
    private const double ExpectedNoDataRadiusMeters = 3.048;

    private static void VerifyTopLevelBlocks(JsonElement root, PreparationBinding preparation, string path)
    {
        var target = RequireObject(root, "targetCoordinateSystem", path);
        RequireExactKeys(target, TargetCoordinateKeys, "prepared manifest targetCoordinateSystem", path);
        RequireEqual(RequireString(target, "type", path), "utm", "targetCoordinateSystem.type", path);
        RequireEqual(RequireString(target, "hemisphere", path), "north", "targetCoordinateSystem.hemisphere", path);

        if (!RequireElement(target, "zone", path).TryGetInt32(out var zone) || zone != 31)
        {
            throw new SedanProofContractException($"targetCoordinateSystem.zone must be 31: {path}");
        }

        if (Math.Abs(RequireNumber(root, "metersPerGameUnit", path) - ExpectedMetersPerGameUnit) > FloatTolerance)
        {
            throw new SedanProofContractException($"metersPerGameUnit must be {ExpectedMetersPerGameUnit}: {path}");
        }

        var manifestSpacing = RequireNumber(root, "metersPerGameUnit", path);

        if (Math.Abs(manifestSpacing - preparation.RgbOutputSpacingMeters) > RgbGeometryTolerance)
        {
            throw new SedanProofContractException(
                $"receipt rgb_control_reference.output_spacing_m {preparation.RgbOutputSpacingMeters.ToString("R", CultureInfo.InvariantCulture)} "
                + $"does not equal the manifest metersPerGameUnit {manifestSpacing.ToString("R", CultureInfo.InvariantCulture)}: {path}");
        }

        RequireEqual(RequireString(root, "defaultMaterial", path), "grass", "defaultMaterial", path);
        RequireEqual(RequireString(root, "extentFeatureId", path), "sedan-extent", "extentFeatureId", path);

        var vertical = RequireObject(root, "verticalPolicy", path);
        RequireExactKeys(vertical, ManifestVerticalPolicyKeys, "prepared manifest verticalPolicy", path);
        var origin = RequireNumber(vertical, "originMeters", path);

        if (origin == 0d)
        {
            throw new SedanProofContractException($"verticalPolicy.originMeters must be non-zero by construction: {path}");
        }

        if (Math.Abs(origin - preparation.OriginMeters) > FloatTolerance)
        {
            throw new SedanProofContractException(
                $"verticalPolicy.originMeters {origin.ToString("R", CultureInfo.InvariantCulture)} disagrees with receipt "
                + $"vertical_policy.originMeters {preparation.OriginMeters.ToString("R", CultureInfo.InvariantCulture)}: {path}");
        }

        if (Math.Abs(RequireNumber(vertical, "exaggeration", path) - 1d) > FloatTolerance)
        {
            throw new SedanProofContractException($"verticalPolicy.exaggeration must be 1.0: {path}");
        }

        var raster = RequireObject(root, "rasterPolicy", path);
        RequireExactKeys(raster, RasterPolicyKeys, "prepared manifest rasterPolicy", path);
        RequireEqual(RequireString(raster, "noData", path), "nearest-valid", "rasterPolicy.noData", path);

        if (Math.Abs(RequireNumber(raster, "noDataSearchRadiusMeters", path) - ExpectedNoDataRadiusMeters) > FloatTolerance)
        {
            throw new SedanProofContractException($"rasterPolicy.noDataSearchRadiusMeters must be {ExpectedNoDataRadiusMeters}: {path}");
        }

        RequireEqual(RequireString(raster, "edge", path), "fail", "rasterPolicy.edge", path);

        if (!RequireElement(raster, "imageryPixelsPerSample", path).TryGetInt32(out var pixels) || pixels != 10)
        {
            throw new SedanProofContractException($"rasterPolicy.imageryPixelsPerSample must be 10: {path}");
        }

        RequireExactKeys(RequireObject(root, "tolerances", path), ToleranceKeys, "prepared manifest tolerances", path);
    }

    private static void VerifySources(JsonElement sources, string path)
    {
        var entries = sources.EnumerateArray().ToList();

        if (entries.Count != 3)
        {
            throw new SedanProofContractException($"prepared manifest sources must carry exactly 3 entries; found {entries.Count}: {path}");
        }

        VerifySource(entries[0], SourceKeys, "sedan-extent", "kml", ExtentKmlRelativePath, "extent", null,
            "generated extent KML", path);
        VerifySource(entries[1], ElevationSourceKeys, "sedan-dem-32631", "geotiff", DemRelativePath, "elevation", "NGF-IGN69",
            "IGN, Licence Ouverte 2.0", path);
        VerifySource(entries[2], SourceKeys, "sedan-ortho-32631", "geotiff", RgbRelativePath, "imagery", null,
            "IGN, Licence Ouverte 2.0", path);
    }

    private static void VerifySource(JsonElement source, string[] keys, string id, string kind, string relative,
        string role, string? datum, string attribution, string path)
    {
        RequireExactKeys(source, keys, $"prepared manifest source '{id}'", path);
        RequireEqual(RequireString(source, "id", path), id, "prepared manifest source id", path);
        RequireEqual(RequireString(source, "kind", path), kind, $"prepared manifest source '{id}' kind", path);
        RequireEqual(RequireString(source, "path", path), relative, $"prepared manifest source '{id}' path", path);
        RequireEqual(RequireString(source, "role", path), role, $"prepared manifest source '{id}' role", path);

        if (datum is not null)
        {
            RequireEqual(RequireString(source, "verticalDatum", path), datum, $"prepared manifest source '{id}' verticalDatum", path);
        }

        RequireString(source, "label", path);
        RequireContains(RequireString(source, "attribution", path), attribution, $"prepared manifest source '{id}' attribution", path);
    }

    private static void VerifyAlternatesAbsent(JsonElement root, SelectionTable selection, string path)
    {
        var alternateIds = selection.Alternates.Select(site => site.Id).ToList();

        if (ContainsAnyString(root, alternateIds))
        {
            throw new SedanProofContractException($"prepared manifest must never reference an alternate id: {path}");
        }
    }

    private static bool ContainsAnyString(JsonElement element, IReadOnlyList<string> needles)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => needles.Contains(element.GetString()!, StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsAnyString(item, needles)),
            JsonValueKind.Object => element.EnumerateObject().Any(property =>
                needles.Contains(property.Name, StringComparer.Ordinal) || ContainsAnyString(property.Value, needles)),
            _ => false,
        };
    }

    private static void VerifyDeclaredAccuracy(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "verticalAccuracyMeters")
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var declared))
                    {
                        throw new SedanProofContractException($"verticalAccuracyMeters must be a number when declared: {path}");
                    }

                    if (declared > OperatorVerticalLimitMeters)
                    {
                        throw new SedanProofContractException(
                            $"declared source accuracy {declared.ToString("R", CultureInfo.InvariantCulture)} exceeds the operator "
                            + "1.0 m limit: escalate before any DEM comparison");
                    }
                }

                VerifyDeclaredAccuracy(property.Value, path);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                VerifyDeclaredAccuracy(item, path);
            }
        }
    }

    private static void VerifyImageryPoint(JsonElement point, string name, string path)
    {
        var rgb = RequireArray(point, "imageryRgb", path);

        if (rgb.GetArrayLength() != 3)
        {
            throw new SedanProofContractException($"control point '{name}' imageryRgb must have three bytes: {path}");
        }

        var nonzero = false;

        foreach (var channel in rgb.EnumerateArray())
        {
            if (!channel.TryGetInt32(out var value) || value < 0 || value > 255)
            {
                throw new SedanProofContractException($"control point '{name}' imageryRgb entries must be bytes: {path}");
            }

            nonzero |= value != 0;
        }

        if (!nonzero)
        {
            throw new SedanProofContractException($"control point '{name}' imageryRgb is all zero (placeholder): {path}");
        }

        var tolerance = RequireElement(point, "imageryToleranceBytes", path);

        if (!tolerance.TryGetInt32(out var toleranceBytes) || toleranceBytes != ExpectedImageryToleranceBytes)
        {
            throw new SedanProofContractException($"control point '{name}' imageryToleranceBytes must be exactly {ExpectedImageryToleranceBytes}: {path}");
        }
    }

    internal static string CanonicalDigest(JsonElement root)
    {
        var builder = new StringBuilder();
        WriteCanonical(root, builder, topLevel: true);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void WriteCanonical(JsonElement element, StringBuilder builder, bool topLevel)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject()
                    .Where(property => !(topLevel && property.Name == "generated_at"))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToList();
                builder.Append('{');

                for (var index = 0; index < properties.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(JsonSerializer.Serialize(properties[index].Name, KeyOptions)).Append(':');
                    WriteCanonical(properties[index].Value, builder, topLevel: false);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var first = true;

                foreach (var item in element.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteCanonical(item, builder, topLevel: false);
                }

                builder.Append(']');
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static readonly JsonSerializerOptions KeyOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.BasicLatin),
    };

    private static JsonElement RequireRootObject(JsonDocument document, string path)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new SedanProofContractException($"JSON root is not an object: {path}");
        }

        return document.RootElement;
    }

    private static JsonElement RequireElement(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw new SedanProofContractException($"{path} is missing '{name}'");
        }

        return value;
    }

    private static JsonElement RequireObject(JsonElement parent, string name, string path)
    {
        var value = RequireElement(parent, name, path);

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new SedanProofContractException($"'{name}' must be a JSON object: {path}");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name, string path)
    {
        var value = RequireElement(parent, name, path);

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new SedanProofContractException($"'{name}' must be a JSON array: {path}");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string name, string path)
    {
        var value = RequireElement(parent, name, path);

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SedanProofContractException($"'{name}' must be a JSON string: {path}");
        }

        return value.GetString()!;
    }

    private static double RequireNumber(JsonElement parent, string name, string path)
    {
        var value = RequireElement(parent, name, path);

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw new SedanProofContractException($"'{name}' must be a finite JSON number: {path}");
        }

        return number;
    }

    private static IReadOnlyList<string> RequireStringArray(JsonElement parent, string name, string path)
    {
        var values = new List<string>();

        foreach (var item in RequireArray(parent, name, path).EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new SedanProofContractException($"'{name}' entries must be JSON strings: {path}");
            }

            values.Add(item.GetString()!);
        }

        return values;
    }

    private static void VerifyFreezeBinding(JsonElement freeze, FreezeIdentity identity, string path)
    {
        RequireExactKeys(freeze, FreezeBindingKeys, "preparation receipt freeze", path);
        RequireEqual(RequireString(freeze, "stage", path), identity.Stage, "receipt freeze.stage", path);
        RequireEqual(RequireString(freeze, "canonical_digest", path), identity.CanonicalDigest, "receipt freeze.canonical_digest", path);
        RequireEqual(RequireSha256Hex(freeze, "frozen_manifest_sha256", path), identity.FrozenManifestSha256, "receipt freeze.frozen_manifest_sha256", path);
        RequireEqual(RequireSha256Hex(freeze, "receipt_sha256", path), identity.FreezeReceiptSha256, "receipt freeze.receipt_sha256", path);
        RequireEqual(RequireSha256Hex(freeze, "integrity_manifest_sha256", path), identity.IntegrityManifestSha256, "receipt freeze.integrity_manifest_sha256", path);
        RequireEqual(RequireSha256Hex(freeze, "candidates_sha256", path), identity.CandidatesSha256, "receipt freeze.candidates_sha256", path);
        RequireEqual(RequireSha256Hex(freeze, "selection_sha256", path), identity.SelectionSha256, "receipt freeze.selection_sha256", path);

        var tiles = RequireObject(freeze, "laz_tiles_sha256", path);

        if (tiles.EnumerateObject().Count() != ExpectedLazTileCount)
        {
            throw new SedanProofContractException($"receipt freeze.laz_tiles_sha256 must carry {ExpectedLazTileCount} tiles: {path}");
        }

        foreach (var tile in tiles.EnumerateObject())
        {
            if (tile.Value.ValueKind != JsonValueKind.String || !IsSha256Hex(tile.Value.GetString()!))
            {
                throw new SedanProofContractException($"receipt freeze.laz_tiles_sha256.{tile.Name} is not a sha256 hex digest: {path}");
            }
        }
    }

    private static void VerifyExtractReceipt(JsonElement extract, string path)
    {
        RequireExactKeys(extract, ExtractReceiptKeys, "receipt extract_receipt", path);
        RequireString(extract, "path", path);
        RequireSha256Hex(extract, "sha256", path);

        if (!RequireElement(extract, "members", path).TryGetInt32(out var members) || members != ExtractMembers)
        {
            throw new SedanProofContractException($"receipt extract_receipt.members must be {ExtractMembers}: {path}");
        }

        if (!RequireElement(extract, "total_bytes", path).TryGetInt64(out var totalBytes) || totalBytes != ExtractTotalBytes)
        {
            throw new SedanProofContractException($"receipt extract_receipt.total_bytes must be {ExtractTotalBytes}: {path}");
        }
    }

    private static void VerifyOperatorPolicy(JsonElement policy, string path)
    {
        RequireExactKeys(policy, OperatorPolicyKeys, "receipt operator_policy", path);

        var vertical = RequireNumber(policy, "vertical_tolerance_meters", path);

        if (Math.Abs(vertical - OperatorVerticalLimitMeters) > FloatTolerance)
        {
            throw new SedanProofContractException($"receipt operator_policy.vertical_tolerance_meters must be 1: {path}");
        }

        RequireContains(RequireString(policy, "vertical_tolerance_scope", path), "engineering-only",
            "receipt operator_policy.vertical_tolerance_scope", path);

        if (Math.Abs(RequireNumber(policy, "budget_b_game_units", path) - BudgetBLimitGameUnits) > FloatTolerance)
        {
            throw new SedanProofContractException($"receipt operator_policy.budget_b_game_units must be {BudgetBLimitGameUnits}: {path}");
        }

        RequireEqual(RequireString(policy, "vertical_datum_label", path), "NGF-IGN69", "receipt operator_policy.vertical_datum_label", path);
        RequireContains(RequireString(policy, "vertical_datum_note", path), "NO EPSG:5720 geokey",
            "receipt operator_policy.vertical_datum_note", path);

        var accuracy = RequireString(policy, "vertical_accuracy_note", path);
        RequireContains(accuracy, "ABSENT", "receipt operator_policy.vertical_accuracy_note", path);
        RequireContains(accuracy, "escalates before any comparison", "receipt operator_policy.vertical_accuracy_note", path);

        var noData = RequireString(policy, "no_data_fill_policy", path);
        RequireContains(noData, "nearest-valid", "receipt operator_policy.no_data_fill_policy", path);
        RequireContains(noData, "3.048", "receipt operator_policy.no_data_fill_policy", path);
    }

    private static double VerifyVerticalPolicy(JsonElement vertical, string path)
    {
        RequireExactKeys(vertical, VerticalPolicyKeys, "receipt vertical_policy", path);
        var origin = RequireNumber(vertical, "originMeters", path);

        if (origin == 0d)
        {
            throw new SedanProofContractException($"receipt vertical_policy.originMeters must be non-zero by construction: {path}");
        }

        RequireContains(RequireString(vertical, "source", path), "prepared DEM extent minimum",
            "receipt vertical_policy.source", path);

        return origin;
    }

    private static (string FreezeVerifiedAt, string FirstDemRasterIoAt) VerifyReceiptChronology(JsonElement chronology, string path)
    {
        RequireExactKeys(chronology, ChronologyKeys, "receipt chronology", path);
        var freezeText = RequireString(chronology, "freezeVerifiedAt", path);
        var demText = RequireString(chronology, "firstDemRasterIoAt", path);

        if (ParseChronology(freezeText, "freezeVerifiedAt", path) >= ParseChronology(demText, "firstDemRasterIoAt", path))
        {
            throw new SedanProofContractException(
                $"chronology order violated: freezeVerifiedAt {freezeText} is not strictly before firstDemRasterIoAt {demText}: {path}");
        }

        RequireString(chronology, "note", path);
        return (freezeText, demText);
    }

    private static void VerifySpaceEstimate(JsonElement estimate, string path)
    {
        RequireExactKeys(estimate, SpaceEstimateKeys, "receipt space_estimate", path);

        foreach (var key in SpaceEstimateKeys)
        {
            RequireNonNegativeInteger(estimate, key, path);
        }
    }

    private static IReadOnlyDictionary<string, string> VerifyOutputs(string root, JsonElement outputs, string path)
    {
        RequireExactKeys(outputs, OutputLabels, "receipt outputs", path);
        var shas = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < OutputLabels.Length; index++)
        {
            var label = OutputLabels[index];
            var relative = ExpectedOutputRelativePaths[index];
            var entry = RequireObject(outputs, label, path);
            RequireExactKeys(entry, OutputEntryKeys, $"receipt outputs.{label}", path);

            var expectedPath = RequireNoSymlinkPath(root, relative);
            RequireEqual(RequireString(entry, "path", path), expectedPath, $"receipt outputs.{label}.path", path);

            if (!RequireElement(entry, "size", path).TryGetInt64(out var size) || size != new FileInfo(expectedPath).Length)
            {
                throw new SedanProofContractException($"receipt outputs.{label}.size does not match the file at {expectedPath}: {path}");
            }

            var sha = RequireSha256Hex(entry, "sha256", path);

            if (!string.Equals(sha, Sha256File(expectedPath), StringComparison.Ordinal))
            {
                throw new SedanProofContractException($"receipt outputs.{label}.sha256 does not match the file at {expectedPath}: {path}");
            }

            shas[label] = sha;
        }

        return shas;
    }

    private const int RgbPixelsPerAxisQuantum = 1280;
    private const double RgbGeometryTolerance = 1e-12;
    private const string RgbSourceRole = "operator-input prepared 0.2 m RGB (not the raw JP2, not the payload)";
    private const string RgbRounding = "clamp to [0, 255], then round half away from zero (Core ToByte)";

    private static (double SpacingMeters, int ToleranceBytes) VerifyRgbControlReference(
        JsonElement block, string root, IReadOnlyDictionary<string, string> outputShas, string path)
    {
        const string label = "receipt rgb_control_reference";
        RequireExactKeys(block, RgbControlReferenceKeys, label, path);
        RequireEqual(RequireString(block, "source_role", path), RgbSourceRole, $"{label}.source_role", path);
        RequireEqual(RequireString(block, "rounding", path), RgbRounding, $"{label}.rounding", path);

        var sourcePath = RequireString(block, "source_path", path);

        if (Path.IsPathRooted(sourcePath) || sourcePath.Contains('\\') || sourcePath.Split('/').Contains(".."))
        {
            throw new SedanProofContractException($"{label}.source_path must be a relative posix path under the proof root: {path}");
        }

        RequireEqual(Path.GetFileName(sourcePath), Path.GetFileName(RgbRelativePath), $"{label}.source_path basename", path);
        RequireNoSymlinkPath(root, sourcePath);

        var rgbOutputPath = RequireNoSymlinkPath(root, RgbRelativePath);
        RequireEqual(RequireSha256Hex(block, "source_sha256", path), outputShas["rgb_tif"], $"{label}.source_sha256", path);

        if (!RequireElement(block, "source_size", path).TryGetInt64(out var sourceSize) || sourceSize != new FileInfo(rgbOutputPath).Length)
        {
            throw new SedanProofContractException($"{label}.source_size does not match the file at {rgbOutputPath}: {path}");
        }

        if (!RequireElement(block, "pixels_per_axis", path).TryGetInt32(out var pixels)
            || pixels <= 0 || pixels % RgbPixelsPerAxisQuantum != 0)
        {
            throw new SedanProofContractException($"{label}.pixels_per_axis must be a positive multiple of {RgbPixelsPerAxisQuantum}: {path}");
        }

        var controlPixels = RequireArray(block, "control_pixels", path).EnumerateArray()
            .Select(element => element.TryGetInt32(out var value) ? value : -1).ToArray();
        var expectedControls = new[] { pixels / 4, (3 * pixels) / 4 };

        if (!controlPixels.SequenceEqual(expectedControls))
        {
            throw new SedanProofContractException(
                $"{label}.control_pixels must be [{expectedControls[0]}, {expectedControls[1]}] for pixels_per_axis {pixels}; "
                + $"found [{string.Join(", ", controlPixels)}]: {path}");
        }

        var spacing = RequireNumber(block, "output_spacing_m", path);

        if (!double.IsFinite(spacing))
        {
            throw new SedanProofContractException($"{label}.output_spacing_m must be finite: {path}");
        }

        var halfFootprint = RequireNumber(block, "footprint_half_m", path);

        if (Math.Abs(halfFootprint - (spacing / 2.0)) > RgbGeometryTolerance)
        {
            throw new SedanProofContractException(
                $"{label}.footprint_half_m {halfFootprint.ToString("R", CultureInfo.InvariantCulture)} must equal output_spacing_m / 2.0 = "
                + $"{(spacing / 2.0).ToString("R", CultureInfo.InvariantCulture)}: {path}");
        }

        if (!RequireElement(block, "tolerance_bytes", path).TryGetInt32(out var toleranceBytes)
            || toleranceBytes != ExpectedImageryToleranceBytes)
        {
            throw new SedanProofContractException($"{label}.tolerance_bytes must be {ExpectedImageryToleranceBytes}: {path}");
        }

        return (spacing, toleranceBytes);
    }

    private static void VerifyPreviewTextures(JsonDocument document, string path)
    {
        var root = RequireRootObject(document, path);
        RequireExactKeys(root, new[] { "grass" }, "preview-textures.json", path);
        RequireEqual(RequireString(root, "grass", path), "GrassType1", "preview-textures.json grass", path);
    }

    private static List<JsonDocument> ParseLedgerEvents(byte[] bytes, string path)
    {
        var documents = new List<JsonDocument>();

        try
        {
            if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
            {
                throw new SedanProofContractException($"chronology ledger must be non-empty and newline-terminated: {path}");
            }

            var start = 0;

            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != (byte)'\n')
                {
                    continue;
                }

                if (documents.Count == LedgerLineCap)
                {
                    throw new SedanProofContractException($"chronology ledger exceeds the {LedgerLineCap} line cap: {path}");
                }

                var line = bytes.AsSpan(start, index - start).ToArray();
                start = index + 1;

                if (line.Length == 0)
                {
                    throw new SedanProofContractException($"chronology ledger line {documents.Count + 1} is empty: {path}");
                }

                var document = StrictJson.Parse(line, path);
                documents.Add(document);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new SedanProofContractException($"chronology ledger line {documents.Count} is not a JSON object: {path}");
                }

                RequireString(document.RootElement, "event", path);
            }
        }
        catch
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }

            throw;
        }

        return documents;
    }

    private static void VerifyLedgerEvents(IReadOnlyList<JsonDocument> documents, string path, FreezeIdentity freeze,
        string freezeVerifiedAt, string firstDemRasterIoAt, IReadOnlyDictionary<string, string> outputShas)
    {
        var events = documents.Select(document => document.RootElement).ToList();
        var names = events.Select(element => RequireString(element, "event", path)).ToList();

        foreach (var name in names)
        {
            if (!LedgerEventNames.Contains(name, StringComparer.Ordinal))
            {
                throw new SedanProofContractException($"chronology ledger carries unknown event '{name}': {path}");
            }
        }

        if (!names.Contains("freeze_validated_complete", StringComparer.Ordinal))
        {
            throw new SedanProofContractException($"chronology ledger carries no freeze_validated_complete event: {path}");
        }

        if (!string.Equals(names[0], "freeze_validated_complete", StringComparison.Ordinal))
        {
            throw new SedanProofContractException(
                $"chronology ledger opens with '{names[0]}' rather than freeze_validated_complete (unfrozen ledger opening): {path}");
        }

        var attemptStart = names.FindLastIndex(name => name == "freeze_validated_complete");
        VerifyFreezeEvent(events[attemptStart], path, freeze, freezeVerifiedAt);

        var demIndices = new List<int>();
        var completedIndices = new List<int>();

        for (var index = attemptStart + 1; index < names.Count; index++)
        {
            if (names[index] == "dem_rasterio_start")
            {
                demIndices.Add(index);
            }
            else if (names[index] == "preparation_completed")
            {
                completedIndices.Add(index);
            }
        }

        if (demIndices.Count != 1)
        {
            throw new SedanProofContractException(
                $"chronology ledger current attempt must carry exactly one dem_rasterio_start event: {path}");
        }

        var demIndex = demIndices[0];

        if (completedIndices.Any(index => index < demIndex))
        {
            throw new SedanProofContractException(
                $"chronology ledger current attempt has preparation_completed before dem_rasterio_start: {path}");
        }

        if (completedIndices.Count == 0)
        {
            throw new SedanProofContractException(
                $"chronology ledger current attempt must end with exactly one preparation_completed event and no events after it: {path}");
        }

        if (completedIndices[^1] != names.Count - 1)
        {
            throw new SedanProofContractException(
                $"chronology ledger current attempt has events after its preparation_completed: {path}");
        }

        if (completedIndices.Count != 1)
        {
            throw new SedanProofContractException(
                $"chronology ledger current attempt must carry exactly one preparation_completed event: {path}");
        }

        VerifyHistoryOrder(events, names, path);
        VerifyAttemptSemantics(events, names, path);

        var demArgv = Array.Empty<string>();
        var firstHeightIndex = -1;
        var firstHeightArgv = Array.Empty<string>();

        for (var index = attemptStart + 1; index < names.Count; index++)
        {
            if (names[index] == "dem_rasterio_start")
            {
                RequireExactKeys(events[index], DemStartKeys, "dem_rasterio_start event", path);
                RequireEqual(RequireString(events[index], "firstDemRasterIoAt", path), firstDemRasterIoAt,
                    "ledger dem_rasterio_start.firstDemRasterIoAt", path);
                demArgv = RequireNonEmptyArgv(events[index], "dem_rasterio_start", path);
            }
            else if (names[index] == "tool_invoked")
            {
                VerifyToolInvoked(events[index], path);

                if (RequireBoolean(events[index], "heightSemantic", path) && firstHeightIndex < 0)
                {
                    firstHeightIndex = index;
                    firstHeightArgv = RequireNonEmptyArgv(events[index], "tool_invoked", path);
                }
            }
        }

        if (firstHeightIndex != demIndex + 1)
        {
            throw new SedanProofContractException(
                $"dem_rasterio_start must immediately precede the first height-semantic invocation: {path}");
        }

        if (!firstHeightArgv.SequenceEqual(demArgv, StringComparer.Ordinal))
        {
            throw new SedanProofContractException(
                $"dem_rasterio_start argv does not equal the first height-semantic tool_invoked argv: {path}");
        }

        var completed = events[^1];
        RequireExactKeys(completed, CompletedKeys, "preparation_completed event", path);
        RequireString(completed, "completedAt", path);
        var completedOutputs = RequireObject(completed, "outputs", path);
        RequireExactKeys(completedOutputs, OutputLabels, "preparation_completed outputs", path);

        foreach (var label in OutputLabels)
        {
            var entry = RequireObject(completedOutputs, label, path);
            RequireExactKeys(entry, CompletedOutputKeys, $"preparation_completed outputs.{label}", path);
            RequireEqual(RequireSha256Hex(entry, "sha256", path), outputShas[label], $"ledger outputs.{label}.sha256", path);
        }
    }

    private static void VerifyHistoryOrder(IReadOnlyList<JsonElement> events, IReadOnlyList<string> names, string path)
    {
        DateTimeOffset? previous = null;

        for (var index = 0; index < names.Count; index++)
        {
            var timestamp = LedgerTimestamp(events[index], names[index], path);

            if (timestamp is null)
            {
                continue;
            }

            if (previous is { } earlier && timestamp.Value < earlier)
            {
                throw new SedanProofContractException(
                    $"chronology ledger timestamp inversion at ledger line {index + 1}: event time precedes an earlier event: {path}");
            }

            previous = timestamp;
        }
    }

    private static void VerifyAttemptSemantics(IReadOnlyList<JsonElement> events, List<string> names, string path)
    {
        var starts = Enumerable.Range(0, names.Count).Where(index => names[index] == "freeze_validated_complete").ToList();

        for (var position = 0; position < starts.Count; position++)
        {
            var begin = starts[position];
            var end = position + 1 < starts.Count ? starts[position + 1] : names.Count;
            var demIndex = names.FindIndex(begin + 1, end - begin - 1, name => name == "dem_rasterio_start");

            for (var index = begin + 1; index < end; index++)
            {
                if (names[index] != "tool_invoked" || !RequireBoolean(events[index], "heightSemantic", path))
                {
                    continue;
                }

                if (demIndex < 0)
                {
                    throw new SedanProofContractException(
                        $"chronology ledger attempt at ledger line {begin + 1} carries height-semantic tool_invoked at ledger line {index + 1} but no dem_rasterio_start: {path}");
                }

                if (index < demIndex)
                {
                    throw new SedanProofContractException(
                        $"height-semantic tool_invoked at ledger line {index + 1} precedes dem_rasterio_start: {path}");
                }
            }
        }
    }

    private static DateTimeOffset? LedgerTimestamp(JsonElement element, string name, string path)
    {
        string? key = name switch
        {
            "freeze_validated_complete" => "freezeVerifiedAt",
            "dem_rasterio_start" => "firstDemRasterIoAt",
            "preparation_completed" => "completedAt",
            _ => null,
        };

        if (key is null)
        {
            return null;
        }

        return ParseChronology(RequireString(element, key, path), $"ledger {key}", path);
    }

    private static void VerifyFreezeEvent(JsonElement element, string path, FreezeIdentity freeze, string freezeVerifiedAt)
    {
        RequireExactKeys(element, FreezeEventKeys, "freeze_validated_complete event", path);
        RequireEqual(RequireString(element, "freezeVerifiedAt", path), freezeVerifiedAt, "ledger freezeVerifiedAt", path);
        RequireEqual(RequireSha256Hex(element, "freezeReceiptSha256", path), freeze.FreezeReceiptSha256,
            "ledger freezeReceiptSha256", path);
        RequireEqual(RequireSha256Hex(element, "integrityManifestSha256", path), freeze.IntegrityManifestSha256,
            "ledger integrityManifestSha256", path);

        var tables = RequireObject(element, "selectionTables", path);
        RequireExactKeys(tables, SelectionTablesKeys, "freeze_validated_complete selectionTables", path);
        RequireEqual(RequireSha256Hex(tables, "candidates_sha256", path), freeze.CandidatesSha256, "ledger selectionTables.candidates_sha256", path);
        RequireEqual(RequireSha256Hex(tables, "selection_sha256", path), freeze.SelectionSha256, "ledger selectionTables.selection_sha256", path);
        RequireEqual(RequireSha256Hex(tables, "frozen_manifest_sha256", path), freeze.FrozenManifestSha256,
            "ledger selectionTables.frozen_manifest_sha256", path);
        RequireEqual(RequireString(tables, "canonical_digest", path), freeze.CanonicalDigest, "ledger selectionTables.canonical_digest", path);
    }

    private static void VerifyToolInvoked(JsonElement element, string path)
    {
        RequireExactKeys(element, ToolInvokedKeys, "tool_invoked event", path);
        RequireString(element, "tool", path);
        RequireNonEmptyArgv(element, "tool_invoked", path);
        RequireNumber(element, "timeoutSeconds", path);
        RequireBoolean(element, "heightSemantic", path);

        if (RequireNumber(element, "returncode", path) != 0d)
        {
            throw new SedanProofContractException($"tool_invoked returncode must be 0 in a completed chronology ledger: {path}");
        }

        if (RequireBoolean(element, "timedOut", path))
        {
            throw new SedanProofContractException($"tool_invoked timedOut must be false in a completed chronology ledger: {path}");
        }

        RequireNonNegativeInteger(element, "stdoutBytes", path);
        RequireSha256Hex(element, "stdoutSha256", path);
        RequireNonNegativeInteger(element, "stderrBytes", path);
        RequireSha256Hex(element, "stderrSha256", path);
    }

    private static string[] RequireNonEmptyArgv(JsonElement element, string label, string path)
    {
        var argv = RequireStringArray(element, "argv", path).ToArray();

        if (argv.Length == 0)
        {
            throw new SedanProofContractException($"{label} argv must be a nonempty command line: {path}");
        }

        return argv;
    }

    private static DateTimeOffset ParseChronology(string text, string label, string path)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            throw new SedanProofContractException($"{label} is not an ISO-8601 timestamp: '{text}' ({path})");
        }

        return value;
    }

    private static void RequireExactKeys(JsonElement element, IReadOnlyCollection<string> expected, string label, string path)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).ToList();

        if (actual.Contains("vertical_accuracy_meters", StringComparer.Ordinal))
        {
            throw new SedanProofContractException(
                $"{label} must not carry vertical_accuracy_meters: the key is absent by policy: {path}");
        }

        if (actual.Count != expected.Count || actual.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
        {
            throw new SedanProofContractException(
                $"{label} must carry exactly the keys [{string.Join(", ", expected)}]; found [{string.Join(", ", actual)}]: {path}");
        }
    }

    private static void RequireEqual(string actual, string expected, string label, string path)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"{label} '{actual}' does not equal '{expected}': {path}");
        }
    }

    private static void RequireContains(string text, string fragment, string label, string path)
    {
        if (!text.Contains(fragment, StringComparison.Ordinal))
        {
            throw new SedanProofContractException($"{label} must contain '{fragment}': {path}");
        }
    }

    private static void RequireStringArrayEquals(JsonElement parent, string name, string[] expected, string label, string path)
    {
        var actual = RequireStringArray(parent, name, path);

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new SedanProofContractException(
                $"receipt {label} [{string.Join(", ", actual)}] must equal the selection table order [{string.Join(", ", expected)}]: {path}");
        }
    }

    private static void RequireStringCount(JsonElement parent, string name, int count, string path)
    {
        var items = RequireStringArray(parent, name, path);

        if (items.Count != count)
        {
            throw new SedanProofContractException($"receipt {name} must have {count} entries; found {items.Count}: {path}");
        }
    }

    private static string RequireAbsolutePath(JsonElement parent, string name, string path)
    {
        var text = RequireString(parent, name, path);

        if (!Path.IsPathFullyQualified(text))
        {
            throw new SedanProofContractException($"receipt {name} must be an absolute path: {path}");
        }

        return text;
    }

    private static void RequireNonNegativeInteger(JsonElement parent, string name, string path)
    {
        if (!RequireElement(parent, name, path).TryGetInt64(out var value) || value < 0)
        {
            throw new SedanProofContractException($"'{name}' must be a non-negative integer: {path}");
        }
    }

    private static string RequireSha256Hex(JsonElement parent, string name, string path)
    {
        var value = RequireString(parent, name, path);

        if (!IsSha256Hex(value))
        {
            throw new SedanProofContractException($"'{name}' must be a 64-character lowercase sha256 hex digest: {path}");
        }

        return value;
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static string RequireNoSymlinkPath(string root, string relative)
    {
        var current = root;

        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);

            if (IsSymlink(current))
            {
                throw new SedanProofContractException(
                    $"{current} is a symlink; proof artifacts must be real files under the proof root");
            }
        }

        if (!File.Exists(current))
        {
            throw new SedanProofContractException($"proof artifact missing or not a regular file: {current}");
        }

        return current;
    }

    private static class StrictJson
    {
        public static JsonDocument Parse(byte[] bytes, string path)
        {
            RejectNonFiniteTokens(bytes, path);
            RejectDuplicateProperties(bytes, path);

            try
            {
                return JsonDocument.Parse(bytes);
            }
            catch (JsonException error)
            {
                throw new SedanProofContractException($"JSON at {path} cannot be parsed: {error.Message}");
            }
        }

        private static void RejectNonFiniteTokens(byte[] bytes, string path)
        {
            var inString = false;
            var escaped = false;

            foreach (var value in bytes)
            {
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (value == (byte)'\\')
                    {
                        escaped = true;
                    }
                    else if (value == (byte)'"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (value == (byte)'"')
                {
                    inString = true;
                }
                else if (value == (byte)'N' || value == (byte)'I')
                {
                    throw new SedanProofContractException(
                        $"JSON at {path} contains a non-finite literal (NaN or Infinity), which is rejected");
                }
            }
        }

        private static void RejectDuplicateProperties(byte[] bytes, string path)
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
            var scopes = new Stack<HashSet<string>?>();

            try
            {
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                            break;
                        case JsonTokenType.StartArray:
                            scopes.Push(null);
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            scopes.Pop();
                            break;
                        case JsonTokenType.PropertyName:
                            var name = reader.GetString()!;

                            if (!scopes.Peek()!.Add(name))
                            {
                                throw new SedanProofContractException($"JSON at {path} repeats property '{name}'; duplicate keys are rejected");
                            }

                            break;
                    }
                }
            }
            catch (JsonException error)
            {
                throw new SedanProofContractException($"JSON at {path} cannot be parsed: {error.Message}");
            }
        }
    }

    private sealed record LedgerArtifact(string Path, string Sha256, long? Size);

    private sealed record SelectionTable(IReadOnlyList<SedanProofSite> Primaries, IReadOnlyList<SedanProofSite> Alternates);
}

/// <summary>Caller-pinned identity of the completed freeze; <see cref="SedanProofArtifacts.Load"/> requires exact matches.</summary>
public sealed record SedanProofExpectedFreeze(string CanonicalDigestSha256, IReadOnlyList<string> PrimaryIds, IReadOnlyList<string> AlternateIds);
