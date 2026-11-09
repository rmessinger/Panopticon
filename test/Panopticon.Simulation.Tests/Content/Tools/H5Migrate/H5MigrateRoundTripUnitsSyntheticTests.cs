#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Tools.H5Migrate;
using Xunit;

namespace Panopticon.Tests.Content.Tools.H5Migrate;

/// <summary>
/// Round-trip validator tests for WI-H5-03 in units mode over a synthetic roster: an identical package
/// passes, and each unit failure class is reported with its named code through the JSON report.
/// </summary>
public sealed class H5MigrateRoundTripUnitsSyntheticTests
{
    private const string TypedMismatchCode = "H5-CLI-006";

    private const string RetainedDigestCode = "H5-CLI-007";

    private const string MissingRecordCode = "H5-CLI-013";

    private const string ExtraRecordCode = "H5-CLI-014";

    private const string TemplateMismatchCode = "H5-CLI-015";

    [Fact]
    public void ValidateUnits_IdenticalPackage_Passes()
    {
        WithPackage(package =>
        {
            var report = Validate(package, package.PackageDir);

            Assert.Equal("units", report.Mode);
            AssertPassed(report);
        });
    }

    [Fact]
    public void ValidateUnits_TamperedTypedField_FailsAsTypedFieldMismatch()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.EditRecordPayload(package.PackageDir, SyntheticUnitPackage.RootName, payload =>
            {
                payload["maxHealth"]!["value"]!["value"] = 200.0;
            });

            AssertReported(Validate(package, package.PackageDir), TypedMismatchCode, RoundTripFailureClass.TypedFieldMismatch);
        });
    }

    [Fact]
    public void ValidateUnits_TamperedRetainedBlock_FailsAsRetainedHashMismatch()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.EditRetainedBlock(package.PackageDir, SyntheticUnitPackage.RootName, text => text + "; round-trip tamper\n");

            AssertReported(Validate(package, package.PackageDir), RetainedDigestCode, RoundTripFailureClass.RetainedHashMismatch);
        });
    }

    [Fact]
    public void ValidateUnits_DroppedRecord_FailsAsMissingRecord()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.DropRecord(package.PackageDir, SyntheticUnitPackage.WeaponName);

            AssertReported(Validate(package, package.PackageDir), MissingRecordCode, RoundTripFailureClass.MissingRecord);
        });
    }

    [Fact]
    public void ValidateUnits_DuplicatedRecord_FailsAsExtraRecord()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.DuplicateRecord(package.PackageDir, SyntheticUnitPackage.WeaponName);

            AssertReported(Validate(package, package.PackageDir), ExtraRecordCode, RoundTripFailureClass.ExtraRecord);
        });
    }

    [Fact]
    public void ValidateUnits_RetainedTemplateEdit_FailsAsMaterializedTemplateMismatch()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.EditRetainedBlock(package.PackageDir, SyntheticUnitPackage.RootName, text => text.Replace("MaxHealth = 180.0", "MaxHealth = 200.0", StringComparison.Ordinal));

            var report = Validate(package, package.PackageDir);

            AssertReported(report, TemplateMismatchCode, RoundTripFailureClass.MaterializedTemplateMismatch);
            Assert.Contains(JsonFindings(report), f => f.Code == TemplateMismatchCode && f.Section == "templates" && f.Subject == SyntheticUnitPackage.RootName);

            // The INI edit also changes the retained bytes, so the digest class accompanies the template class.
            Assert.Contains(JsonFindings(report), f => f.Code == RetainedDigestCode);
        });
    }

    [Fact]
    public void ValidateUnits_ProvenanceArchiveLayerAbsentOnOneSide_IsPermitted()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.EditRecordPayload(package.PackageDir, SyntheticUnitPackage.RootName, payload =>
            {
                payload["provenance"]!["archiveLayer"] = "Base";
            });

            AssertPassed(Validate(package, package.PackageDir));
        });
    }

    [Fact]
    public void ValidateUnits_ProvenanceLineChange_FailsAsTypedFieldMismatch()
    {
        WithPackage(package =>
        {
            UnitPackageTamper.EditRecordPayload(package.PackageDir, SyntheticUnitPackage.RootName, payload =>
            {
                var provenance = payload["provenance"]!;
                provenance["line"] = provenance["line"]!.GetValue<int>() + 1;
            });

            AssertReported(Validate(package, package.PackageDir), TypedMismatchCode, RoundTripFailureClass.TypedFieldMismatch);
        });
    }

    private static void WithPackage(Action<SyntheticUnitPackage.Converted> body)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));

        try
        {
            body(SyntheticUnitPackage.ConvertToPackage(Path.Combine(tempRoot, "package")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static RoundTripReport Validate(SyntheticUnitPackage.Converted package, string packageDir)
    {
        return RoundTripValidator.ValidateUnits(package.Resolution, package.Natives, package.Provider, packageDir);
    }

    private static void AssertPassed(RoundTripReport report)
    {
        Assert.True(report.Passed, string.Join("; ", report.Sections.SelectMany(s => s.Findings).Select(f => $"{f.Code} {f.Message}")));
        Assert.Empty(JsonFindings(report));
    }

    private static void AssertReported(RoundTripReport report, string code, RoundTripFailureClass failureClass)
    {
        Assert.False(report.Passed);
        Assert.Contains(JsonFindings(report), f => f.Code == code && f.FailureClass == failureClass.ToString());
    }

    private static List<ReportedFinding> JsonFindings(RoundTripReport report)
    {
        using var document = JsonDocument.Parse(RoundTripReportWriter.ToJson(report));

        return document.RootElement.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("findings").EnumerateArray().Select(finding => new ReportedFinding(
                finding.GetProperty("code").GetString() ?? string.Empty,
                finding.GetProperty("failureClass").GetString(),
                finding.GetProperty("section").GetString() ?? string.Empty,
                finding.GetProperty("subject").GetString() ?? string.Empty)))
            .ToList();
    }

    private sealed record ReportedFinding(string Code, string? FailureClass, string Section, string Subject);
}
