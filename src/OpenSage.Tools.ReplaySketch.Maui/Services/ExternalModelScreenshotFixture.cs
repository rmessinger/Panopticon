#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.IO;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using OpenSage.Tools.ReplaySketch.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui.Services;

/// <summary>
/// Deterministic Model-tab state for screenshot mode: an <see cref="ExternalModelViewModel"/>
/// over fake mounts and a fake preview client, containing a catalog, two clips, one warning,
/// one error detail, non-default options, eligibility, and a stable preview image. No
/// TestHarness process is launched.
/// </summary>
public static class ExternalModelScreenshotFixture
{
    /// <summary>
    /// 4x4 grayscale PNG; stable bytes so screenshots are reproducible.
    /// </summary>
    private static readonly byte[] PreviewPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x04, 0x08, 0x02, 0x00, 0x00, 0x00, 0x26, 0x92, 0x0E,
        0x7C, 0x00, 0x00, 0x00, 0x18, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x1F, 0x00, 0x05, 0x0A, 0x02, 0x2D, 0x3B, 0x4F, 0x58, 0x9E, 0x04, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public static ExternalModelViewModel Create()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "replaysketch-model-fixture");
        Directory.CreateDirectory(fixtureRoot);
        var modelPath = Path.Combine(fixtureRoot, "tank.gltf");
        File.WriteAllText(modelPath, "{}");

        var viewModel = new ExternalModelViewModel(
            new ExternalModelSession(new FixtureMountFactory(), new ExternalModelArtifactStore()),
            new ExternalModelArtifactStore(),
            new FixtureSelector(modelPath),
            new SynchronousMainThreadDispatcher(),
            new FixturePreviewClient());

        var command = (System.Windows.Input.ICommand)viewModel.ChooseModelCommand;
        command.Execute(null);

        // Successful render publishes the stable preview image; a second, failing render
        // leaves one error diagnostic visible with the artifact retained.
        ((System.Windows.Input.ICommand)viewModel.RenderCommand).Execute(null);
        ((System.Windows.Input.ICommand)viewModel.RenderCommand).Execute(null);

        viewModel.NextPreviewOptions = ExternalModelPreviewOptions.Default with
        {
            ModelYaw = 0.8f,
            CameraOrbit = 0.5f,
            CameraElevation = 0.3f,
            CameraDistance = 14f,
        };
        ((System.Windows.Input.ICommand)viewModel.ApplyPreviewOptionsCommand).Execute(null);

        return viewModel;
    }

    private static ExternalAssetCatalog CreateCatalog()
    {
        return new ExternalAssetCatalog(
            "tank.gltf",
            ExternalAssetInspection.GltfFormatId,
            sceneCount: 1,
            nodeCount: 3,
            primitiveCount: 2,
            vertexCount: 128,
            indexCount: 240,
            materialCount: 2,
            imageCount: 2,
            skinCount: 1,
            jointCount: 4,
            bounds: new ExternalModelBounds(
                new Vector3(-1, -1, 0),
                new Vector3(1, 1, 2),
                new Vector3(0, 0, 1),
                1.7320508f),
            requiredFeatureIds: [],
            optionalFeatureIds: ["KHR_texture_transform"],
            animations:
            [
                new ExternalAnimationDescriptor("tank.gltf#animation=0", "idle", TimeSpan.FromSeconds(2), 3),
                new ExternalAnimationDescriptor("tank.gltf#animation=1", "drive", TimeSpan.FromSeconds(4.5), 5),
            ],
            diagnostics:
            [
                new ExternalAssetInspectionDiagnostic(
                    "GLTF0001",
                    ExternalInspectionDiagnosticSeverity.Warning,
                    "Optional glTF extension 'KHR_texture_transform' is not interpreted."),
            ]);
    }

    private sealed class FixtureSelector(string modelPath) : IExternalModelFileSelector
    {
        public Task<string?> SelectModelFileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(modelPath);
        }

        public Task<string?> SelectAnimationFileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FixtureMountFactory : IExternalAssetSourceMountFactory
    {
        public IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options)
        {
            return new FixtureMount(Path.GetFileName(absoluteSelectedFile));
        }
    }

    private sealed class FixtureMount(string rootVirtualKey) : IExternalSourceMount
    {
        public string RootVirtualKey { get; } = rootVirtualKey;

        public ExternalFormatSelection FormatSelection { get; } = new(
            ExternalAssetInspection.GltfFormatId,
            GltfImportOptions.Default,
            new FixtureInspector());

        public AssetImportContext ImportContext { get; } = new(
            new DiskFileSystem(Path.GetTempPath()),
            null);

        public void Dispose()
        {
        }
    }

    private sealed class FixtureInspector : IExternalAssetInspector
    {
        public ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context)
        {
            return CreateCatalog();
        }
    }

    private sealed class FixturePreviewClient : IExternalModelPreviewClient
    {
        private int _calls;

        public Task<ExternalModelPreviewResult> ExecuteAsync(
            ExternalModelRequest request,
            IExternalModelArtifactLease lease,
            CancellationToken cancellationToken)
        {
            var call = ++_calls;
            if (call == 1 && request.OutputImagePath is { } imagePath)
            {
                File.WriteAllBytes(imagePath, PreviewPng);
                return Task.FromResult(new ExternalModelPreviewResult(true, []));
            }

            return Task.FromResult(new ExternalModelPreviewResult(
                false,
                [
                    new ExternalModelDiagnostic(
                        "EXT210",
                        ExternalDiagnosticSeverity.Error,
                        ExternalDiagnosticStage.Render,
                        ExternalDiagnosticSourceRole.Model,
                        null,
                        "The TestHarness process exited with code 5 (fixture failure detail)."),
                ]));
        }
    }
}
