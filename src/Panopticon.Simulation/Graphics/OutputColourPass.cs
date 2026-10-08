using Panopticon.Graphics.Shaders;
using Veldrid;

namespace Panopticon.Graphics;

/// <summary>
/// Presents the linear-space composition texture: one sRGB transfer plus ordered dither,
/// straight into the output framebuffer. Counterpart of <see cref="TextureCopier"/> for the
/// Linear colour space mode.
/// </summary>
public sealed class OutputColourPass : DisposableBase
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly CommandList _commandList;
    private readonly OutputColourShaderResources _shaderResources;

    public OutputColourPass(IGame game, in OutputDescription outputDescription)
    {
        _graphicsDevice = game.GraphicsDevice;

        _commandList = AddDisposable(_graphicsDevice.ResourceFactory.CreateCommandList());

        _shaderResources = AddDisposable(new OutputColourShaderResources(
            game.GraphicsLoadContext.ShaderSetStore,
            outputDescription));
    }

    public void Execute(Texture source, Framebuffer destination)
    {
        _commandList.Begin();

        _commandList.PushDebugGroup("Encoding linear colour to output");

        _commandList.SetFramebuffer(destination);

        _commandList.SetPipeline(_shaderResources.Pipeline);

        _commandList.SetGraphicsResourceSet(
            0,
            _shaderResources.GetOrCreateSourceResourceSet(source, _graphicsDevice.PointSampler));

        _commandList.Draw(3);

        _commandList.PopDebugGroup();

        _commandList.End();

        _graphicsDevice.SubmitCommands(_commandList);
    }
}
