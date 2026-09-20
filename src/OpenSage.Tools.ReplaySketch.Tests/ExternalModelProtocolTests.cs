#nullable enable

using System;
using System.IO;
using System.Linq;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class ExternalModelProtocolTests : IDisposable
{
    private readonly string _root;
    private readonly string _animRoot;
    private readonly string _outputDir;

    public ExternalModelProtocolTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "opensage-proto-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(baseDir, "modelroot");
        _animRoot = Path.Combine(baseDir, "animroot");
        _outputDir = Path.Combine(baseDir, "out");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_animRoot);
        Directory.CreateDirectory(_outputDir);

        File.WriteAllText(Path.Combine(_root, "tank.gltf"), "{}");
        File.WriteAllText(Path.Combine(_animRoot, "walk.gltf"), "{}");
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_root)!, true);
    }

    private ExternalModelRequest CreateRequest(
        string? imagePath = null,
        string? reportPath = null,
        ExternalModelSourceRequest? animationSource = null,
        ExternalModelPreviewOptions? preview = null,
        ExternalModelViewport? viewport = null)
    {
        return new ExternalModelRequest
        {
            SchemaVersion = ExternalModelRequest.CurrentSchemaVersion,
            Generation = 7,
            ModelSource = new ExternalModelSourceRequest(_root, "tank.gltf", ExternalAssetInspection.GltfFormatId),
            AnimationSource = animationSource,
            ImportOptions = new ExternalModelImportOptions(2f, null, null),
            PreviewOptions = preview ?? ExternalModelPreviewOptions.Default,
            Viewport = viewport ?? new ExternalModelViewport(128, 64),
            OutputImagePath = imagePath,
            OutputReportPath = reportPath ?? Path.Combine(_outputDir, "report.json"),
        };
    }

    [Fact]
    public void Serialization_RoundTripsEveryDocumentedOption()
    {
        var request = CreateRequest(
            imagePath: Path.Combine(_outputDir, "preview.png"),
            animationSource: new ExternalModelSourceRequest(_animRoot, "walk.gltf", ExternalAssetInspection.GltfFormatId),
            preview: ExternalModelPreviewOptions.Default with
            {
                ModelYaw = 0.5f,
                ModelPitch = 0.25f,
                ModelRoll = -0.1f,
                ModelTranslation = new System.Numerics.Vector3(1, 2, 3),
                CameraOrbit = 1f,
                CameraElevation = 0.5f,
                CameraDistance = 25f,
                CameraFieldOfView = 1f,
                Background = ExternalPreviewBackground.Dark,
                Lighting = ExternalPreviewLighting.Studio,
                SelectedAnimationKey = "walk.gltf#animation=0",
                AnimationSampleTime = 1.5f,
                MorphWeights = new System.Collections.Generic.Dictionary<string, float> { ["smile"] = 0.5f },
            });

        var json = ExternalModelProtocolJson.Serialize(request);
        var roundTripped = ExternalModelProtocolJson.Deserialize(json);

        Assert.Equal(request.Generation, roundTripped.Generation);
        Assert.Equal(_root, roundTripped.ModelSource.AbsoluteRoot);
        Assert.Equal("tank.gltf", roundTripped.ModelSource.VirtualKey);
        Assert.Equal(request.ModelSource.FormatId, roundTripped.ModelSource.FormatId);
        Assert.Equal(_animRoot, roundTripped.AnimationSource!.AbsoluteRoot);
        Assert.Equal(2f, roundTripped.ImportOptions.UnitsPerMeter);
        Assert.Equal(0.5f, roundTripped.PreviewOptions.ModelYaw);
        Assert.Equal(new System.Numerics.Vector3(1, 2, 3), roundTripped.PreviewOptions.ModelTranslation);
        Assert.Equal(25f, roundTripped.PreviewOptions.CameraDistance);
        Assert.Equal(ExternalPreviewBackground.Dark, roundTripped.PreviewOptions.Background);
        Assert.Equal(ExternalPreviewLighting.Studio, roundTripped.PreviewOptions.Lighting);
        Assert.Equal("walk.gltf#animation=0", roundTripped.PreviewOptions.SelectedAnimationKey);
        Assert.Equal(1.5f, roundTripped.PreviewOptions.AnimationSampleTime);
        Assert.Equal(0.5f, roundTripped.PreviewOptions.MorphWeights["smile"]);
        Assert.Equal(128, roundTripped.Viewport.Width);
        Assert.Equal(64, roundTripped.Viewport.Height);
        Assert.Equal(request.OutputImagePath, roundTripped.OutputImagePath);
        Assert.Equal(request.OutputReportPath, roundTripped.OutputReportPath);
    }

    [Fact]
    public void Serialization_IsDeterministic()
    {
        var request = CreateRequest(imagePath: Path.Combine(_outputDir, "preview.png"));

        Assert.Equal(
            ExternalModelProtocolJson.Serialize(request),
            ExternalModelProtocolJson.Serialize(request));
    }

    [Fact]
    public void Deserialize_UnknownSchemaVersion_Throws()
    {
        var json = ExternalModelProtocolJson.Serialize(CreateRequest())
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");

        var exception = Assert.Throws<InvalidDataException>(
            () => ExternalModelProtocolJson.Deserialize(json));
        Assert.Contains("99", exception.Message);
    }

    [Fact]
    public void Validate_ValidRequest_HasNoDiagnostics()
    {
        var request = CreateRequest(imagePath: Path.Combine(_outputDir, "preview.png"));

        Assert.Empty(ExternalModelRequestValidator.Validate(request));
    }

    [Fact]
    public void Validate_SeparateAnimationRoot_IsCarriedDistinctly()
    {
        var request = CreateRequest(
            animationSource: new ExternalModelSourceRequest(_animRoot, "walk.gltf", ExternalAssetInspection.GltfFormatId));

        Assert.Empty(ExternalModelRequestValidator.Validate(request));
        Assert.NotEqual(request.ModelSource.AbsoluteRoot, request.AnimationSource!.AbsoluteRoot);
    }

    [Fact]
    public void Validate_ReportOnly_OmitsImageOnly()
    {
        var request = CreateRequest(imagePath: null);

        Assert.Empty(ExternalModelRequestValidator.Validate(request));
        Assert.Null(request.OutputImagePath);
        Assert.False(string.IsNullOrEmpty(request.OutputReportPath));
    }

    [Fact]
    public void Validate_MissingReportPath_Fails()
    {
        var request = CreateRequest(reportPath: "");

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ExternalModelRequestValidator.MissingReportPathCode, diagnostic.Code);
    }

    [Fact]
    public void Validate_UnsupportedFormat_FailsWithoutOpeningSources()
    {
        var request = CreateRequest();
        request = request with
        {
            ModelSource = request.ModelSource with { FormatId = "W3D" },
        };

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ExternalModelRequestValidator.UnsupportedFormatCode, diagnostic.Code);
    }

    [Theory]
    [InlineData("/tank.gltf")]
    [InlineData("sub\\tank.gltf")]
    [InlineData("sub/../tank.gltf")]
    [InlineData("sub//tank.gltf")]
    [InlineData("")]
    public void Validate_NonNormalizedVirtualKeys_Fail(string virtualKey)
    {
        var request = CreateRequest();
        request = request with
        {
            ModelSource = request.ModelSource with { VirtualKey = virtualKey },
        };

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d => d.Code == ExternalModelRequestValidator.InvalidVirtualKeyCode);
    }

    [Fact]
    public void Validate_MissingSourceRoot_Fails()
    {
        var request = CreateRequest();
        request = request with
        {
            ModelSource = request.ModelSource with { AbsoluteRoot = Path.Combine(_root, "absent") },
        };

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d => d.Code == ExternalModelRequestValidator.MissingSourceRootCode);
    }

    [Fact]
    public void Validate_NonPositiveViewport_Fails()
    {
        var request = CreateRequest(viewport: new ExternalModelViewport(0, 64));

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d => d.Code == ExternalModelRequestValidator.InvalidViewportCode);
    }

    [Fact]
    public void Validate_InvalidPreviewOptions_Fail()
    {
        var request = CreateRequest(preview: ExternalModelPreviewOptions.Default with
        {
            CameraDistance = -1f,
        });

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d => d.Code == ExternalModelRequestValidator.InvalidPreviewOptionsCode);
    }

    [Fact]
    public void Validate_RelativeOutputPath_Fails()
    {
        var request = CreateRequest(reportPath: "relative/report.json");

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("absolute"));
    }

    [Fact]
    public void Validate_DirectoryOutputTarget_Fails()
    {
        var request = CreateRequest(reportPath: _outputDir);

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("directory"));
    }

    [Fact]
    public void Validate_MissingOutputParent_Fails()
    {
        var request = CreateRequest(reportPath: Path.Combine(_outputDir, "absent", "report.json"));

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("parent"));
    }

    [Fact]
    public void Validate_DuplicateImageAndReportPaths_Fail()
    {
        var path = Path.Combine(_outputDir, "same.json");
        var request = CreateRequest(imagePath: path, reportPath: path);

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("distinct"));
    }

    [Fact]
    public void Validate_OutputsInDifferentDirectories_Fail()
    {
        var otherDir = Path.Combine(Path.GetDirectoryName(_outputDir)!, "out2");
        Directory.CreateDirectory(otherDir);

        var request = CreateRequest(
            imagePath: Path.Combine(_outputDir, "preview.png"),
            reportPath: Path.Combine(otherDir, "report.json"));

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("parent directory"));
    }

    [Fact]
    public void Validate_OutputOverwritingSourceFile_Fails()
    {
        var request = CreateRequest(reportPath: Path.Combine(_root, "tank.gltf"));

        var diagnostics = ExternalModelRequestValidator.Validate(request);

        Assert.Contains(diagnostics, d =>
            d.Code == ExternalModelRequestValidator.InvalidOutputCode &&
            d.Message.Contains("source"));
    }
}
