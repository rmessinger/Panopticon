using System.Diagnostics;
using Panopticon.Graphics;
using Panopticon.Input;
using Veldrid;

namespace Panopticon.Tools.TestHarness;

internal static class HarnessWindowedFrame
{
    public static void Step(Game game, GameWindow window, TextureCopier textureCopier,
        HarnessOverlay overlay, bool validationControls = false)
    {
        if (!window.PumpEvents())
        {
            game.Exit();
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var frameSeconds = overlay.PreviousFrameTimestamp == 0
            ? 0 : Stopwatch.GetElapsedTime(overlay.PreviousFrameTimestamp, start).TotalSeconds;
        overlay.PreviousFrameTimestamp = start;
        overlay.Update((float)game.RenderTime.DeltaTime.TotalSeconds, window.CurrentInputSnapshot);
        var count = window.MessageQueue.Count;
        for (var i = 0; i < count; i++)
        {
            var message = window.MessageQueue.Dequeue();
            var space = validationControls
                && message.MessageType is InputMessageType.KeyDown or InputMessageType.KeyUp
                && message.Value.Key == Key.Space;
            var mouse = message.MessageType is InputMessageType.MouseMove or InputMessageType.MouseWheel
                or InputMessageType.MouseLeftButtonDown or InputMessageType.MouseLeftButtonUp
                or InputMessageType.MouseRightButtonDown or InputMessageType.MouseRightButtonUp
                or InputMessageType.MouseMiddleButtonDown or InputMessageType.MouseMiddleButtonUp;
            if (!space && !(mouse && overlay.WantCaptureMouse))
            {
                window.MessageQueue.Enqueue(message);
            }
        }

        var updateStart = Stopwatch.GetTimestamp();
        game.Update(window.MessageQueue);
        var renderStart = Stopwatch.GetTimestamp();
        game.Panel.EnsureFrame(window.DrawableBounds);
        game.Render();
        textureCopier.Execute(game.Panel.Framebuffer.ColorTargets[0].Target, window.Swapchain.Framebuffer);
        overlay.Render(game.GraphicsDevice);
        var renderEnd = Stopwatch.GetTimestamp();
        game.GraphicsDevice.SwapBuffers(window.Swapchain);
        window.MessageQueue.Clear();
        overlay.Performance.Record(frameSeconds,
            Stopwatch.GetElapsedTime(updateStart, renderStart).TotalMilliseconds,
            Stopwatch.GetElapsedTime(renderStart, renderEnd).TotalMilliseconds);
    }
}
