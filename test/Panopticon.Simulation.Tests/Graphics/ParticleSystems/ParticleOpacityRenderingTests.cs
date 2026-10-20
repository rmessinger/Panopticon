using System.Numerics;
using Panopticon.Graphics;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;
using Veldrid;
using Xunit;

namespace Panopticon.Tests.Graphics.ParticleSystems;

[Collection("GraphicsTests")]
public sealed class ParticleOpacityRenderingTests
{
    [GraphicsFact]
    public void SampledOpacityIsClampedWithoutChangingRgbOrAdditiveFlames()
    {
        using var device = GraphicsDeviceUtility.CreateGraphicsDevice(null, null);
        var factory = device.ResourceFactory;
        // HDR targets expose negative blending that normalized targets clamp implicitly.
        using var target = factory.CreateTexture(TextureDescription.Texture2D(
            8, 8, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget));
        using var staging = factory.CreateTexture(TextureDescription.Texture2D(
            8, 8, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Staging));
        using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, target));
        using var store = new ShaderSetStore(device, framebuffer.OutputDescription);
        using var shader = new ParticleShaderResources(store);
        using var texture = factory.CreateTexture(TextureDescription.Texture2D(
            1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        device.UpdateTexture(texture, new byte[] { 255, 128, 64, 128 }, 0, 0, 0, 1, 1, 1, 0, 0);

        using var globals = factory.CreateBuffer(new BufferDescription(128, BufferUsage.UniformBuffer));
        device.UpdateBuffer(globals, 0, new GlobalShaderResources.GlobalConstants
        {
            ViewProjection = Matrix4x4.Identity,
            CameraPosition = Vector3.UnitZ,
        });
        using var constants = factory.CreateBuffer(new BufferDescription(16, BufferUsage.UniformBuffer));
        device.UpdateBuffer(constants, 0, new Vector4(0, 0, 0, 1));
        using var globalSet = factory.CreateResourceSet(new ResourceSetDescription(shader.ResourceLayouts[0], globals));
        // Forward-pass bindings are reflected even though the particle shader does not read them.
        using var passConstants = factory.CreateBuffer(new BufferDescription(1024, BufferUsage.UniformBuffer));
        using var decals = factory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly, 16));
        using var passSet = factory.CreateResourceSet(new ResourceSetDescription(
            shader.ResourceLayouts[1],
            passConstants, passConstants, texture, device.PointSampler,
            passConstants, texture, device.PointSampler,
            texture, device.PointSampler, passConstants, decals));
        using var materialSet = factory.CreateResourceSet(new ResourceSetDescription(
            shader.ResourceLayouts[2], constants, texture, device.PointSampler));
        using var vertices = factory.CreateBuffer(new BufferDescription(
            4 * ParticleShaderResources.ParticleVertex.VertexDescriptor.Stride, BufferUsage.VertexBuffer));
        using var indices = factory.CreateBuffer(new BufferDescription(12, BufferUsage.IndexBuffer));
        device.UpdateBuffer(indices, 0, new ushort[] { 0, 2, 1, 1, 2, 3 });
        using var commands = factory.CreateCommandList();

        Vector4 Render(float alpha, BlendStateDescription blend)
        {
            using var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                blend,
                DepthStencilStateDescription.Disabled,
                RasterizerStateDescription.CullNone,
                PrimitiveTopology.TriangleList,
                shader.Description,
                shader.ResourceLayouts,
                framebuffer.OutputDescription));
            var vertex = new ParticleShaderResources.ParticleVertex
            {
                Position = new Vector3(0, 0, 0.5f),
                Size = 1,
                Color = Vector3.One,
                Alpha = alpha,
            };
            device.UpdateBuffer(vertices, 0, new[] { vertex, vertex, vertex, vertex });
            commands.Begin();
            commands.SetFramebuffer(framebuffer);
            commands.ClearColorTarget(0, new RgbaFloat(0.2f, 0.4f, 0.6f, 1));
            commands.SetPipeline(pipeline);
            commands.SetGraphicsResourceSet(0, globalSet);
            commands.SetGraphicsResourceSet(1, passSet);
            commands.SetGraphicsResourceSet(2, materialSet);
            commands.SetVertexBuffer(0, vertices);
            commands.SetIndexBuffer(indices, IndexFormat.UInt16);
            commands.DrawIndexed(6);
            commands.CopyTexture(target, staging);
            commands.End();
            device.SubmitCommands(commands);
            device.WaitForIdle();
            var mapped = device.Map<Vector4>(staging, MapMode.Read);
            var pixel = mapped[4, 4];
            device.Unmap(staging);
            return pixel;
        }

        var zero = Render(0, BlendStateDescription.SingleAlphaBlend);
        Assert.Equal(zero, Render(-2, BlendStateDescription.SingleAlphaBlend));
        var opaque = Render(2, BlendStateDescription.SingleAlphaBlend);
        Assert.Equal(opaque, Render(4, BlendStateDescription.SingleAlphaBlend));
        var half = Render(1, BlendStateDescription.SingleAlphaBlend);
        Assert.InRange(half.X, 0.600f, 0.604f);
        Assert.InRange(half.Y, 0.450f, 0.454f);
        Assert.InRange(half.Z, 0.424f, 0.426f);
        Assert.Equal(1f, opaque.X);
        Assert.InRange(opaque.Y, 0.501f, 0.503f);
        Assert.InRange(opaque.Z, 0.250f, 0.252f);

        var additive = Render(0, Panopticon.Graphics.BlendStateDescriptionUtility.SingleAdditiveBlendNoAlpha);
        Assert.InRange(additive.X, 1.199f, 1.201f);
        Assert.True(additive.Y > zero.Y);
        Assert.True(additive.Z > zero.Z);
    }
}
