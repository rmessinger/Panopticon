#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Payload.Storage;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

public class PayloadDirectoryTransactionTests
{
    private sealed class ScratchRoot : IDisposable
    {
        internal string Root { get; } = Path.Combine(
            Path.GetTempPath(), "panopticon-payload-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            BclPayloadDirectoryIo.Instance.DeleteDirectory(Root);
        }
    }

    private sealed class FaultableIo : IPayloadDirectoryIo
    {
        private readonly BclPayloadDirectoryIo _bcl = BclPayloadDirectoryIo.Instance;

        internal Func<string, string, Exception?>? MoveFault { get; set; }

        internal Action<string, string>? BeforeMove { get; set; }

        internal Func<string, Exception?>? DeleteFault { get; set; }

        internal List<(string Source, string Destination)> Moves { get; } = [];

        internal List<string> Deletes { get; } = [];

        internal List<string> Creates { get; } = [];

        public void CreateDirectory(string path)
        {
            Creates.Add(path);
            _bcl.CreateDirectory(path);
        }

        public void MoveDirectory(string sourceDirectory, string destinationDirectory)
        {
            Moves.Add((sourceDirectory, destinationDirectory));
            BeforeMove?.Invoke(sourceDirectory, destinationDirectory);

            var fault = MoveFault?.Invoke(sourceDirectory, destinationDirectory);

            if (fault is not null)
            {
                throw fault;
            }

            _bcl.MoveDirectory(sourceDirectory, destinationDirectory);
        }

        public void DeleteDirectory(string path)
        {
            Deletes.Add(path);

            var fault = DeleteFault?.Invoke(path);

            if (fault is not null)
            {
                throw fault;
            }

            _bcl.DeleteDirectory(path);
        }
    }

    [Fact]
    public void CreateWithoutForceFailsUnchangedOnExistingOutput()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        WriteTree(output, ("payload.bin", "old"));

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: false, [Path.Combine(root.Root, "inputs", "manifest.json")], bag, CancellationToken.None);

        Assert.Null(transaction);
        Assert.True(bag.HasErrors);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputDirectoryExists, diagnostic.Code);
        Assert.Equal(GeoStage.Write, diagnostic.Stage);
        AssertFileContent(output, "payload.bin", "old");
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).StartsWith("out.tmp-"));
    }

    [Fact]
    public void ForceCommitRetainsByteExactBackupOfPreviousOutput()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var expected = Path.Combine(root.Root, "expected");
        WriteTree(output, ("a.bin", "old-a"), (Path.Combine("sub", "b.bin"), "old-b"));
        WriteTree(expected, ("a.bin", "old-a"), (Path.Combine("sub", "b.bin"), "old-b"));

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None);

        Assert.NotNull(transaction);
        WriteTree(transaction!.StageDirectory, ("payload.bin", "new"));
        Assert.True(transaction.TryCommit(CancellationToken.None));
        Assert.False(bag.HasErrors);

        AssertFileContent(output, "payload.bin", "new");
        Assert.NotNull(transaction.RetainedBackupPath);
        Assert.StartsWith("out.backup-", Path.GetFileName(transaction.RetainedBackupPath));
        Assert.True(Directory.Exists(transaction.RetainedBackupPath));
        AssertTreesMatch(expected, transaction.RetainedBackupPath);
    }

    [Fact]
    public void CancelledBeforeCommitThrowsAndPreservesOldOutput()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        WriteTree(output, ("payload.bin", "old"));

        var bag = new GeoDiagnosticBag();
        using var cts = new CancellationTokenSource();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None);

        Assert.NotNull(transaction);
        WriteTree(transaction!.StageDirectory, ("payload.bin", "new"));
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => transaction.TryCommit(cts.Token));
        Assert.Empty(bag.Items);
        Assert.Null(transaction.RetainedBackupPath);
        AssertFileContent(output, "payload.bin", "old");
        Assert.True(Directory.Exists(transaction.StageDirectory));

        transaction.Dispose();

        Assert.False(Directory.Exists(transaction.StageDirectory));
        AssertFileContent(output, "payload.bin", "old");
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).Contains("backup"));
    }

    [Fact]
    public void CancellationBetweenMovesStillFinishesCommit()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var expected = Path.Combine(root.Root, "expected");
        WriteTree(output, ("a.bin", "old-a"));
        WriteTree(expected, ("a.bin", "old-a"));

        var bag = new GeoDiagnosticBag();
        using var cts = new CancellationTokenSource();
        var io = new FaultableIo();
        io.BeforeMove = (source, _) =>
        {
            if (source == output)
            {
                cts.Cancel();
            }
        };

        var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None, io);

        Assert.NotNull(transaction);
        WriteTree(transaction!.StageDirectory, ("payload.bin", "new"));

        var committed = transaction.TryCommit(cts.Token);

        Assert.True(committed);
        Assert.True(cts.Token.IsCancellationRequested);
        Assert.False(bag.HasErrors);
        AssertFileContent(output, "payload.bin", "new");
        AssertTreesMatch(expected, transaction.RetainedBackupPath);
    }

    [Fact]
    public void FinalRenameFailureRestoresPreviousOutput()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var expected = Path.Combine(root.Root, "expected");
        WriteTree(output, ("a.bin", "old-a"), (Path.Combine("sub", "b.bin"), "old-b"));
        WriteTree(expected, ("a.bin", "old-a"), (Path.Combine("sub", "b.bin"), "old-b"));

        var bag = new GeoDiagnosticBag();
        var io = new FaultableIo();

        var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None, io);

        Assert.NotNull(transaction);
        WriteTree(transaction!.StageDirectory, ("payload.bin", "new"));
        io.MoveFault = (source, destination) =>
            source == transaction.StageDirectory && destination == output
                ? new IOException("publish failed")
                : null;

        Assert.False(transaction.TryCommit(CancellationToken.None));

        Assert.Equal(3, io.Moves.Count);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.CommitFailed, diagnostic.Code);
        Assert.IsType<IOException>(diagnostic.Detail);
        AssertTreesMatch(expected, output);
        Assert.Null(transaction.RetainedBackupPath);

        transaction.Dispose();

        Assert.False(Directory.Exists(transaction.StageDirectory));
        AssertTreesMatch(expected, output);
    }

    [Fact]
    public void RollbackFailurePreservesStageAndBackupAndReportsRecoveryPaths()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        WriteTree(output, ("a.bin", "old-a"));

        var bag = new GeoDiagnosticBag();
        var io = new FaultableIo();

        var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None, io);

        Assert.NotNull(transaction);
        WriteTree(transaction!.StageDirectory, ("payload.bin", "new"));
        io.MoveFault = (_, destination) =>
            destination == output ? new IOException("rename target busy") : null;

        Assert.False(transaction.TryCommit(CancellationToken.None));

        Assert.Equal(3, io.Moves.Count);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.RecoveryRequired, diagnostic.Code);
        Assert.IsType<IOException>(diagnostic.Detail);
        Assert.Contains(transaction.RetainedBackupPath!, diagnostic.Message);
        Assert.Contains(transaction.StageDirectory, diagnostic.Message);
        Assert.Equal(transaction.StageDirectory, transaction.RecoveryStagePath);
        Assert.True(Directory.Exists(transaction.StageDirectory));
        Assert.True(Directory.Exists(transaction.RetainedBackupPath));
        AssertFileContent(transaction.RetainedBackupPath!, "a.bin", "old-a");
        Assert.False(Directory.Exists(output));

        transaction.Dispose();

        Assert.True(Directory.Exists(transaction.StageDirectory));
        Assert.True(Directory.Exists(transaction.RetainedBackupPath));
    }

    [Fact]
    public void CreateRejectsOutputContainingProtectedInputEvenWithForce()
    {
        using var root = new ScratchRoot();
        var inputs = Path.Combine(root.Root, "inputs");
        WriteTree(inputs, ("manifest.json", "{}"));

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            inputs, force: true, [Path.Combine(inputs, "manifest.json")], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputOverlapsProtectedInput, diagnostic.Code);
        AssertFileContent(inputs, "manifest.json", "{}");
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).Contains(".tmp-"));
    }

    [Fact]
    public void CreateRejectsOutputInsideProtectedInputTreeEvenWithForce()
    {
        using var root = new ScratchRoot();
        var inputs = Path.Combine(root.Root, "inputs");
        WriteTree(inputs, ("source.tif", "tiff"));

        var bag = new GeoDiagnosticBag();
        var output = Path.Combine(inputs, "nested", "out");

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [inputs], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputOverlapsProtectedInput, diagnostic.Code);
        Assert.DoesNotContain(Directory.GetDirectories(inputs), directory =>
            Path.GetFileName(directory).Contains(".tmp-"));
    }

    [Fact]
    public void CreateResolvesSymlinkAncestorsForContainment()
    {
        using var root = new ScratchRoot();
        var real = Path.Combine(root.Root, "real");
        WriteTree(real, ("source.tif", "tiff"));
        var link = Path.Combine(root.Root, "link");
        Directory.CreateSymbolicLink(link, real);

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            link, force: true, [Path.Combine(real, "source.tif")], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputOverlapsProtectedInput, diagnostic.Code);
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).Contains(".tmp-"));
    }

    [Fact]
    public void CreateResolvesLinkedProtectedInputForContainment()
    {
        using var root = new ScratchRoot();
        var real = Path.Combine(root.Root, "real");
        WriteTree(real, ("source.tif", "tiff"));
        var linkedInput = Path.Combine(root.Root, "linked.tif");
        File.CreateSymbolicLink(linkedInput, Path.Combine(real, "source.tif"));

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            real, force: true, [linkedInput], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputOverlapsProtectedInput, diagnostic.Code);
        AssertFileContent(real, "source.tif", "tiff");
    }

    [Fact]
    public void CreateRejectsSymlinkLoopAncestorSafely()
    {
        using var root = new ScratchRoot();
        Directory.CreateDirectory(root.Root);
        var loop = Path.Combine(root.Root, "loop");
        Directory.CreateSymbolicLink(loop, loop);
        var output = Path.Combine(loop, "out");

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputPathInvalid, diagnostic.Code);
        Assert.IsType<IOException>(diagnostic.Detail);
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).Contains(".tmp-"));
    }

    [Fact]
    public void CreateRejectsDanglingAncestorLinkSafely()
    {
        using var root = new ScratchRoot();
        Directory.CreateDirectory(root.Root);
        var dangle = Path.Combine(root.Root, "dangle");
        Directory.CreateSymbolicLink(dangle, Path.Combine(root.Root, "missing"));
        var output = Path.Combine(dangle, "out");

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputPathInvalid, diagnostic.Code);
        Assert.IsType<IOException>(diagnostic.Detail);
        Assert.True(BclPayloadDirectoryIo.IsSymbolicLink(dangle));
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).Contains(".tmp-"));
    }

    [Fact]
    public void CreateAllowsUnrelatedOutputAndStagesUniqueSibling()
    {
        using var root = new ScratchRoot();
        var inputs = Path.Combine(root.Root, "inputs");
        WriteTree(inputs, ("source.tif", "tiff"));
        var output = Path.Combine(root.Root, "payload", "out");

        var firstBag = new GeoDiagnosticBag();
        var secondBag = new GeoDiagnosticBag();

        using var first = PayloadDirectoryTransaction.Create(
            output, force: false, [Path.Combine(inputs, "source.tif")], firstBag, CancellationToken.None);
        using var second = PayloadDirectoryTransaction.Create(
            output, force: false, [Path.Combine(inputs, "source.tif")], secondBag, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        var stage = first!.StageDirectory;

        Assert.Equal(Path.GetDirectoryName(output), Path.GetDirectoryName(stage));
        Assert.StartsWith("out.tmp-", Path.GetFileName(stage));
        Assert.NotEqual(stage, second!.StageDirectory);
        Assert.False(firstBag.HasErrors);

        WriteTree(stage, ("payload.bin", "new"));
        Assert.True(first.TryCommit(CancellationToken.None));
        Assert.Null(first.RetainedBackupPath);
        AssertFileContent(output, "payload.bin", "new");
    }

    [Fact]
    public void OverlapComparisonUsesPlatformCase()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var root = new ScratchRoot();
        var inputs = Path.Combine(root.Root, "inputs");
        WriteTree(inputs, ("source.tif", "tiff"));
        var distinctCaseOutput = Path.Combine(root.Root, "INPUTS");

        var allowedBag = new GeoDiagnosticBag();

        using var allowed = PayloadDirectoryTransaction.Create(
            distinctCaseOutput, force: false, [Path.Combine(inputs, "source.tif")], allowedBag, CancellationToken.None);

        Assert.NotNull(allowed);
        Assert.False(allowedBag.HasErrors);

        var rejectedBag = new GeoDiagnosticBag();

        using var rejected = PayloadDirectoryTransaction.Create(
            inputs, force: false, [Path.Combine(inputs, "source.tif")], rejectedBag, CancellationToken.None);

        Assert.Null(rejected);
        var diagnostic = Assert.Single(rejectedBag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputOverlapsProtectedInput, diagnostic.Code);
    }

    [Fact]
    public void CreateRejectsExistingFileAtOutputEvenWithForce()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        Directory.CreateDirectory(root.Root);
        File.WriteAllBytes(output, [1, 2, 3]);

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: true, [], bag, CancellationToken.None);

        Assert.Null(transaction);
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputPathIsFile, diagnostic.Code);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(output));
    }

    [Fact]
    public void CommitWithoutForceFailsUnchangedWhenOutputAppearsAfterStaging()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: false, [], bag, CancellationToken.None);

        Assert.NotNull(transaction);
        WriteTree(output, ("raced.bin", "raced"));

        Assert.False(transaction.TryCommit(CancellationToken.None));
        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.OutputDirectoryExists, diagnostic.Code);
        AssertFileContent(output, "raced.bin", "raced");

        transaction.Dispose();

        Assert.False(Directory.Exists(transaction.StageDirectory));
        AssertFileContent(output, "raced.bin", "raced");
    }

    [Fact]
    public void PreCancelledCreateThrowsWithoutCreatingAnything()
    {
        using var root = new ScratchRoot();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bag = new GeoDiagnosticBag();

        Assert.Throws<OperationCanceledException>(() => PayloadDirectoryTransaction.Create(
            Path.Combine(root.Root, "out"), force: false, [], bag, cts.Token));

        Assert.Empty(bag.Items);
        Assert.False(Directory.Exists(root.Root));
    }

    [Fact]
    public void CancelledDuringValidationThrowsBeforeStageCreation()
    {
        using var root = new ScratchRoot();
        var inputs = Path.Combine(root.Root, "inputs");
        WriteTree(inputs, ("source.tif", "tiff"));
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        using var cts = new CancellationTokenSource();
        var io = new FaultableIo();

        Assert.Throws<OperationCanceledException>(() => PayloadDirectoryTransaction.Create(
            output,
            force: false,
            CancelAfterFirstInput(cts, Path.Combine(inputs, "source.tif")),
            bag,
            cts.Token,
            io));

        Assert.Empty(bag.Items);
        Assert.Empty(io.Creates);
        Assert.DoesNotContain(Directory.GetDirectories(root.Root), directory =>
            Path.GetFileName(directory).StartsWith("out.tmp-"));
    }

    [Fact]
    public void RepeatedDisposeWarnsOnceWithoutReportingFailure()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        var io = new FaultableIo
        {
            DeleteFault = _ => new IOException("stage locked"),
        };

        var transaction = PayloadDirectoryTransaction.Create(
            output, force: false, [], bag, CancellationToken.None, io);

        Assert.NotNull(transaction);

        transaction.Dispose();
        transaction.Dispose();

        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.CleanupFailed, diagnostic.Code);
        Assert.Equal(GeoSeverity.Warning, diagnostic.Severity);
        Assert.IsType<IOException>(diagnostic.Detail);
        Assert.False(bag.HasErrors);
        Assert.True(Directory.Exists(transaction!.StageDirectory));
    }

    [Fact]
    public void CleanupStopsWithWarningWhenStageEntryIsInaccessible()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: false, [], bag, CancellationToken.None);

        Assert.NotNull(transaction);
        var stage = transaction!.StageDirectory;
        Directory.CreateDirectory(stage);
        var locked = Path.Combine(stage, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "inner.bin"), "x");
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            transaction.Dispose();
        }
        finally
        {
            File.SetUnixFileMode(
                locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var diagnostic = Assert.Single(bag.Items);
        Assert.Equal(PayloadStorageDiagnostics.CleanupFailed, diagnostic.Code);
        Assert.Equal(GeoSeverity.Warning, diagnostic.Severity);
        Assert.True(diagnostic.Detail is IOException or UnauthorizedAccessException);
        Assert.False(bag.HasErrors);
        Assert.True(Directory.Exists(stage));
    }

    [Fact]
    public void DisposeDeletesStageWithoutFollowingSymlinks()
    {
        using var root = new ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var external = Path.Combine(root.Root, "external", "target");
        WriteTree(external, ("keep.bin", "kept"));

        var bag = new GeoDiagnosticBag();

        using var transaction = PayloadDirectoryTransaction.Create(
            output, force: false, [], bag, CancellationToken.None);

        Assert.NotNull(transaction);
        var stage = transaction!.StageDirectory;
        WriteTree(stage, ("payload.bin", "new"), (Path.Combine("sub", "deep.bin"), "deep"));
        Directory.CreateSymbolicLink(Path.Combine(stage, "sub", "link"), external);

        transaction.Dispose();

        Assert.False(Directory.Exists(stage));
        AssertFileContent(external, "keep.bin", "kept");
        Assert.False(bag.HasErrors);
    }

    [Fact]
    public void CreateRejectsInvalidPaths()
    {
        using var root = new ScratchRoot();
        var bag = new GeoDiagnosticBag();

        Assert.Null(PayloadDirectoryTransaction.Create(
            "", force: false, [], bag, CancellationToken.None));
        Assert.Null(PayloadDirectoryTransaction.Create(
            "out\0bad", force: false, [], bag, CancellationToken.None));
        Assert.Null(PayloadDirectoryTransaction.Create(
            Path.GetPathRoot(root.Root)!, force: false, [], bag, CancellationToken.None));
        Assert.Null(PayloadDirectoryTransaction.Create(
            Path.Combine(root.Root, "out"), force: false, [" "], bag, CancellationToken.None));

        Assert.Equal(4, bag.Items.Count);
        Assert.All(
            bag.Items.Take(3),
            diagnostic => Assert.Equal(PayloadStorageDiagnostics.OutputPathInvalid, diagnostic.Code));
        Assert.Equal(PayloadStorageDiagnostics.ProtectedInputInvalid, bag.Items[3].Code);
        Assert.False(Directory.Exists(Path.Combine(root.Root, "out")));
    }

    private static IEnumerable<string> CancelAfterFirstInput(CancellationTokenSource cts, string first)
    {
        yield return first;
        cts.Cancel();

        yield return first + ".kml";
    }

    private static void WriteTree(string root, params (string RelativePath, string Content)[] files)
    {
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }

    private static void AssertFileContent(string root, string relativePath, string expected)
    {
        Assert.Equal(expected, File.ReadAllText(Path.Combine(root, relativePath)));
    }

    private static void AssertTreesMatch(string? expected, string? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        var expectedFiles = Directory.GetFiles(expected!, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(expected!, path))
            .Order()
            .ToArray();
        var actualFiles = Directory.GetFiles(actual!, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(actual!, path))
            .Order()
            .ToArray();

        Assert.Equal(expectedFiles, actualFiles);

        foreach (var relativePath in expectedFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(expected!, relativePath)),
                File.ReadAllBytes(Path.Combine(actual!, relativePath)));
        }
    }
}
