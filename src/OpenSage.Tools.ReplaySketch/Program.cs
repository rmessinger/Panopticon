// DEPRECATED: This ImGui/Veldrid frontend is superseded by the MAUI frontend in
// OpenSage.Tools.ReplaySketch.Maui, which provides a tree-based action editor with
// drag-and-drop support.  This project is no longer included in OpenSage.sln.
using OpenSage.Tools.ReplaySketch.UI;
using Veldrid;
using Veldrid.StartupUtilities;

namespace OpenSage.Tools.ReplaySketch;

class Program
{
    static void Main(string[] args)
    {
        Platform.Start();

        const int initialWidth = 1280;
        const int initialHeight = 800;

        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(100, 100, initialWidth, initialHeight, WindowState.Normal, "OpenSAGE Replay Sketch"),
            out var window,
            out var graphicsDevice);

        graphicsDevice.SyncToVerticalBlank = true;

        using (var commandList = graphicsDevice.ResourceFactory.CreateCommandList())
        using (var imGuiRenderer = new ImGuiRenderer(graphicsDevice, graphicsDevice.MainSwapchain.Framebuffer.OutputDescription, initialWidth, initialHeight))
        using (var gameTimer = new DeltaTimer())
        {
            window.Resized += () =>
            {
                graphicsDevice.ResizeMainWindow((uint)window.Width, (uint)window.Height);
                imGuiRenderer.WindowResized(window.Width, window.Height);
            };

            var windowOpen = true;
            window.Closed += () => windowOpen = false;

            gameTimer.Start();

            using (var mainForm = new MainForm(graphicsDevice, imGuiRenderer))
            {
                while (windowOpen)
                {
                    commandList.Begin();

                    gameTimer.Update();

                    var inputSnapshot = window.PumpEvents();

                    commandList.SetFramebuffer(graphicsDevice.MainSwapchain.Framebuffer);

                    commandList.ClearColorTarget(0, RgbaFloat.Clear);

                    imGuiRenderer.Update(
                        (float)gameTimer.CurrentGameTime.DeltaTime.TotalSeconds,
                        inputSnapshot);

                    mainForm.Draw(window);

                    imGuiRenderer.Render(graphicsDevice, commandList);

                    commandList.End();

                    graphicsDevice.SubmitCommands(commandList);

                    graphicsDevice.SwapBuffers();
                }
            }
        }

        graphicsDevice.Dispose();

        Platform.Stop();
    }
}
