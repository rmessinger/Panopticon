#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

public sealed class UnitPackageSecurityTests
{
    [Fact]
    public void OversizeManifestIsRejectedWithResourceLimitDiagnostic()
    {
        using var builder = new UnitPackageTestBuilder();
        var packageDir = builder.Build();

        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);

        // Create an oversized manifest (> 64 MiB) using sparse file
        using (var fs = new FileStream(manifestPath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(UnitPackageFormat.MaxManifestBytes + 1);
        }

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.ResourceLimitExceeded, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void OversizeManifest_ProbeStaysBoundedAndReadFailsTyped()
    {
        using var builder = new UnitPackageTestBuilder();
        var packageDir = builder.Build();

        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);

        // Create an oversized manifest (> 64 MiB) using sparse file
        using (var fs = new FileStream(manifestPath, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(UnitPackageFormat.MaxManifestBytes + 1);
        }

        var reader = new H4UnitPackageReader(packageDir);

        // The bounded probe never reads the oversized manifest and falls back to the unknown source.
        Assert.Equal("unknown", reader.Source.Id);

        var set = reader.Read([]);

        var diag = Assert.Single(set.Diagnostics, d => d.Code == UnitPackageDiagnosticCodes.ResourceLimitExceeded);
        Assert.True(diag.IsError);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void TraversalPathInRecordIsRejected()
    {
        using var builder = new UnitPackageTestBuilder();
        var packageDir = builder.Build();

        // Craft a manifest containing traversal path
        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);
        var manifestObj = new
        {
            containerKind = UnitPackageFormat.ContainerKind,
            schemaVersion = UnitPackageFormat.CurrentSchemaVersion,
            source = new { id = "generals", caseSensitiveNames = false },
            records = new[]
            {
                new
                {
                    name = "EscapedUnit",
                    kind = "Unit",
                    path = "../outside.json",
                    byteLength = 10,
                    sha256 = new string('a', 64),
                },
            },
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObj));

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var errorCodes = set.Diagnostics.Where(d => d.IsError).Select(d => d.Code).ToList();
        Assert.True(errorCodes.Contains(UnitPackageDiagnosticCodes.PathInvalid) || errorCodes.Contains(UnitPackageDiagnosticCodes.PathEscape));
        Assert.Empty(set.Records);
    }

    [Fact]
    public void FileLengthMismatchIsRejectedWithLengthMismatch()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();

        // Append bytes to payload file so its length exceeds declared byteLength
        var payloadPath = Path.Combine(packageDir, "records", "ranger.json");
        File.AppendAllText(payloadPath, "   ");

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.LengthMismatch, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void SymlinkInPayloadPathIsRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Symlinks require elevated privileges on Windows
        }

        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("LegitUnit", ContentKind.Unit, "records/legit.json",
            """
            {
              "name": "LegitUnit",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();

        // Create a symlink under the package pointing outside or inside
        var targetFile = Path.Combine(packageDir, "records", "legit.json");
        var linkDir = Path.Combine(packageDir, "linked");
        Directory.CreateDirectory(linkDir);
        var linkPath = Path.Combine(linkDir, "symlink.json");

        try
        {
            File.CreateSymbolicLink(linkPath, targetFile);
        }
        catch
        {
            // If the environment does not permit symlink creation, skip
            return;
        }

        // Alter manifest to reference the symlink
        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);
        var manifestObj = new
        {
            containerKind = UnitPackageFormat.ContainerKind,
            schemaVersion = UnitPackageFormat.CurrentSchemaVersion,
            source = new { id = "generals", caseSensitiveNames = false },
            records = new[]
            {
                new
                {
                    name = "SymlinkedUnit",
                    kind = "Unit",
                    path = "linked/symlink.json",
                    byteLength = new FileInfo(targetFile).Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(targetFile))).ToLowerInvariant(),
                },
            },
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObj));

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.SymlinkRejected, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void DigestTamperIsRejectedWithDigestMismatch()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();

        // Tamper with payload file content after manifest was written with original digest
        var payloadPath = Path.Combine(packageDir, "records", "ranger.json");
        File.WriteAllText(payloadPath,
            """
            {
              "name": "RangerTampered",
              "kind": "Unit"
            }
            """);

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var errorCodes = set.Diagnostics.Where(d => d.IsError).Select(d => d.Code).ToList();
        Assert.True(errorCodes.Contains(UnitPackageDiagnosticCodes.DigestMismatch) || errorCodes.Contains(UnitPackageDiagnosticCodes.LengthMismatch));
        Assert.Empty(set.Records);
    }

    [Fact]
    public void MissingPayloadFileIsRejected()
    {
        using var builder = new UnitPackageTestBuilder();
        builder.AddRecord("Ranger", ContentKind.Unit, "records/ranger.json",
            """
            {
              "name": "Ranger",
              "kind": "Unit"
            }
            """);

        var packageDir = builder.Build();

        // Delete payload file
        var payloadPath = Path.Combine(packageDir, "records", "ranger.json");
        File.Delete(payloadPath);

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        var diag = set.Diagnostics.First(d => d.IsError);
        Assert.Equal(UnitPackageDiagnosticCodes.FileMissing, diag.Code);
        Assert.Empty(set.Records);
    }

    [Fact]
    public void DiagnosticsCountExceedingLimitIsCappedWithAggregate()
    {
        using var builder = new UnitPackageTestBuilder();
        var packageDir = builder.Build();

        // Create manifest with 150 invalid records
        var recordEntries = Enumerable.Range(0, 150).Select(i => new
        {
            name = $"Unit{i}",
            kind = "Unit",
            path = $"records/unit{i}.json",
            byteLength = 10,
            sha256 = new string('z', 64), // invalid sha256 (not hex)
        }).ToArray();

        var manifestObj = new
        {
            containerKind = UnitPackageFormat.ContainerKind,
            schemaVersion = UnitPackageFormat.CurrentSchemaVersion,
            source = new { id = "generals", caseSensitiveNames = false },
            records = recordEntries,
        };

        var manifestPath = Path.Combine(packageDir, UnitPackageFormat.ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifestObj));

        var reader = new H4UnitPackageReader(packageDir);
        var set = reader.Read([]);

        Assert.Contains(set.Diagnostics, d => d.IsError);
        Assert.Equal(UnitPackageFormat.MaxDiagnosticCount + 1, set.Diagnostics.Count);
        Assert.Equal(UnitPackageDiagnosticCodes.DiagnosticCapExceeded, set.Diagnostics.Last().Code);
    }
}
