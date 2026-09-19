using OpenSage.Graphics.Shaders;
using OpenSage.Tests.Content.Importing;
using Xunit;

namespace OpenSage.Tests.Graphics.Shaders;

public sealed class PbrMetallicRoughnessShaderResourcesTests
{
    [GraphicsFact]
    public void ShaderSet_CompilesAndExposesCompleteMaterialLayout()
    {
        using var graphics = new TestGraphicsContext();

        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);

        var expectedBindings = new (string Name, int Binding)[]
        {
            ("MaterialConstants", 0),
            ("BaseColorTexture", 1),
            ("MetallicRoughnessTexture", 2),
            ("NormalTexture", 3),
            ("OcclusionTexture", 4),
            ("EmissiveTexture", 5),
            ("BaseColorSampler", 6),
            ("MetallicRoughnessSampler", 7),
            ("NormalSampler", 8),
            ("OcclusionSampler", 9),
            ("EmissiveSampler", 10),
        };

        foreach (var (name, binding) in expectedBindings)
        {
            var resourceBinding = resources.MaterialResourceBindings[name];
            Assert.Equal(binding, (int)resourceBinding.Binding);
        }

        Assert.Equal(PbrMetallicRoughnessShaderResources.MaterialConstantsBufferSize,
            (int)resources.MaterialResourceBindings["MaterialConstants"].Type.Size);
    }
}
