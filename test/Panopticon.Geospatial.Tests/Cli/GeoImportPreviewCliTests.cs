#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Panopticon.Data.Map;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Pipeline;
using Panopticon.Geospatial.Preview;
using Panopticon.Geospatial.Tests.Payload;
using Panopticon.Geospatial.Tests.Pipeline;
using Panopticon.IO;
using Panopticon.Tools.GeoImport;
using Xunit;

namespace Panopticon.Geospatial.Tests.Cli;

/// <summary>
/// Preview-verb tests over the real importer, reader, and preview builder with a fake
/// terrain texture catalog: no installation is probed, and every failure keeps the input
/// payload byte-identical. Transaction faults inject through the per-instance internal
/// move seam of the publication transaction.
/// </summary>
public class GeoImportPreviewCliTests
{
    private static readonly string[] CatalogNames = ["GrassType1", "DirtMediumType1", "RockType1", "SandType1"];

    private const string TexturesJson =
        """
        {
          "grass": "GrassType1",
          "dirt": "DirtMediumType1",
          "rock": "RockType1",
          "spare": "SandType1"
        }
        """;

    [Fact]
    public void Preview_FakeCatalog_WritesAMapOutsideThePayloadAndLeavesItUnchanged()
    {
        using var proof = ProofProject.Create();

        var (exitCode, standardOut, errorOut) = RunPreview(proof);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.Contains(Lines(standardOut), line => line.StartsWith("preview: ", StringComparison.Ordinal));

        var mapPath = Path.Combine(proof.MapsDirectory, proof.PayloadName + ".map");

        Assert.True(File.Exists(mapPath));
        AssertPayloadUnchanged(proof);
        Assert.Empty(Directory.GetFiles(proof.MapsDirectory, "*.tmp-*"));
        Assert.Empty(Directory.GetFiles(proof.MapsDirectory, "*.backup-*"));

        var tiles = ReadMap(mapPath).BlendTileData;

        Assert.Equal(257, tiles.Impassability.GetLength(0));
        Assert.Equal(257, tiles.Impassability.GetLength(1));
        Assert.Equal(
            ["GrassType1", "DirtMediumType1", "RockType1"],
            tiles.Textures.Select(texture => texture.Name).ToArray());
        Assert.True(tiles.Impassability[0, 0]);
        Assert.False(tiles.Impassability[1, 1]);
    }

    [Fact]
    public void Preview_WithCrop_WritesTheCroppedMap()
    {
        using var proof = ProofProject.Create();

        var (exitCode, _, errorOut) = RunPreview(proof, crop: "1,0,2,1");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());

        var tiles = ReadMap(Path.Combine(proof.MapsDirectory, proof.PayloadName + ".map")).BlendTileData;

        Assert.Equal(129, tiles.Impassability.GetLength(0));
        Assert.Equal(129, tiles.Impassability.GetLength(1));
    }

    [Fact]
    public void Preview_CropMalformed_IsAUsageFailure()
    {
        using var proof = ProofProject.Create();

        var (exitCode, standardOut, errorOut) = RunPreview(proof, crop: "1,2,three");

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.StartsWith("usage: geoimport preview", Assert.Single(Lines(errorOut)));
        Assert.False(Directory.Exists(proof.MapsDirectory));
    }

    [Fact]
    public void Preview_CropOutsidePayloadBounds_IsAnOperationalFailure()
    {
        using var proof = ProofProject.Create();

        var (exitCode, _, errorOut) = RunPreview(proof, crop: "0,0,9,9");

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-PRV-005", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_MissingRequiredArgument_IsAUsageFailure()
    {
        using var standardOut = new StringWriter();
        using var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            ["preview", "--payload", "payload"],
            standardOut,
            errorOut,
            CancellationToken.None,
            new GeoImportCliOptions { TerrainTextureCatalogResolver = FakeCatalog });

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, standardOut.ToString());
        Assert.StartsWith("usage: geoimport", errorOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_MissingTexturesFile_IsAnOperationalFailure()
    {
        using var proof = ProofProject.Create();

        var (exitCode, _, errorOut) = RunPreview(proof, texturesPath: Path.Combine(proof.Root, "absent.json"));

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-001", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_InvalidTexturesJson_IsAnOperationalFailure()
    {
        using var proof = ProofProject.Create("{ not json");

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-PRV-001", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_UnknownTextureBindingInUse_IsAnOperationalFailure()
    {
        using var proof = ProofProject.Create(
            """
            {
              "grass": "GrassType1",
              "dirt": "DirtMediumType1",
              "rock": "NotACatalogTexture"
            }
            """);

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-PRV-004", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_MissingPaletteBinding_IsAnOperationalFailure()
    {
        using var proof = ProofProject.Create(
            """
            {
              "grass": "GrassType1",
              "dirt": "DirtMediumType1"
            }
            """);

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-PRV-003", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_OversizeTexturesFile_IsRejectedWhileStreaming()
    {
        using var proof = ProofProject.Create();
        File.WriteAllBytes(proof.TexturesPath, new byte[GeoImportCli.MaxTexturesBytes + 1]);

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-002", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_TexturesWithInvalidUtf8ByteInUnusedBinding_FailsClosedAndLeavesPayloadUnchanged()
    {
        using var proof = ProofProject.Create();

        // One raw 0xFF byte inside the unused "spare" value; the rest of the file is valid UTF-8 JSON.
        var encoded = Encoding.UTF8.GetBytes(TexturesJson.Replace("\"SandType1\"", "\"SandType?\""));
        encoded[Array.IndexOf(encoded, (byte)'?')] = 0xFF;
        File.WriteAllBytes(proof.TexturesPath, encoded);

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-003", StringComparison.Ordinal));
        AssertPayloadUnchanged(proof);
        Assert.False(Directory.Exists(proof.MapsDirectory));
    }

    [Fact]
    public void Preview_CancelledRead_CancelsWithNoArtifacts()
    {
        using var proof = ProofProject.Create();
        using var source = new CancellationTokenSource();
        source.Cancel();

        var (exitCode, _, errorOut) = RunPreview(proof, cancellationToken: source.Token);

        Assert.Equal(1, exitCode);
        Assert.Equal(
            $"cancelled: the preview was cancelled; the published payload at {proof.PayloadRoot} is unchanged",
            Assert.Single(Lines(errorOut)));
        Assert.False(Directory.Exists(proof.MapsDirectory));
    }

    [Fact]
    public void Preview_WithoutForce_PreservesTheExistingMap()
    {
        using var proof = ProofProject.Create();
        proof.WriteExistingMap("operator map");

        var (exitCode, _, errorOut) = RunPreview(proof);

        Assert.Equal(1, exitCode);
        Assert.Equal("operator map", File.ReadAllText(proof.MapPath));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-006", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(proof.MapsDirectory, "*.tmp-*"));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_WithForce_RetainsABackupOfTheOldMapByteExact()
    {
        using var proof = ProofProject.Create();
        proof.WriteExistingMap("operator map");

        var (exitCode, standardOut, errorOut) = RunPreview(proof, force: true);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.Contains(Lines(standardOut), line => line.StartsWith("retained backup: ", StringComparison.Ordinal));

        var backup = Assert.Single(Directory.GetFiles(proof.MapsDirectory, "*.backup-*"));

        Assert.Equal("operator map", File.ReadAllText(backup));
        Assert.NotEqual("operator map", File.ReadAllText(proof.MapPath));
    }

    [Fact]
    public void Preview_FinalMoveFailure_RestoresTheOldMap()
    {
        using var proof = ProofProject.Create();
        proof.WriteExistingMap("operator map");

        var (exitCode, _, errorOut) = RunPreview(
            proof,
            force: true,
            configureTransaction: transaction => transaction.MoveFault = (source, destination) =>
                destination == proof.MapPath && source.Contains(".tmp-", StringComparison.Ordinal)
                    ? new IOException("injected final move failure")
                    : null);

        Assert.Equal(1, exitCode);
        Assert.Equal("operator map", File.ReadAllText(proof.MapPath));
        Assert.Empty(Directory.GetFiles(proof.MapsDirectory, "*.backup-*"));
        Assert.Empty(Directory.GetFiles(proof.MapsDirectory, "*.tmp-*"));
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-009", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_RestoreFailure_PreservesBothAndReportsRecoveryPaths()
    {
        using var proof = ProofProject.Create();
        proof.WriteExistingMap("operator map");

        var (exitCode, _, errorOut) = RunPreview(
            proof,
            force: true,
            configureTransaction: transaction => transaction.MoveFault = (_, destination) =>
                destination == proof.MapPath ? new IOException("injected move failure") : null);

        Assert.Equal(1, exitCode);

        var backup = Assert.Single(Directory.GetFiles(proof.MapsDirectory, "*.backup-*"));

        Assert.Equal("operator map", File.ReadAllText(backup));

        var temp = Assert.Single(Directory.GetFiles(proof.MapsDirectory, "*.tmp-*"));

        Assert.True(new FileInfo(temp).Length > 0);
        Assert.False(File.Exists(proof.MapPath));

        var recovery = Assert.Single(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-010", StringComparison.Ordinal));

        Assert.Contains(backup, recovery);
        Assert.Contains(temp, recovery);
        AssertPayloadFailedLine(errorOut, proof);
    }

    [Fact]
    public void Preview_LateCancelDuringTheCriticalPair_CommitsSuccessfully()
    {
        using var proof = ProofProject.Create();
        using var source = new CancellationTokenSource();

        var (exitCode, standardOut, errorOut) = RunPreview(
            proof,
            cancellationToken: source.Token,
            configureTransaction: transaction => transaction.MoveFault = (_, _) =>
            {
                source.Cancel();
                return null;
            });

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, errorOut.ToString());
        Assert.Contains(Lines(standardOut), line => line.StartsWith("preview: ", StringComparison.Ordinal));
        Assert.True(File.Exists(proof.MapPath));
    }

    [Fact]
    public void Preview_OutputInsidePayloadRoot_IsRejectedBeforeCreatingAnything()
    {
        using var proof = ProofProject.Create();

        var (exitCode, _, errorOut) = RunPreview(proof, mapsDirectory: Path.Combine(proof.PayloadRoot, "maps"));

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-007", StringComparison.Ordinal));
        AssertPayloadFailedLine(errorOut, proof);
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_TargetAliasingTheTexturesFile_IsRejected()
    {
        using var proof = ProofProject.Create();
        // The textures file IS the would-be output target: <maps>/<payload name>.map.
        Directory.CreateDirectory(proof.MapsDirectory);
        var aliasingTextures = Path.Combine(proof.MapsDirectory, proof.PayloadName + ".map");
        File.WriteAllText(aliasingTextures, TexturesJson);

        var (exitCode, _, errorOut) = RunPreview(proof, texturesPath: aliasingTextures);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-007", StringComparison.Ordinal));
        Assert.Equal(TexturesJson, File.ReadAllText(aliasingTextures));
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void Preview_UnsafePayloadDirectoryName_IsRejected()
    {
        using var proof = ProofProject.Create();

        var (exitCode, _, errorOut) = RunPreview(proof, payloadPath: proof.PayloadRoot + ":unsafe");

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-004", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_PayloadSymlink_IsRejectedWithoutTouchingTheTarget()
    {
        using var proof = ProofProject.Create();
        var linkedPayload = Path.Combine(proof.Root, "linked-payload");
        Directory.CreateSymbolicLink(linkedPayload, proof.PayloadRoot);

        var (exitCode, _, errorOut) = RunPreview(proof, payloadPath: linkedPayload);

        Assert.Equal(1, exitCode);
        Assert.Contains(Lines(errorOut), line => line.StartsWith("Error GEO-CLI-004", StringComparison.Ordinal));
        AssertPayloadUnchanged(proof);
    }

    [Fact]
    public void ImportAndReport_WithAThrowingResolver_NeverProbeTheInstallation()
    {
        using var project = ImportProjectFixture.Create();
        var output = project.OutputPath();
        var options = new GeoImportCliOptions { TerrainTextureCatalogResolver = ThrowingCatalog };

        var importCode = GeoImportCli.Run(
            ["import", "--manifest", project.ManifestPath, "--out", output],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None,
            options);

        Assert.Equal(0, importCode);

        var reportCode = GeoImportCli.Run(
            ["report", "--payload", output],
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None,
            options);

        Assert.Equal(0, reportCode);
    }

    [Fact]
    public void WriteStream_CancelledMidWrite_ForwardsNoFurtherBlock()
    {
        using var source = new CancellationTokenSource();
        using var inner = new CancellingStream(source, blocksBeforeCancel: 1);
        using var stream = new PreviewMapWriteStream(inner, source.Token);
        var payload = new byte[3 * 64 * 1024];

        Assert.Throws<OperationCanceledException>(() => stream.Write(payload));

        Assert.Equal(64 * 1024, inner.TotalWritten);
        Assert.True(stream.CanSeek);
    }

    private static ITerrainTextureCatalog FakeCatalog()
    {
        return new FakeTerrainTextureCatalog(CatalogNames);
    }

    private static ITerrainTextureCatalog ThrowingCatalog()
    {
        throw new InvalidOperationException("the resolver must not run outside the preview verb");
    }

    private static (int ExitCode, StringWriter StandardOut, StringWriter ErrorOut) RunPreview(
        ProofProject proof,
        string? payloadPath = null,
        string? texturesPath = null,
        string? mapsDirectory = null,
        string? crop = null,
        bool force = false,
        Action<PreviewMapTransaction>? configureTransaction = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "preview" };

        void Add(string name, string? value)
        {
            if (value is not null)
            {
                arguments.Add(name);
                arguments.Add(value);
            }
        }

        Add("--payload", payloadPath ?? proof.PayloadRoot);
        Add("--textures", texturesPath ?? proof.TexturesPath);
        Add("--crop", crop);
        Add("--out", mapsDirectory ?? proof.MapsDirectory);

        if (force)
        {
            arguments.Add("--force");
        }

        var standardOut = new StringWriter();
        var errorOut = new StringWriter();

        var exitCode = GeoImportCli.Run(
            arguments.ToArray(),
            standardOut,
            errorOut,
            cancellationToken,
            new GeoImportCliOptions
            {
                TerrainTextureCatalogResolver = FakeCatalog,
                ConfigureTransaction = configureTransaction,
            });

        return (exitCode, standardOut, errorOut);
    }

    private static void AssertPayloadFailedLine(StringWriter errorOut, ProofProject proof)
    {
        Assert.Contains(
            Lines(errorOut),
            line => line == $"preview failed: the published payload at {proof.PayloadRoot} is unchanged");
    }

    private static void AssertPayloadUnchanged(ProofProject proof)
    {
        // Byte identity per relative path, which also pins the exact inventory.
        var baseline = proof.PayloadBaseline();
        var current = SnapshotFiles(proof.PayloadRoot);

        Assert.Equal(
            baseline.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            current.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray());

        foreach (var relativePath in baseline.Keys.OrderBy(path => path, StringComparer.Ordinal))
        {
            Assert.Equal(baseline[relativePath], current[relativePath]);
        }
    }

    private static MapFile ReadMap(string mapPath)
    {
        using var fileSystem = new DiskFileSystem(Path.GetDirectoryName(mapPath)!);

        return MapFile.FromFileSystemEntry(
            new FileSystemEntry(fileSystem, Path.GetFileName(mapPath), (uint)new FileInfo(mapPath).Length, () => File.OpenRead(mapPath)));
    }

    private static string[] Lines(StringWriter writer)
    {
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static Dictionary<string, byte[]> SnapshotFiles(string root)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order())
        {
            snapshot[Path.GetRelativePath(root, path)] = File.ReadAllBytes(path);
        }

        return snapshot;
    }

    private sealed class FakeTerrainTextureCatalog(string[] names) : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => names;
    }

    private sealed class CancellingStream(CancellationTokenSource source, int blocksBeforeCancel) : MemoryStream
    {
        private readonly CancellationTokenSource _source = source;
        private readonly int _blocksBeforeCancel = blocksBeforeCancel;
        private int _blocks;

        internal long TotalWritten { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _blocks++;

            if (_blocks > _blocksBeforeCancel)
            {
                // Cancel and swallow this block: the next block start in the wrapper
                // must observe the token before forwarding anything else.
                _source.Cancel();
                return;
            }

            base.Write(buffer);
            TotalWritten += buffer.Length;
        }
    }

    private sealed class ProofProject : IDisposable
    {
        internal const string PayloadDirectoryName = "payload";

        private ProofProject(string root, string payloadRoot, string texturesPath, string mapsDirectory, Dictionary<string, byte[]> payloadBaseline)
        {
            Root = root;
            PayloadRoot = payloadRoot;
            TexturesPath = texturesPath;
            MapsDirectory = mapsDirectory;
            _payloadBaseline = payloadBaseline;
        }

        private readonly Dictionary<string, byte[]> _payloadBaseline;

        internal string Root { get; }

        internal string PayloadRoot { get; }

        internal string TexturesPath { get; }

        internal string MapsDirectory { get; }

        internal string PayloadName => PayloadDirectoryName;

        internal string MapPath => Path.Combine(MapsDirectory, PayloadName + ".map");

        internal Dictionary<string, byte[]> PayloadBaseline()
        {
            return _payloadBaseline;
        }

        internal static ProofProject Create(string? textures = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "panopticon-preview-cli-tests-" + Guid.NewGuid().ToString("N"));
            var payloadRoot = Path.Combine(root, PayloadDirectoryName);
            Directory.CreateDirectory(root);

            // The accepted reader fixture writes a complete 2x2 payload with imagery
            // directly through the accepted codecs; its lattice drives the mask expectations.
            ReaderTestSupport.CreatePayload(payloadRoot, chunksX: 2, chunksY: 2, imageryPixelsPerSample: 1);

            var payloadBaseline = SnapshotFiles(payloadRoot);

            Assert.NotEmpty(payloadBaseline);

            var texturesPath = Path.Combine(root, "textures.json");
            File.WriteAllText(texturesPath, textures ?? TexturesJson);
            var mapsDirectory = Path.Combine(root, "maps");

            return new ProofProject(root, payloadRoot, texturesPath, mapsDirectory, payloadBaseline);
        }

        internal void WriteExistingMap(string content)
        {
            Directory.CreateDirectory(MapsDirectory);
            File.WriteAllText(MapPath, content);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
