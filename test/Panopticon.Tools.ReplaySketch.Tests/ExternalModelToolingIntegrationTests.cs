using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Panopticon.Content.Importing;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;
using Panopticon.Tools.TestHarness.ExternalModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// C5 acceptance integrations over the committed C4 fixture matrix: static PBR with a
/// relative external image isolated to the model root, skinned capture with fixed sample
/// times, failure reporting, and same/separate-document plus report-only request paths.
/// Graphics-backed cases return without asserting when no Generals installation or
/// headless graphics backend is available, matching ScenarioCaptureRunnerTests.
/// </summary>
public sealed class ExternalModelToolingIntegrationTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _modelRoot;
    private readonly string _animationRoot;
    private readonly List<string> _outputDirs = [];

    public ExternalModelToolingIntegrationTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "panopticon-c5-int-" + Guid.NewGuid().ToString("N"));
        _modelRoot = Path.Combine(_baseDir, "modelroot");
        _animationRoot = Path.Combine(_baseDir, "animroot");
        Directory.CreateDirectory(_modelRoot);
        Directory.CreateDirectory(_animationRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_baseDir, true);
    }

    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Content", "Importing", "Gltf", "Fixtures");

    private static void CopyFixture(string fileName, string destinationRoot)
    {
        File.Copy(
            Path.Combine(FixtureRoot, fileName),
            Path.Combine(destinationRoot, fileName),
            overwrite: true);
    }

    private string NewOutputDir()
    {
        var dir = Path.Combine(_baseDir, "out" + _outputDirs.Count);
        Directory.CreateDirectory(dir);
        _outputDirs.Add(dir);
        return dir;
    }

    private ExternalModelRequest Request(
        string outputDir,
        string modelKey,
        string? animationKey = null,
        ExternalModelSourceRequest? animationSource = null,
        string? image = "preview.png",
        float sampleTime = 0f)
    {
        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = 1,
            ModelSource = new ExternalModelSourceRequest(
                _modelRoot,
                modelKey,
                ExternalAssetInspection.GltfFormatId),
            AnimationSource = animationSource,
            ImportOptions = ExternalModelImportOptions.Default,
            PreviewOptions = ExternalModelPreviewOptions.Default with
            {
                SelectedAnimationKey = animationKey,
                AnimationSampleTime = animationKey is null ? 0 : sampleTime,
            },
            Viewport = new ExternalModelViewport(96, 96),
            OutputImagePath = image is null ? null : Path.Combine(outputDir, image),
            OutputReportPath = Path.Combine(outputDir, "report.json"),
        };
    }

    private static ExternalModelHostResult Render(ExternalModelRequest request)
    {
        return new ExternalModelHost(new SilentReporter(), new ExternalModelSceneRenderer())
            .RunRequest(request);
    }

    private static bool InstallationAvailable()
    {
        return new GeneralsInstallationService().TryGetInstallation(out _);
    }

    private sealed class SilentReporter : ITestHarnessReporter
    {
        public void Error(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void ActionDispatched(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(Panopticon.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(Panopticon.Logic.OutcomeStatus status, Panopticon.Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }

    private static string ReportHash(JsonDocument document)
    {
        var hash = document.RootElement.GetProperty("renderMetadata").GetProperty("pngContentHash").GetString();
        Assert.NotNull(hash);
        return hash!;
    }

    [Fact]
    public void StaticPbr_ExternalRelativeImage_ResolvesOnlyThroughModelRoot()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        foreach (var file in new[]
                 {
                     "pbr-materials.gltf",
                     "pbr-materials.bin",
                     "pbr-materials_0.png",
                     "pbr-materials_1.jpg",
                 })
        {
            CopyFixture(file, _modelRoot);
        }

        var output = NewOutputDir();
        var request = Request(output, "pbr-materials.gltf");

        var result = Render(request);
        Assert.Equal(0, result.ExitCode);

        var imagePath = Path.Combine(output, "preview.png");
        Assert.True(File.Exists(imagePath));
        using (var document = ExternalModelReportJson.Parse(File.ReadAllText(Path.Combine(output, "report.json"))))
        {
            Assert.True(document.RootElement.GetProperty("eligibility").GetProperty("canRender").GetBoolean());
            Assert.Equal(
                ExternalModelArtifactWriter.ComputeFileHash(imagePath),
                ReportHash(document));
        }

        // Removing the external image from the model root must break materialization: the
        // relative path can only resolve through the external filesystem, never from the
        // game installation.
        File.Delete(Path.Combine(_modelRoot, "pbr-materials_0.png"));
        var missingOutput = NewOutputDir();
        var missingResult = Render(Request(missingOutput, "pbr-materials.gltf"));

        Assert.Equal(5, missingResult.ExitCode);
        Assert.False(File.Exists(Path.Combine(missingOutput, "preview.png")));
    }

    [Fact]
    public void SkinnedFixture_FixedSampleTimes_AreDeterministic()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        CopyFixture("animated-skin.glb", _modelRoot);

        var t0Output = NewOutputDir();
        var t0Repeat = NewOutputDir();
        var t1Output = NewOutputDir();

        var t0 = Render(Request(t0Output, "animated-skin.glb", "animated-skin.glb#animation=0"));
        Assert.Equal(0, t0.ExitCode);

        var t0Again = Render(Request(t0Repeat, "animated-skin.glb", "animated-skin.glb#animation=0"));
        Assert.Equal(0, t0Again.ExitCode);

        var t1 = Render(Request(
            t1Output,
            "animated-skin.glb",
            "animated-skin.glb#animation=0",
            sampleTime: 1f));
        Assert.Equal(0, t1.ExitCode);

        string hash0, hash0Repeat, hash1;
        using (var document = ExternalModelReportJson.Parse(File.ReadAllText(Path.Combine(t0Output, "report.json"))))
        {
            hash0 = ReportHash(document);
            Assert.Equal("animated-skin.glb#animation=0",
                document.RootElement.GetProperty("renderMetadata").GetProperty("animationKey").GetString());
            Assert.Equal(0f,
                document.RootElement.GetProperty("renderMetadata").GetProperty("animationSampleTimeSeconds").GetSingle(),
                5);
        }

        using (var document = ExternalModelReportJson.Parse(File.ReadAllText(Path.Combine(t0Repeat, "report.json"))))
        {
            hash0Repeat = ReportHash(document);
        }

        using (var document = ExternalModelReportJson.Parse(File.ReadAllText(Path.Combine(t1Output, "report.json"))))
        {
            hash1 = ReportHash(document);
            Assert.Equal(1f,
                document.RootElement.GetProperty("renderMetadata").GetProperty("animationSampleTimeSeconds").GetSingle(),
                5);
        }

        Assert.Equal(hash0, hash0Repeat);
        Assert.NotEqual(hash0, hash1);
    }

    [Fact]
    public void BlockedImport_PreservesFailureReport_NoPartialPng()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        CopyFixture("unsupported-required-extension.gltf", _modelRoot);
        var output = NewOutputDir();

        var result = Render(Request(output, "unsupported-required-extension.gltf"));

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, d => d.Severity == ExternalDiagnosticSeverity.Error);

        var reportPath = Path.Combine(output, "report.json");
        Assert.True(File.Exists(reportPath));
        Assert.False(File.Exists(Path.Combine(output, "preview.png")));
        Assert.Empty(Directory.GetFiles(output, "*" + ExternalModelArtifactWriter.StagingSuffix));

        var reportJson = File.ReadAllText(reportPath);
        Assert.DoesNotContain("staging", reportJson, StringComparison.Ordinal);
        Assert.False(ExternalModelReportJson.Parse(reportJson).RootElement
            .GetProperty("eligibility").GetProperty("canRender").GetBoolean());
    }

    [Fact]
    public void SeparateDocumentAnimation_BindsAndReports()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        CopyFixture("rigid-scene.gltf", _modelRoot);
        CopyFixture("rigid-scene.bin", _modelRoot);
        CopyFixture("rigid-scene.gltf", _animationRoot);
        CopyFixture("rigid-scene.bin", _animationRoot);

        var output = NewOutputDir();
        var request = Request(
            output,
            "rigid-scene.gltf",
            "rigid-scene.gltf#animation=0",
            new ExternalModelSourceRequest(
                _animationRoot,
                "rigid-scene.gltf",
                ExternalAssetInspection.GltfFormatId));

        var result = Render(request);
        Assert.Equal(0, result.ExitCode);

        using var document = ExternalModelReportJson.Parse(File.ReadAllText(Path.Combine(output, "report.json")));
        var pairing = document.RootElement.GetProperty("animationPairing");
        Assert.Equal("SeparateDocument", pairing.GetProperty("document").GetString());
        Assert.Equal("rigid-scene.gltf#animation=0", pairing.GetProperty("key").GetString());
        Assert.True(document.RootElement.GetProperty("eligibility").GetProperty("canRender").GetBoolean());
    }

    [Fact]
    public void ReportOnly_CompletesWithoutPng_AcrossRequestPaths()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        CopyFixture("static-triangle.glb", _modelRoot);
        CopyFixture("animated-skin.glb", _modelRoot);

        var staticOutput = NewOutputDir();
        var staticOnly = Render(Request(staticOutput, "static-triangle.glb", image: null));
        Assert.Equal(0, staticOnly.ExitCode);
        Assert.Empty(Directory.GetFiles(staticOutput, "*.png"));

        var animatedOutput = NewOutputDir();
        var animatedOnly = Render(Request(
            animatedOutput,
            "animated-skin.glb",
            "animated-skin.glb#animation=0",
            image: null));
        Assert.Equal(0, animatedOnly.ExitCode);
        Assert.Empty(Directory.GetFiles(animatedOutput, "*.png"));

        using var document = ExternalModelReportJson.Parse(
            File.ReadAllText(Path.Combine(animatedOutput, "report.json")));
        var metadata = document.RootElement.GetProperty("renderMetadata");
        Assert.False(metadata.TryGetProperty("pngContentHash", out _));
        Assert.Equal("animated-skin.glb#animation=0", metadata.GetProperty("animationKey").GetString());
    }
}
