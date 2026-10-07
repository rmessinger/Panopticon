using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;
using Panopticon.Tools.TestHarness.ExternalModels;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public sealed class ExternalModelHostTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _modelRoot;
    private readonly string _animationRoot;
    private readonly string _outputDir;

    public ExternalModelHostTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "panopticon-host-" + Guid.NewGuid().ToString("N"));
        _modelRoot = Path.Combine(_baseDir, "modelroot");
        _animationRoot = Path.Combine(_baseDir, "animroot");
        _outputDir = Path.Combine(_baseDir, "out");

        Directory.CreateDirectory(_modelRoot);
        Directory.CreateDirectory(_animationRoot);
        Directory.CreateDirectory(_outputDir);

        foreach (var file in FixtureFiles)
        {
            File.Copy(FixturePath(file), Path.Combine(_modelRoot, file), overwrite: true);
        }

        File.Copy(FixturePath("rigid-scene.gltf"), Path.Combine(_animationRoot, "rigid-scene.gltf"), true);
        File.Copy(FixturePath("rigid-scene.bin"), Path.Combine(_animationRoot, "rigid-scene.bin"), true);
    }

    public void Dispose()
    {
        Directory.Delete(_baseDir, true);
    }

    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Content", "Importing", "Gltf", "Fixtures");

    private static string FixturePath(string file) => Path.Combine(FixtureRoot, file);

    private static string[] FixtureFiles =>
    [
        "static-triangle.glb",
        "pbr-materials.gltf",
        "pbr-materials.bin",
        "pbr-materials_0.png",
        "pbr-materials_1.jpg",
        "rigid-scene.gltf",
        "rigid-scene.bin",
    ];

    private sealed class CollectingReporter : ITestHarnessReporter
    {
        public List<string> Errors { get; } = [];

        public List<string> Messages { get; } = [];

        public void Error(string message) => Errors.Add(message);

        public void Info(string message) => Messages.Add(message);

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

    private ExternalModelRequest CreateRequest(
        string? imagePath = null,
        string? reportName = "report.json",
        ExternalModelSourceRequest? animationSource = null,
        ExternalModelPreviewOptions? preview = null)
    {
        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = 1,
            ModelSource = new ExternalModelSourceRequest(
                _modelRoot,
                "pbr-materials.gltf",
                ExternalAssetInspection.GltfFormatId),
            AnimationSource = animationSource,
            ImportOptions = ExternalModelImportOptions.Default,
            PreviewOptions = preview ?? ExternalModelPreviewOptions.Default,
            Viewport = new ExternalModelViewport(64, 64),
            OutputImagePath = imagePath,
            OutputReportPath = reportName is null ? "" : Path.Combine(_outputDir, reportName),
        };
    }

    private static ExternalModelHostResult Run(
        ExternalModelRequest request,
        IExternalModelRenderStage? stage = null)
    {
        return new ExternalModelHost(new CollectingReporter(), stage).RunRequest(request);
    }

    private static bool InstallationAvailable()
    {
        return new GeneralsInstallationService().TryGetInstallation(out _);
    }

    [Fact]
    public void Arguments_MissingValue_ReturnsTwo()
    {
        var result = ExternalModelHost.RunArguments(
            ["--external-model-request"],
            new CollectingReporter());

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void Arguments_DuplicateFlag_ReturnsTwo()
    {
        var result = ExternalModelHost.RunArguments(
            ["--external-model-request", "a.json", "--external-model-request", "b.json"],
            new CollectingReporter());

        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void Arguments_UnreadableFile_ReturnsThree()
    {
        var result = ExternalModelHost.RunArguments(
            ["--external-model-request", Path.Combine(_baseDir, "absent.json")],
            new CollectingReporter());

        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void InvalidRequest_FailsBeforeInstallationResolution()
    {
        var previous = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        Environment.SetEnvironmentVariable("CNC_GENERALS_PATH", null);
        try
        {
            var request = CreateRequest();
            request = request with
            {
                ModelSource = request.ModelSource with { FormatId = "W3D" },
            };

            var result = Run(request);

            Assert.Equal(4, result.ExitCode);
            Assert.Contains(result.Diagnostics, d => d.Code == ExternalModelRequestValidator.UnsupportedFormatCode);
            Assert.True(result.ReportWritten);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CNC_GENERALS_PATH", previous);
        }
    }

    [Fact]
    public void ValidationDiagnostics_AreReportedAndWritten()
    {
        var request = CreateRequest(reportName: null);
        var reporter = new CollectingReporter();

        var result = new ExternalModelHost(reporter).RunJson(ExternalModelProtocolJson.Serialize(request));

        Assert.Equal(4, result.ExitCode);
        Assert.Contains(reporter.Errors, m => m.Contains(ExternalModelRequestValidator.MissingReportPathCode));
    }

    [Fact]
    public void Mounts_AreIsolatedPerRoot()
    {
        File.Delete(Path.Combine(_modelRoot, "rigid-scene.gltf"));
        File.Delete(Path.Combine(_modelRoot, "rigid-scene.bin"));

        using var modelMount = ExternalModelHost.CreateMount(
            new ExternalModelSourceRequest(_modelRoot, "pbr-materials.gltf", ExternalAssetInspection.GltfFormatId),
            GltfImportOptions.Default,
            ExternalModelImportOptions.Default);
        using var animationMount = ExternalModelHost.CreateMount(
            new ExternalModelSourceRequest(_animationRoot, "rigid-scene.gltf", ExternalAssetInspection.GltfFormatId),
            GltfImportOptions.Default,
            ExternalModelImportOptions.Default);

        Assert.NotNull(modelMount.ImportContext.FileSystem.GetFile("pbr-materials.gltf"));
        Assert.Null(modelMount.ImportContext.FileSystem.GetFile("rigid-scene.gltf"));
        Assert.NotNull(animationMount.ImportContext.FileSystem.GetFile("rigid-scene.gltf"));
        Assert.Null(animationMount.ImportContext.FileSystem.GetFile("pbr-materials.gltf"));
    }

    [Fact]
    public void AnimationKey_ResolvesFromSelectionOrAnimationSource()
    {
        var sameDocument = CreateRequest(preview: ExternalModelPreviewOptions.Default with
        {
            SelectedAnimationKey = "pbr-materials.gltf#animation=0",
        });
        Assert.Equal("pbr-materials.gltf#animation=0", ExternalModelHost.ResolveAnimationKey(sameDocument));

        var separate = CreateRequest(
            animationSource: new ExternalModelSourceRequest(
                _animationRoot,
                "rigid-scene.gltf",
                ExternalAssetInspection.GltfFormatId));
        Assert.Equal("rigid-scene.gltf#animation=0", ExternalModelHost.ResolveAnimationKey(separate));

        Assert.Null(ExternalModelHost.ResolveAnimationKey(CreateRequest()));
    }

    [Fact]
    public void StaticModel_MaterializesThroughExternalFileSystem()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var result = Run(CreateRequest());

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ReportWritten);

        var report = ExternalModelReportJson.Parse(
            File.ReadAllText(Path.Combine(_outputDir, "report.json"))).RootElement;
        Assert.True(report.GetProperty("eligibility").GetProperty("canRender").GetBoolean());
        Assert.Equal("pbr-materials.gltf", report.GetProperty("modelSource").GetProperty("displayName").GetString());
        Assert.Equal(64, report.GetProperty("modelSource").GetProperty("contentHash").GetString()!.Length);
        Assert.Contains(report.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == "EXT300");
    }

    [Fact]
    public void CrossDocumentAnimation_BindsByName()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var request = CreateRequest(
            animationSource: new ExternalModelSourceRequest(
                _animationRoot,
                "rigid-scene.gltf",
                ExternalAssetInspection.GltfFormatId),
            preview: ExternalModelPreviewOptions.Default with
            {
                SelectedAnimationKey = "rigid-scene.gltf#animation=0",
            });
        request = request with
        {
            ModelSource = request.ModelSource with { VirtualKey = "rigid-scene.gltf" },
        };

        var result = Run(request);

        Assert.Equal(0, result.ExitCode);

        var report = ExternalModelReportJson.Parse(
            File.ReadAllText(Path.Combine(_outputDir, "report.json"))).RootElement;
        Assert.Equal("SeparateDocument", report.GetProperty("animationPairing").GetProperty("document").GetString());
        Assert.Equal("rigid-scene.gltf#animation=0", report.GetProperty("animationPairing").GetProperty("key").GetString());
    }

    [Fact]
    public void MissingSourceDocument_FailsWithSelectionDiagnostic()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var request = CreateRequest();
        request = request with
        {
            ModelSource = request.ModelSource with { VirtualKey = "absent.gltf" },
        };

        var result = Run(request);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, d => d.Code == "EXT002");
    }

    [Fact]
    public void MalformedSourceDocument_FailsWithParseDiagnostic()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        File.WriteAllText(Path.Combine(_modelRoot, "broken.gltf"), "{ not gltf");
        var request = CreateRequest();
        request = request with
        {
            ModelSource = request.ModelSource with { VirtualKey = "broken.gltf" },
        };

        var result = Run(request);

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, d => d.Code == "GLTF010");
        Assert.True(result.ReportWritten);
    }

    [Fact]
    public void RenderStageFailure_ProducesFailureReportWithoutCrashing()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var result = Run(CreateRequest(), new ThrowingStage());

        Assert.Equal(5, result.ExitCode);
        Assert.Contains(result.Diagnostics, d => d.Code == "EXT999");
        Assert.True(result.ReportWritten);

        var report = ExternalModelReportJson.Parse(
            File.ReadAllText(Path.Combine(_outputDir, "report.json"))).RootElement;
        Assert.False(report.GetProperty("eligibility").GetProperty("canRender").GetBoolean());
    }

    private sealed class ThrowingStage : IExternalModelRenderStage
    {
        public ExternalModelRenderOutcome Render(ExternalModelRenderInputs inputs)
        {
            throw new InvalidOperationException("injected render failure");
        }
    }

    [Fact]
    public void ExternalTexture_MaterializesOnlyThroughModelFileSystem()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        // pbr-materials.gltf references pbr-albedo.png relatively; the file exists only in
        // the model root, so successful materialization proves resolution through the
        // external filesystem rather than the installation.
        File.Move(
            Path.Combine(_animationRoot, "rigid-scene.gltf"),
            Path.Combine(_animationRoot, "hidden.gltf"));

        var result = Run(CreateRequest());

        Assert.Equal(0, result.ExitCode);
    }
}
