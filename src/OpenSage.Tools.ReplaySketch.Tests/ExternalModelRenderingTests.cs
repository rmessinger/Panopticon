#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using OpenSage.Tools.TestHarness;
using OpenSage.Tools.TestHarness.ExternalModels;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class ExternalModelRenderingTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _modelRoot;
    private readonly string _outputA;
    private readonly string _outputB;

    public ExternalModelRenderingTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "opensage-render-" + Guid.NewGuid().ToString("N"));
        _modelRoot = Path.Combine(_baseDir, "modelroot");
        _outputA = Path.Combine(_baseDir, "outA");
        _outputB = Path.Combine(_baseDir, "outB");

        Directory.CreateDirectory(_modelRoot);
        Directory.CreateDirectory(_outputA);
        Directory.CreateDirectory(_outputB);

        foreach (var file in new[]
                 {
                     "rigid-scene.gltf",
                     "rigid-scene.bin",
                     "pbr-materials.gltf",
                     "pbr-materials.bin",
                     "pbr-materials_0.png",
                     "pbr-materials_1.jpg",
                 })
        {
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Content", "Importing", "Gltf", "Fixtures", file),
                Path.Combine(_modelRoot, file),
                overwrite: true);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_baseDir, true);
    }

    private sealed class CollectingReporter : ITestHarnessReporter
    {
        public void Error(string message)
        {
        }

        public void Info(string message)
        {
        }

        public void ActionDispatched(OpenSage.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(OpenSage.Tools.ReplaySketch.Model.ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(OpenSage.Logic.OutcomeStatus status, OpenSage.Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }

    private ExternalModelRequest CreateRequest(
        string outputDir,
        string? virtualKey = "pbr-materials.gltf",
        string? image = "preview.png",
        ExternalModelPreviewOptions? preview = null)
    {
        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = 1,
            ModelSource = new ExternalModelSourceRequest(
                _modelRoot,
                virtualKey,
                ExternalAssetInspection.GltfFormatId),
            ImportOptions = ExternalModelImportOptions.Default,
            PreviewOptions = preview ?? ExternalModelPreviewOptions.Default,
            Viewport = new ExternalModelViewport(96, 96),
            OutputImagePath = image is null ? null : Path.Combine(outputDir, image),
            OutputReportPath = Path.Combine(outputDir, "report.json"),
        };
    }

    private static ExternalModelHostResult Render(ExternalModelRequest request)
    {
        return new ExternalModelHost(new CollectingReporter(), new ExternalModelSceneRenderer())
            .RunRequest(request);
    }

    private static bool InstallationAvailable()
    {
        return new GeneralsInstallationService().TryGetInstallation(out _);
    }

    [Fact]
    public void WorldMatrix_CombinesYawPitchRollTranslationInFixedOrder()
    {
        var preview = ExternalModelPreviewOptions.Default with
        {
            ModelYaw = 0.1f,
            ModelPitch = 0.2f,
            ModelRoll = 0.3f,
            ModelTranslation = new Vector3(1, 2, 3),
        };

        var expected = Matrix4x4.CreateRotationZ(0.1f) *
            Matrix4x4.CreateRotationX(0.2f) *
            Matrix4x4.CreateRotationY(0.3f) *
            Matrix4x4.CreateTranslation(1, 2, 3);

        Assert.Equal(expected, ExternalModelSceneRenderer.ComputeWorldMatrix(preview));
        Assert.Equal(Matrix4x4.Identity, ExternalModelSceneRenderer.ComputeWorldMatrix(ExternalModelPreviewOptions.Default));
    }

    [Fact]
    public void ArtifactWriter_PublishesThroughStagingAtomically()
    {
        var path = Path.Combine(_outputA, "blob.bin");
        var observedStagingPath = "";

        ExternalModelArtifactWriter.Publish(path, stagingPath =>
        {
            observedStagingPath = stagingPath;
            File.WriteAllText(stagingPath, "payload");
        });

        Assert.Equal(path + ExternalModelArtifactWriter.StagingSuffix, observedStagingPath);
        Assert.Equal("payload", File.ReadAllText(path));
        Assert.False(File.Exists(observedStagingPath));
    }

    [Fact]
    public void ArtifactWriter_FailedPublish_LeavesFinalUntouchedAndNoStaging()
    {
        var path = Path.Combine(_outputA, "report.json");
        File.WriteAllText(path, "original");
        var stagingPath = path + ExternalModelArtifactWriter.StagingSuffix;
        Directory.CreateDirectory(stagingPath);

        Assert.Throws<UnauthorizedAccessException>(
            () => ExternalModelArtifactWriter.PublishText(path, "new"));

        Assert.Equal("original", File.ReadAllText(path));
        Assert.True(Directory.Exists(stagingPath));
        Directory.Delete(stagingPath);
    }

    [Fact]
    public void ArtifactWriter_RemoveImage_ToleratesMissingFile()
    {
        ExternalModelArtifactWriter.RemoveImage(Path.Combine(_outputA, "absent.png"));
    }

    [Fact]
    public void ReportOnly_CompletesWithoutImage()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var result = Render(CreateRequest(_outputA, image: null));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Directory.GetFiles(_outputA, "*.png"));
        Assert.True(File.Exists(Path.Combine(_outputA, "report.json")));
    }

    [Fact]
    public void SuccessfulRender_PublishesBothArtifactsAtomically()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var request = CreateRequest(_outputA, preview: ExternalModelPreviewOptions.Default with
        {
            CameraOrbit = 0.6f,
            CameraElevation = 0.3f,
            CameraDistance = 12f,
            Background = ExternalPreviewBackground.Dark,
            Lighting = ExternalPreviewLighting.Studio,
        });
        var result = Render(request);

        Assert.Equal(0, result.ExitCode);

        var imagePath = Path.Combine(_outputA, "preview.png");
        var reportPath = Path.Combine(_outputA, "report.json");
        Assert.True(File.Exists(imagePath));
        Assert.True(File.Exists(reportPath));
        Assert.Empty(Directory.GetFiles(_outputA, "*" + ExternalModelArtifactWriter.StagingSuffix));

        var report = ExternalModelReportJson.Parse(File.ReadAllText(reportPath)).RootElement;
        var metadata = report.GetProperty("renderMetadata");
        Assert.Equal(96, metadata.GetProperty("viewportWidth").GetInt32());
        Assert.Equal(96, metadata.GetProperty("viewportHeight").GetInt32());

        var fileHash = ExternalModelArtifactWriter.ComputeFileHash(imagePath);
        Assert.Equal(fileHash, metadata.GetProperty("pngContentHash").GetString());
        Assert.Equal(new FileInfo(imagePath).Length, metadata.GetProperty("pngByteLength").GetInt64());

        Assert.Equal(0.6f, report.GetProperty("previewOptions").GetProperty("cameraOrbit").GetSingle(), 5);
        Assert.Equal("Dark", report.GetProperty("previewOptions").GetProperty("background").GetString());
        Assert.Equal("Studio", report.GetProperty("previewOptions").GetProperty("lighting").GetString());
    }

    [Fact]
    public void RepeatedIdenticalRequests_ProduceIdenticalPngHashes()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var preview = ExternalModelPreviewOptions.Default with
        {
            CameraOrbit = 0.9f,
            CameraElevation = 0.2f,
            CameraDistance = 14f,
        };

        Assert.Equal(0, Render(CreateRequest(_outputA, preview: preview)).ExitCode);
        Assert.Equal(0, Render(CreateRequest(_outputB, preview: preview)).ExitCode);

        var hashA = ExternalModelArtifactWriter.ComputeFileHash(Path.Combine(_outputA, "preview.png"));
        var hashB = ExternalModelArtifactWriter.ComputeFileHash(Path.Combine(_outputB, "preview.png"));
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void AnimatedRequest_RecordsFixedSampleTime()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var result = Render(CreateRequest(
            _outputA,
            virtualKey: "rigid-scene.gltf",
            preview: ExternalModelPreviewOptions.Default with
            {
                SelectedAnimationKey = "rigid-scene.gltf#animation=0",
                AnimationSampleTime = 1f,
                CameraOrbit = 0.6f,
                CameraElevation = 0.3f,
            }));

        Assert.Equal(0, result.ExitCode);

        var report = ExternalModelReportJson.Parse(
            File.ReadAllText(Path.Combine(_outputA, "report.json"))).RootElement;
        Assert.Equal(1f, report.GetProperty("renderMetadata").GetProperty("animationSampleTimeSeconds").GetSingle(), 5);
        Assert.Equal("rigid-scene.gltf#animation=0",
            report.GetProperty("renderMetadata").GetProperty("animationKey").GetString());
    }

    [Fact]
    public void RenderStageThrow_ProducesFailureReportAndNoImage()
    {
        if (!InstallationAvailable())
        {
            return;
        }

        var result = new ExternalModelHost(
            new CollectingReporter(),
            new ThrowingStage()).RunRequest(CreateRequest(_outputA));

        Assert.Equal(5, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_outputA, "preview.png")));
        Assert.True(File.Exists(Path.Combine(_outputA, "report.json")));

        var reportJson = File.ReadAllText(Path.Combine(_outputA, "report.json"));
        Assert.DoesNotContain(ExternalModelArtifactWriter.StagingSuffix, reportJson, StringComparison.Ordinal);
        Assert.Contains("EXT999", reportJson);
    }

    private sealed class ThrowingStage : IExternalModelRenderStage
    {
        public ExternalModelRenderOutcome Render(ExternalModelRenderInputs inputs)
        {
            throw new InvalidOperationException("injected render failure");
        }
    }
}
