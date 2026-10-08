using System.Collections.Generic;
using System.Linq;
using Panopticon.Benchmarks.Simulation.Experiments;
using Panopticon.Content;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A4-36 model-import seam registration: identities through the A4 registry, construction
/// delegated to the C1-owned factories, W3D as the reference default.
/// </summary>
public class ModelImportExperimentRegistryTests
{
    private sealed class StubPathResolver : IPathResolver
    {
        public IEnumerable<string> GetPaths(string name, string language)
        {
            yield return name;
        }
    }

    [Fact]
    public void CreateDefault_RegistersModelImportSeamWithW3dReference()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var descriptors = registry.Descriptors.Where(d => d.Seam == "model-import").OrderBy(d => d.Id).ToList();
        Assert.Equal(2, descriptors.Count);
        Assert.Equal("gltf", descriptors[0].Id);
        Assert.Equal("w3d", descriptors[1].Id);
    }

    [Fact]
    public void Resolve_W3dCarriesStableIdentity()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var descriptor = registry.Resolve("model-import", "w3d");

        Assert.Equal("model-import", descriptor.Seam);
        Assert.Equal("w3d", descriptor.Id);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Version));
    }

    [Fact]
    public void Settings_ConstructStrategiesThroughC1Factories()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();
        var resolver = new StubPathResolver();

        var w3dSelection = (AssetLoadStrategySelection)registry.Resolve("model-import", "w3d").CreateSettings();
        var gltfSelection = (AssetLoadStrategySelection)registry.Resolve("model-import", "gltf").CreateSettings();

        Assert.NotNull(w3dSelection.CreateStrategy(resolver, resolver));
        Assert.NotNull(gltfSelection.CreateStrategy(resolver, resolver));
    }

    [Fact]
    public void ValidateDefinition_AcceptsModelImportAxis()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();
        var definition = new ExperimentDefinition(
            "import-comparison",
            "b1.empty-host",
            1,
            1,
            10,
            [new ExperimentAxis("model-import", ["w3d", "gltf"])]);

        registry.ValidateDefinition(definition);
        Assert.Equal(2, definition.Expand().Count);
    }
}
