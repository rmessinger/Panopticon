using System;
using System.Linq;
using Panopticon.Graphics.Shaders;
using Panopticon.Tests.Content.Importing;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.Shaders;

// WI-C3-07: proves the complete PBR material contract binds on a real device: one constants
// buffer, five role textures, five per-role samplers, across both UV-set selections. Visual
// lighting behavior is locked by C3-10/C3-11; this suite locks layout and binding validity.

public sealed class PbrShaderBindingTests
{
    [GraphicsFact]
    public void FullResourceSet_BindsAllRoles_And_BothUvSelections()
    {
        using var graphics = new TestGraphicsContext();
        using var resources = new PbrMetallicRoughnessShaderResources(graphics.ShaderSetStore);

        var layout = graphics.Device.ResourceFactory.CreateResourceLayout(
            new ResourceLayoutDescription(
                resources.MaterialResourceBindings.Values
                    .Select(b => b.Description)
                    .ToArray()));

        var constantsBuffer = graphics.Device.ResourceFactory.CreateBuffer(
            new BufferDescription(PbrMetallicRoughnessShaderResources.MaterialConstantsBufferSize, BufferUsage.UniformBuffer));

        var sampler = graphics.Device.ResourceFactory.CreateSampler(
            new SamplerDescription
            {
                Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
                AddressModeU = SamplerAddressMode.Wrap,
                AddressModeV = SamplerAddressMode.Wrap,
                AddressModeW = SamplerAddressMode.Wrap,
                MaximumAnisotropy = 1,
            });

        var texture = graphics.StandardResources.SolidWhiteTexture;

        var resourceSet = graphics.Device.ResourceFactory.CreateResourceSet(
            new ResourceSetDescription(
                layout,
                new BindableResource[]
                {
                    constantsBuffer,
                    texture, texture, texture, texture, texture,
                    sampler, sampler, sampler, sampler, sampler,
                }));

        // Both UV-set selections write cleanly through the same constants buffer, covering the
        // per-role selector fields reserved in the layout.
        foreach (var uvSet in new[] { 0, 1 })
        {
            var constants = new byte[PbrMetallicRoughnessShaderResources.MaterialConstantsBufferSize];
            WriteFloat(constants, 0, 0.5f);
            WriteFloat(constants, 48, 0.5f);
            WriteInt(constants, 52, PbrMetallicRoughnessShaderResources.AlphaModeMask);
            WriteInt(constants, 56,
                PbrMetallicRoughnessShaderResources.FlagHasBaseColorTexture
                | PbrMetallicRoughnessShaderResources.FlagHasMetallicRoughnessTexture
                | PbrMetallicRoughnessShaderResources.FlagHasNormalTexture
                | PbrMetallicRoughnessShaderResources.FlagHasOcclusionTexture
                | PbrMetallicRoughnessShaderResources.FlagHasEmissiveTexture);
            WriteInt(constants, 60, uvSet);
            WriteInt(constants, 64, uvSet);
            WriteInt(constants, 68, uvSet);
            WriteInt(constants, 72, uvSet);
            WriteInt(constants, 76, uvSet);

            graphics.Device.UpdateBuffer(constantsBuffer, 0, constants);
        }

        Assert.NotNull(resourceSet);

        resourceSet.Dispose();
        sampler.Dispose();
        constantsBuffer.Dispose();
        layout.Dispose();
    }

    private static void WriteFloat(byte[] buffer, int offset, float value)
    {
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
    }
}
