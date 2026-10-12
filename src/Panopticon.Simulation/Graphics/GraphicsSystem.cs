using System;
using Panopticon.Graphics.Rendering;
using Veldrid;

namespace Panopticon.Graphics;

public sealed class GraphicsSystem : GameSystem
{
    private readonly RenderContext _renderContext;

    internal RenderPipeline RenderPipeline { get; private set; }

    /// <summary>Per-frame render counters (B5); valid after the last Render call.</summary>
    public RenderStatistics RenderStatistics => RenderPipeline.Statistics;

    /// <summary>Render-path span sink (B5): buildRenderList, shadowPass, forwardPass, render.</summary>
    public event Action<string, TimeSpan>? RenderSpan
    {
        add => RenderPipeline.RenderSpan += value;
        remove => RenderPipeline.RenderSpan -= value;
    }

    public Texture ShadowMap => RenderPipeline.ShadowMap;
    public Texture ReflectionMap => RenderPipeline.ReflectionMap;
    public Texture RefractionMap => RenderPipeline.RefractionMap;

    public GraphicsSystem(IGame game)
        : base(game)
    {
        _renderContext = new RenderContext();
    }

    public override void Initialize()
    {
        RenderPipeline = AddDisposable(new RenderPipeline(Game));
    }

    internal void Draw(in TimeInterval gameTime)
    {
        _renderContext.ContentManager = Game.ContentManager;
        _renderContext.GraphicsDevice = Game.GraphicsDevice;
        _renderContext.Scene3D = Game.Scene3D;
        _renderContext.Scene2D = Game.Scene2D;
        _renderContext.RenderTarget = Game.Panel.Framebuffer;
        _renderContext.GameTime = gameTime;

        RenderPipeline.Execute(_renderContext);
    }
}
