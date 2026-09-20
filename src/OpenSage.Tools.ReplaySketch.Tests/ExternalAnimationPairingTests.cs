#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using OpenSage.Content.Importing;
using OpenSage.Content.Importing.Gltf;
using OpenSage.IO;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class ExternalAnimationPairingTests
{
    private static ExternalAnimationDescriptor Descriptor(
        string key,
        string name,
        double durationSeconds,
        int channelCount = 1)
    {
        return new ExternalAnimationDescriptor(key, name, TimeSpan.FromSeconds(durationSeconds), channelCount);
    }

    private static ExternalAssetCatalog CreateCatalog(
        string sourceDisplayName,
        params ExternalAnimationDescriptor[] animations)
    {
        return new ExternalAssetCatalog(
            sourceDisplayName,
            ExternalAssetInspection.GltfFormatId,
            1, animations.Length, 1, 3, 3, 1, 0, 0, 0,
            new ExternalModelBounds(Vector3.Zero, Vector3.One, new Vector3(0.5f), 0.8660254f),
            [],
            [],
            animations,
            []);
    }

    private sealed class FakeInspector : IExternalAssetInspector
    {
        private readonly Func<string, ExternalAssetCatalog?> _inspect;

        public FakeInspector(Func<string, ExternalAssetCatalog?> inspect)
        {
            _inspect = inspect;
        }

        public ExternalAssetCatalog? InspectModel(string assetKey, AssetImportContext context)
        {
            return _inspect(assetKey);
        }
    }

    private sealed class FakeMount : IExternalSourceMount
    {
        public FakeMount(string rootVirtualKey, ExternalAssetCatalog catalog)
        {
            RootVirtualKey = rootVirtualKey;
            FormatSelection = new ExternalFormatSelection(
                ExternalAssetInspection.GltfFormatId,
                GltfImportOptions.Default,
                new FakeInspector(_ => catalog));
            ImportContext = new AssetImportContext(new DiskFileSystem(Path.GetTempPath()), null);
        }

        public string RootVirtualKey { get; }

        public ExternalFormatSelection FormatSelection { get; }

        public AssetImportContext ImportContext { get; }

        public void Dispose()
        {
        }
    }

    private static ExternalModelSession CreateSession(
        Func<string, ExternalAssetCatalog> modelCatalog,
        Func<string, ExternalAssetCatalog>? animationCatalog = null)
    {
        return new ExternalModelSession(
            new StubMountFactory((file, _) =>
                new FakeMount(
                    Path.GetFileName(file),
                    file.Contains("walks")
                        ? (animationCatalog ?? modelCatalog)(file)
                        : modelCatalog(file))),
            new StubArtifactStore());
    }

    private sealed class StubMountFactory(Func<string, ExternalModelImportOptions, IExternalSourceMount> create)
        : IExternalAssetSourceMountFactory
    {
        public IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options)
        {
            return create(absoluteSelectedFile, options);
        }
    }

    private sealed class StubArtifactStore : IExternalModelArtifactStore
    {
        public IExternalModelArtifactLease CreateLease()
        {
            return new StubLease();
        }

        public void Dispose()
        {
        }

        private sealed class StubLease : IExternalModelArtifactLease
        {
            public string ArtifactId { get; } = Guid.NewGuid().ToString("N");

            public void Dispose()
            {
            }
        }
    }

    [Fact]
    public async Task SelectAnimation_FromModelDocument_PairsSameDocument()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));

        var loaded = await session.LoadModelAsync("/models/tank.gltf");
        var selected = session.SelectAnimation(key);

        Assert.NotNull(selected.AnimationPairing);
        Assert.Equal(ExternalAnimationSourceDocument.SameDocument, selected.AnimationPairing!.Document);
        Assert.Equal("tank.gltf", selected.AnimationPairing.SourceDisplayName);
        Assert.Equal(key, selected.AnimationPairing.SelectedKey);
        Assert.Equal(key, selected.PreviewOptions.SelectedAnimationKey);
        Assert.Equal(0, selected.PreviewOptions.AnimationSampleTime);
    }

    [Fact]
    public async Task SelectAnimation_FromSeparateDocument_PairsSeparateDocument()
    {
        var modelKey = "models/tank.gltf#animation=0";
        var animKey = "/walks/walk.gltf#animation=1".Replace("\\", "/");
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(modelKey, "idle", 1)),
            _ => CreateCatalog("walk.gltf",
                Descriptor("walks/walk.gltf#animation=0", "left", 1),
                Descriptor("walks/walk.gltf#animation=1", "right", 1, channelCount: 3)));

        await session.LoadModelAsync("/models/tank.gltf");
        await session.LoadAnimationAsync("/walks/walk.gltf");

        var selected = session.SelectAnimation("walks/walk.gltf#animation=1");

        Assert.NotNull(selected.AnimationPairing);
        Assert.Equal(ExternalAnimationSourceDocument.SeparateDocument, selected.AnimationPairing!.Document);
        Assert.Equal("walk.gltf", selected.AnimationPairing.SourceDisplayName);
        Assert.Equal("walks/walk.gltf#animation=1", selected.AnimationPairing.SelectedKey);
        Assert.Equal(3, selected.AnimationPairing.Descriptor.ChannelCount);
    }

    [Fact]
    public async Task SelectAnimation_DuplicateNames_DistinguishedByStableKeys()
    {
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf",
                Descriptor("models/tank.gltf#animation=0", "drive", 1),
                Descriptor("models/tank.gltf#animation=1", "drive", 1, channelCount: 2)));

        await session.LoadModelAsync("/models/tank.gltf");

        var second = session.SelectAnimation("models/tank.gltf#animation=1");

        Assert.Equal("models/tank.gltf#animation=1", second.AnimationPairing!.SelectedKey);
        Assert.Equal(2, second.AnimationPairing.Descriptor.ChannelCount);
    }

    [Fact]
    public async Task SelectAnimation_UnknownKey_Throws()
    {
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor("models/tank.gltf#animation=0", "drive", 1)));

        await session.LoadModelAsync("/models/tank.gltf");

        Assert.Throws<InvalidDataException>(() => session.SelectAnimation("models/tank.gltf#animation=9"));
    }

    [Fact]
    public async Task SelectAnimation_Null_ClearsSelectionAndSampleTime()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));

        await session.LoadModelAsync("/models/tank.gltf");
        session.SelectAnimation(key);
        session.SetPreviewOptions(ExternalModelPreviewOptions.Default with
        {
            SelectedAnimationKey = key,
            AnimationSampleTime = 1,
        });

        var cleared = session.SelectAnimation(null);

        Assert.Null(cleared.AnimationPairing);
        Assert.Null(cleared.PreviewOptions.SelectedAnimationKey);
        Assert.Equal(0, cleared.PreviewOptions.AnimationSampleTime);
    }

    [Fact]
    public async Task LoadAnimation_Replacement_ClearsInvalidSeparateSelection()
    {
        var oldKey = "walks/old.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf"),
            file => Path.GetFileName(file) == "old.gltf"
                ? CreateCatalog("old.gltf", Descriptor(oldKey, "walk", 2))
                : CreateCatalog("new.gltf", Descriptor("walks/new.gltf#animation=0", "other", 1)));

        await session.LoadModelAsync("/models/tank.gltf");
        await session.LoadAnimationAsync("/walks/old.gltf");
        session.SelectAnimation(oldKey);

        var replaced = await session.LoadAnimationAsync("/walks/new.gltf");

        Assert.Null(replaced.AnimationPairing);
        Assert.Equal("new.gltf", replaced.AnimationSourceDisplayName);
    }

    [Fact]
    public async Task LoadAnimation_Replacement_KeepsValidSameDocumentSelection()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)),
            _ => CreateCatalog("other.gltf"));

        await session.LoadModelAsync("/models/tank.gltf");
        session.SelectAnimation(key);

        var replaced = await session.LoadAnimationAsync("/walks/other.gltf");

        Assert.NotNull(replaced.AnimationPairing);
        Assert.Equal(key, replaced.AnimationPairing!.SelectedKey);
    }

    [Fact]
    public async Task SetImportOptions_RerunsInspection_AndRemountsAnimation()
    {
        var animationKey = "walks/walk.gltf#animation=0";
        var mountCreations = 0;
        var session = new ExternalModelSession(
            new CountingFactory((file, _) =>
            {
                mountCreations++;
                return new FakeMount(
                    Path.GetFileName(file),
                    file.Contains("anim") || file.Contains("walk")
                        ? CreateCatalog("walk.gltf", Descriptor(animationKey, "walk", 1))
                        : CreateCatalog("tank.gltf"));
            }),
            new StubArtifactStore());

        using (session)
        {
            await session.LoadModelAsync("/models/tank.gltf");
            await session.LoadAnimationAsync("/walks/walk.gltf");
            session.SelectAnimation(animationKey);

            var snapshot = await session.SetImportOptionsAsync(new ExternalModelImportOptions(
                unitsPerMeter: 2f,
                featureGates: null,
                morphWeightRanges: null));

            Assert.Equal(ExternalModelSessionState.Ready, snapshot.State);
            Assert.Equal(2f, snapshot.ImportOptions.UnitsPerMeter);
            Assert.Equal("tank.gltf", snapshot.SourceDisplayName);
            Assert.Equal("walk.gltf", snapshot.AnimationSourceDisplayName);
        }

        Assert.Equal(4, mountCreations);
    }

    private sealed class CountingFactory(Func<string, ExternalModelImportOptions, IExternalSourceMount> create)
        : IExternalAssetSourceMountFactory
    {
        public IExternalSourceMount Create(string absoluteSelectedFile, ExternalModelImportOptions options)
        {
            return create(absoluteSelectedFile, options);
        }
    }

    [Fact]
    public async Task SetPreviewOptions_InvalidValues_ThrowBeforeApplying()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));

        await session.LoadModelAsync("/models/tank.gltf");
        var afterSelect = session.SelectAnimation(key);

        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with { SelectedAnimationKey = key, AnimationSampleTime = 5 }));
        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with { CameraDistance = 0 }));
        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with { CameraFieldOfView = 4 }));
        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with { ModelYaw = float.NaN }));
        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with { AnimationSampleTime = 1 }));

        Assert.Same(afterSelect.PreviewOptions, session.CurrentSnapshot.PreviewOptions);
    }

    [Fact]
    public async Task SetPreviewOptions_DoesNotReopenSources()
    {
        var key = "models/tank.gltf#animation=0";
        var mountCreations = 0;
        var session = new ExternalModelSession(
            new CountingFactory((file, _) =>
            {
                mountCreations++;
                return new FakeMount("tank.gltf", CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));
            }),
            new StubArtifactStore());

        using (session)
        {
            await session.LoadModelAsync("/models/tank.gltf");

            var snapshot = session.SetPreviewOptions(ExternalModelPreviewOptions.Default with
            {
                ModelYaw = 1.5f,
                CameraOrbit = 0.5f,
                Background = ExternalPreviewBackground.Dark,
            });

            Assert.Equal(1.5f, snapshot.PreviewOptions.ModelYaw);
            Assert.Equal(ExternalPreviewBackground.Dark, snapshot.PreviewOptions.Background);
            Assert.NotNull(snapshot.Catalog);
        }

        Assert.Equal(1, mountCreations);
    }

    [Fact]
    public async Task MorphWeights_RequireEnabledGateAndDeclaredRange()
    {
        using var session = CreateSession(_ => CreateCatalog("tank.gltf"));

        await session.LoadModelAsync("/models/tank.gltf");

        var noGateOptions = new ExternalModelImportOptions(
            unitsPerMeter: 1,
            featureGates: null,
            morphWeightRanges: new Dictionary<string, ExternalMorphWeightRange>
            {
                ["smile"] = new(0f, 1f),
            });
        await session.SetImportOptionsAsync(noGateOptions);

        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with
            {
                MorphWeights = new Dictionary<string, float> { ["smile"] = 0.5f },
            }));

        var gatedOptions = noGateOptions.WithFeatureGate(ExternalModelImportOptions.MorphTargetsGate, true);
        await session.SetImportOptionsAsync(gatedOptions);

        session.SetPreviewOptions(ExternalModelPreviewOptions.Default with
        {
            MorphWeights = new Dictionary<string, float> { ["smile"] = 0.5f },
        });

        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with
            {
                MorphWeights = new Dictionary<string, float> { ["smile"] = 1.5f },
            }));
        Assert.Throws<InvalidDataException>(() => session.SetPreviewOptions(
            ExternalModelPreviewOptions.Default with
            {
                MorphWeights = new Dictionary<string, float> { ["unknown"] = 0.5f },
            }));
    }

    [Fact]
    public async Task ResetToDefaults_RestoresCanonicalValues()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));

        await session.LoadModelAsync("/models/tank.gltf");
        session.SelectAnimation(key);
        session.SetPreviewOptions(ExternalModelPreviewOptions.Default with { ModelYaw = 2f });
        await session.SetImportOptionsAsync(new ExternalModelImportOptions(5f, null, null));

        var reset = await session.ResetToDefaultsAsync();

        Assert.Equal(ExternalModelSessionState.Ready, reset.State);
        Assert.Equal(ExternalModelImportOptions.Default.UnitsPerMeter, reset.ImportOptions.UnitsPerMeter);
        Assert.Equal(ExternalModelPreviewOptions.Default, reset.PreviewOptions);
        Assert.Null(reset.AnimationPairing);
        Assert.NotNull(reset.Catalog);
    }

    [Fact]
    public async Task Report_CarriesPairingAndOptions()
    {
        var key = "models/tank.gltf#animation=0";
        using var session = CreateSession(
            _ => CreateCatalog("tank.gltf", Descriptor(key, "drive", 2)));

        await session.LoadModelAsync("/models/tank.gltf");
        session.SelectAnimation(key);
        session.SetPreviewOptions(ExternalModelPreviewOptions.Default with
        {
            SelectedAnimationKey = key,
            AnimationSampleTime = 1,
            ModelYaw = 0.75f,
        });

        var snapshot = session.CurrentSnapshot;
        var report = new ExternalModelReport(
            new ExternalSourceIdentity("tank.gltf", "models/tank.gltf", 102, "ABCD"),
            null,
            snapshot.Catalog,
            snapshot.Diagnostics,
            snapshot.Eligibility,
            snapshot.AnimationPairing,
            snapshot.ImportOptions,
            snapshot.PreviewOptions);

        var json = ExternalModelReportJson.Serialize(report);
        var root = ExternalModelReportJson.Parse(json).RootElement;

        Assert.Equal("SameDocument", root.GetProperty("animationPairing").GetProperty("document").GetString());
        Assert.Equal(key, root.GetProperty("animationPairing").GetProperty("key").GetString());
        Assert.Equal(2.0, root.GetProperty("animationPairing").GetProperty("durationSeconds").GetDouble());
        Assert.Equal(ExternalModelImportOptions.Default.UnitsPerMeter,
            root.GetProperty("importOptions").GetProperty("unitsPerMeter").GetSingle(), 5);
        Assert.Equal(0.75f, root.GetProperty("previewOptions").GetProperty("modelYaw").GetSingle(), 5);
        Assert.Equal(1f, root.GetProperty("previewOptions").GetProperty("animationSampleTime").GetSingle(), 5);
    }
}
