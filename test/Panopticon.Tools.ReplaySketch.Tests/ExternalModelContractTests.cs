#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Content.Importing;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class ExternalModelContractTests
{
    public static TheoryData<ExternalDiagnosticStage, string> InvalidDataStageCodes => new()
    {
        { ExternalDiagnosticStage.Selection, "EXT002" },
        { ExternalDiagnosticStage.Resolve, "EXT020" },
        { ExternalDiagnosticStage.Parse, "GLTF010" },
        { ExternalDiagnosticStage.Convert, "GLTF020" },
        { ExternalDiagnosticStage.Validate, "IMP010" },
        { ExternalDiagnosticStage.Materialize, "PBR010" },
        { ExternalDiagnosticStage.Bind, "ANIM010" },
        { ExternalDiagnosticStage.Render, "GPU010" },
    };

    public static TheoryData<ExternalDiagnosticStage, string> NotSupportedStageCodes => new()
    {
        { ExternalDiagnosticStage.Selection, "EXT003" },
        { ExternalDiagnosticStage.Resolve, "EXT021" },
        { ExternalDiagnosticStage.Parse, "GLTF011" },
        { ExternalDiagnosticStage.Convert, "GLTF021" },
        { ExternalDiagnosticStage.Validate, "IMP011" },
        { ExternalDiagnosticStage.Materialize, "PBR011" },
        { ExternalDiagnosticStage.Bind, "ANIM011" },
        { ExternalDiagnosticStage.Render, "GPU011" },
    };

    [Theory]
    [MemberData(nameof(InvalidDataStageCodes))]
    public void Map_InvalidData_UsesStageTable(ExternalDiagnosticStage stage, string expectedCode)
    {
        var diagnostic = ExternalModelDiagnosticMapper.Map(
            stage,
            ExternalDiagnosticSourceRole.Model,
            "node hull",
            new InvalidDataException("malformed"));

        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(ExternalDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(stage, diagnostic.Stage);
        Assert.Equal("malformed", diagnostic.Message);
        Assert.Null(diagnostic.ExceptionDetail);
    }

    [Theory]
    [MemberData(nameof(NotSupportedStageCodes))]
    public void Map_NotSupported_UsesStageTable(ExternalDiagnosticStage stage, string expectedCode)
    {
        var diagnostic = ExternalModelDiagnosticMapper.Map(
            stage,
            ExternalDiagnosticSourceRole.Animation,
            null,
            new NotSupportedException("unsupported"));

        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(ExternalDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("unsupported", diagnostic.Message);
        Assert.Null(diagnostic.ExceptionDetail);
    }

    [Fact]
    public void Map_UnexpectedException_BecomesExt999_WithLocalDetail()
    {
        var exception = new InvalidOperationException("driver exploded at /tmp/secret");

        var diagnostic = ExternalModelDiagnosticMapper.Map(
            ExternalDiagnosticStage.Render,
            ExternalDiagnosticSourceRole.Model,
            null,
            exception);

        Assert.Equal(ExternalModelDiagnosticMapper.InternalFailureCode, diagnostic.Code);
        Assert.Equal(ExternalDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Same(exception, diagnostic.ExceptionDetail);
        Assert.Contains(nameof(InvalidOperationException), diagnostic.Message);
    }

    [Fact]
    public void SortAndCollapse_OrdersBySeverityThenStageThenSourceThenContextThenCode()
    {
        var warningBind = new ExternalModelDiagnostic(
            "ANIM100", ExternalDiagnosticSeverity.Warning, ExternalDiagnosticStage.Bind,
            ExternalDiagnosticSourceRole.Model, null, "ignored");
        var errorParseA = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node a", "bad");
        var errorParseB = new ExternalModelDiagnostic(
            "GLTF005", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node a", "worse");
        var errorParseAnim = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Animation, "node a", "bad");
        var infoSelection = new ExternalModelDiagnostic(
            "EXT050", ExternalDiagnosticSeverity.Info, ExternalDiagnosticStage.Selection,
            ExternalDiagnosticSourceRole.Model, null, "note");

        var sorted = ExternalModelDiagnosticMapper.SortAndCollapse(
            [
                warningBind,
                infoSelection,
                errorParseAnim,
                errorParseB,
                errorParseA,
            ]);

        Assert.Equal(
            [
                errorParseB,
                errorParseA,
                errorParseAnim,
                warningBind,
                infoSelection,
            ],
            sorted);
    }

    [Fact]
    public void SortAndCollapse_CollapsesDuplicates_WithOccurrenceCount()
    {
        var original = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node a", "bad");
        var sameIdentity = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node a", "bad", occurrenceCount: 3);
        var otherMessage = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node z", "different");

        var sorted = ExternalModelDiagnosticMapper.SortAndCollapse(
            [
                otherMessage,
                sameIdentity,
                original,
            ]);

        Assert.Equal(2, sorted.Count);
        Assert.Equal(4, sorted[0].OccurrenceCount);
        Assert.Equal("different", sorted[1].Message);
    }

    [Fact]
    public void Eligibility_WarningsNeverBlock()
    {
        var warnings = new[]
        {
            new ExternalModelDiagnostic(
                "GLTF0001", ExternalDiagnosticSeverity.Warning, ExternalDiagnosticStage.Parse,
                ExternalDiagnosticSourceRole.Model, null, "ignored extension"),
        };

        var eligibility = PreviewEligibility.Evaluate(warnings);

        Assert.True(eligibility.CanRender);
        Assert.Empty(eligibility.BlockingDiagnosticCodes);
        Assert.Empty(eligibility.MissingCapabilities);
    }

    [Fact]
    public void Eligibility_ErrorsBlock_WithDistinctSortedCodes()
    {
        var errors = new[]
        {
            new ExternalModelDiagnostic(
                "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
                ExternalDiagnosticSourceRole.Model, null, "bad"),
            new ExternalModelDiagnostic(
                "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
                ExternalDiagnosticSourceRole.Model, "node a", "bad"),
            new ExternalModelDiagnostic(
                "ANIM010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Bind,
                ExternalDiagnosticSourceRole.Animation, null, "unbound"),
        };

        var eligibility = PreviewEligibility.Evaluate(errors);

        Assert.False(eligibility.CanRender);
        Assert.Equal(["ANIM010", "GLTF010"], eligibility.BlockingDiagnosticCodes);
    }

    [Fact]
    public void Eligibility_MissingCapabilitiesBlock()
    {
        var eligibility = PreviewEligibility.Evaluate(
            [],
            ["MorphTargets"]);

        Assert.False(eligibility.CanRender);
        Assert.Equal(["MorphTargets"], eligibility.MissingCapabilities);
    }

    private static ExternalModelReport BuildReport()
    {
        var catalog = new ExternalAssetCatalog(
            "tank.gltf",
            "GLTF",
            1, 1, 1, 3, 3, 1, 1, 0, 0,
            new ExternalModelBounds(
                new System.Numerics.Vector3(0, 0, 0),
                new System.Numerics.Vector3(1, 1, 1),
                new System.Numerics.Vector3(0.5f, 0.5f, 0.5f),
                0.8660254f),
            ["KHR_texture_transform"],
            [],
            [new ExternalAnimationDescriptor("models/tank.gltf#animation=0", "drive", TimeSpan.FromSeconds(2), 1)],
            []);

        var diagnostic = new ExternalModelDiagnostic(
            "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
            ExternalDiagnosticSourceRole.Model, "node hull", "bad accessor",
            exceptionDetail: new InvalidOperationException("secret stack at /home/user"));

        var diagnostics = new[]
        {
            diagnostic,
            new ExternalModelDiagnostic(
                "GLTF010", ExternalDiagnosticSeverity.Error, ExternalDiagnosticStage.Parse,
                ExternalDiagnosticSourceRole.Model, "node hull", "bad accessor", occurrenceCount: 3),
        };

        return new ExternalModelReport(
            new ExternalSourceIdentity("tank.gltf", "models/tank.gltf", 102, "ABCD"),
            null,
            catalog,
            diagnostics,
            PreviewEligibility.Evaluate(diagnostics));
    }

    [Fact]
    public void Report_Json_IsVersioned_AndRoundTrips()
    {
        var report = BuildReport();

        var json = ExternalModelReportJson.Serialize(report);
        var document = ExternalModelReportJson.Parse(json);

        var root = document.RootElement;
        Assert.Equal(ExternalModelReport.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("tank.gltf", root.GetProperty("modelSource").GetProperty("displayName").GetString());
        Assert.Equal("ABCD", root.GetProperty("modelSource").GetProperty("contentHash").GetString());
        Assert.Equal(3, root.GetProperty("catalog").GetProperty("vertexCount").GetInt64());
        Assert.Equal("drive", root.GetProperty("catalog").GetProperty("animations")[0].GetProperty("name").GetString());
        Assert.Equal(2.0, root.GetProperty("catalog").GetProperty("animations")[0].GetProperty("durationSeconds").GetDouble());
        Assert.Equal("GLTF010", root.GetProperty("diagnostics")[0].GetProperty("code").GetString());
        Assert.Equal(4, root.GetProperty("diagnostics")[0].GetProperty("occurrenceCount").GetInt32());
        Assert.False(root.GetProperty("eligibility").GetProperty("canRender").GetBoolean());
        Assert.Equal(["GLTF010"], root.GetProperty("eligibility").GetProperty("blockingDiagnosticCodes")
            .EnumerateArray().Select(e => e.GetString() ?? "").ToArray());
    }

    [Fact]
    public void Report_Json_OmitsForbiddenFields()
    {
        var report = BuildReport();

        var json = ExternalModelReportJson.Serialize(report);

        Assert.DoesNotContain("exceptionDetail", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/user", json, StringComparison.Ordinal);

        var modelSourceProperties = ExternalModelReportJson
            .Parse(json)
            .RootElement
            .GetProperty("modelSource")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["displayName", "virtualKey", "byteLength", "contentHash"], modelSourceProperties);
    }

    [Fact]
    public void SourceIdentity_ComputesStableSha256()
    {
        using var contentA = new MemoryStream(new byte[] { 1, 2, 3 });
        using var contentB = new MemoryStream(new byte[] { 1, 2, 3 });
        using var contentC = new MemoryStream(new byte[] { 1, 2, 4 });

        var identityA = ExternalSourceIdentity.Create("a.gltf", "a.gltf", contentA);
        var identityB = ExternalSourceIdentity.Create("b.gltf", "b.gltf", contentB);
        var identityC = ExternalSourceIdentity.Create("c.gltf", "c.gltf", contentC);

        Assert.Equal(3, identityA.ByteLength);
        Assert.Equal(identityA.ContentHash, identityB.ContentHash);
        Assert.NotEqual(identityA.ContentHash, identityC.ContentHash);
        Assert.Equal(64, identityA.ContentHash.Length);
    }

    [Fact]
    public void SourceIdentity_HashRestoresStreamPosition()
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        content.Position = 2;

        ExternalSourceIdentity.Create("a.gltf", "a.gltf", content);

        Assert.Equal(2, content.Position);
    }
}
