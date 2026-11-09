#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Packages;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Units.Packages;

/// <summary>Negative tests: tampering with a written package is caught when it is re-read.</summary>
public sealed class UnitPackageWriterSecurityTests : IDisposable
{
    private const string RetainedIniContent =
        """
        Weapon RangerRifle
          PrimaryDamage = 5.0
          DamageType = SMALL_ARMS
          AttackRange = 100.0
        End

        Object Ranger
          VisionRange = 150
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryHeight = 12.0
        End
        """;

    private readonly string _rootDir;

    public UnitPackageWriterSecurityTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "unit_pkg_sec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootDir, recursive: true);
        }
        catch
        {
            // Ignore temp dir cleanup errors
        }
    }

    [Fact]
    public void TamperedPayloadByteIsCaughtOnReread()
    {
        var target = WriteRangerPackage();

        // Flip one byte in place so the declared byte length still matches and the digest check runs.
        var payloadPath = Path.Combine(target, "records", "units", "Ranger.json");
        var text = File.ReadAllText(payloadPath);
        File.WriteAllText(payloadPath, "X" + text[1..]);

        var set = new H4UnitPackageReader(target).Read([]);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Code == UnitPackageDiagnosticCodes.DigestMismatch);
    }

    [Fact]
    public void TamperedManifestDigestIsCaughtOnReread()
    {
        var target = WriteRangerPackage();

        var manifestPath = Path.Combine(target, UnitPackageFormat.ManifestFileName);
        var manifest = File.ReadAllText(manifestPath);
        var withSwappedDigest = SwapSha256(manifest, "records/units/Ranger.json");
        Assert.NotEqual(manifest, withSwappedDigest);
        File.WriteAllText(manifestPath, withSwappedDigest);

        var set = new H4UnitPackageReader(target).Read([]);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.IsError);
    }

    [Fact]
    public void TamperedManifestDialectIsCaughtOnReread()
    {
        var target = WriteRangerPackage();

        var manifestPath = Path.Combine(target, UnitPackageFormat.ManifestFileName);
        var manifest = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, manifest.Replace(
            $"\"dialect\": \"{UnitPackageFormat.SupportedGeneralsIniDialect}\"",
            "\"dialect\": \"unknown-ini\""));

        var set = new H4UnitPackageReader(target).Read([]);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Code == UnitPackageDiagnosticCodes.UnsupportedDialect);
    }

    [Fact]
    public void TamperedManifestRecordPathIsCaughtOnReread()
    {
        var target = WriteRangerPackage();

        var manifestPath = Path.Combine(target, UnitPackageFormat.ManifestFileName);
        var manifest = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, manifest.Replace(
            "\"path\": \"records/units/Ranger.json\"",
            "\"path\": \"../outside/Ranger.json\""));

        var set = new H4UnitPackageReader(target).Read([]);

        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Contains(set.Diagnostics, diagnostic => diagnostic.Code == UnitPackageDiagnosticCodes.PathInvalid);
    }

    [Fact]
    public void WrittenPackageHasNoUnknownManifestMembersOrPayloadWarnings()
    {
        var target = WriteRangerPackage();

        var (manifest, diagnostics) = UnitPackageManifestReader.ParseAndValidate(
            File.ReadAllText(Path.Combine(target, UnitPackageFormat.ManifestFileName)));

        Assert.NotNull(manifest);
        Assert.Empty(diagnostics);
        Assert.Equal(UnitPackageFormat.ContainerKind, manifest.ContainerKind);
        Assert.Equal(UnitPackageFormat.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("generals", manifest.Source!.Id);
        Assert.Equal(2, manifest.Records.Count);
        Assert.Equal(2, manifest.RetainedPayloads.Count);

        var set = new H4UnitPackageReader(target).Read([]);

        // The writer emits only members the reader knows, so no unknown-field or unknown-member
        // warnings appear on a clean re-read.
        Assert.Empty(set.Diagnostics);
    }

    [Fact]
    public void WrittenPayloadsNeverContainBindingHandles()
    {
        var target = WriteRangerPackage();

        var payloadFiles = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != UnitPackageFormat.ManifestFileName);

        foreach (var path in payloadFiles)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("\"handle\"", text);
            Assert.DoesNotContain("\"legacy\":", text);
            Assert.DoesNotContain("Owner", text);
        }
    }

    private string WriteRangerPackage()
    {
        var source = ContentSource.Generals;

        ContentId Id(ContentKind kind, string name) => new(source, kind, name);

        var unit = new UnitRecord(Id(ContentKind.Unit, "Ranger"), new Provenance("generals", "Data/INI/Ranger.ini", 30))
        {
            Legacy = new LegacyBinding(source, 4),
            VisionRange = Authored.Of(new Quantity(150, QuantityUnit.GameDistance)),
        };

        var weapon = new WeaponRecord(Id(ContentKind.Weapon, "RangerRifle"), new Provenance("generals", "Data/INI/Ranger.ini", 60))
        {
            Legacy = new LegacyBinding(source, 5),
            PrimaryDamage = Authored.Of(new Quantity(5, QuantityUnit.HitPoints)),
        };

        var resolution = new UnitImportResolution(
            source,
            [unit.Id],
            [unit, weapon],
            []);

        var target = Path.Combine(_rootDir, "package");
        var result = H4UnitPackageWriter.Write(
            target,
            resolution,
            new StaticProvider(UnitPackageFormat.SupportedGeneralsIniDialect, UnitPackageFormat.SupportedGeneralsIniDialectVersion, RetainedIniContent));

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return target;
    }

    private static string SwapSha256(string manifestJson, string entryPath)
    {
        var lines = manifestJson.Split('\n');
        var inEntry = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains($"\"path\": \"{entryPath}\""))
            {
                inEntry = true;
                continue;
            }

            if (inEntry && lines[i].Contains("\"sha256\": \""))
            {
                var start = lines[i].IndexOf("\"sha256\": \"", StringComparison.Ordinal) + "\"sha256\": \"".Length;
                var end = lines[i].IndexOf('"', start);
                var digest = lines[i][start..end];
                var replacement = digest == "0" + digest[1..] ? "1" + digest[1..] : "0" + digest[1..];
                lines[i] = lines[i][..start] + replacement + lines[i][end..];
                return string.Join('\n', lines);
            }
        }

        throw new InvalidOperationException($"No sha256 entry found after '{entryPath}'.");
    }

    private sealed class StaticProvider(string dialect, int dialectVersion, string iniText) : IUnitPackageRetainedPayloadProvider
    {
        public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
        {
            return new UnitPackageRetainedBlock(
                dialect,
                dialectVersion,
                Encoding.UTF8.GetBytes(iniText),
                new UnitPackageProvenance("generals", "Data/INI/Test.ini", 1));
        }
    }
}
