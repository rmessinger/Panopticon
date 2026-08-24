using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenSage.Gui;
using OpenSage.Gui.Wnd;
using OpenSage.Gui.Wnd.Controls;
using OpenSage.Logic.Orders;

namespace OpenSage.Mods.Generals.Gui;

[WndCallbacks]
public static class ControlBarCallbacks
{
    private const string GeneralsExpPointsWnd = "GeneralsExpPoints.wnd";

    public static void W3DCommandBarBackgroundDraw(Control control, DrawingContext2D drawingContext)
    {

    }

    public static void ControlBarSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        switch (message.MessageType)
        {
            case WndWindowMessageType.SelectedButton:
                switch (message.Element.Name)
                {
                    case "ControlBar.wnd:ButtonLarge":
                        ((GeneralsControlBar)context.Game.Scene2D.ControlBar).ToggleSize();
                        break;
                    case "ControlBar.wnd:ButtonOptions":
                        context.WindowManager.PushWindow(Path.Combine("Menus", "QuitMenu.wnd"));
                        break;
                    case "ControlBar.wnd:ButtonGeneral":
                        if (context.WindowManager.TopWindow?.Name == GeneralsExpPointsWnd)
                        {
                            GeneralsExpPointsCallbacks.SetWindow(null);
                            context.WindowManager.PopWindow();
                        }
                        else
                        {
                            var window = context.WindowManager.PushWindow(GeneralsExpPointsWnd);
                            window.Name = GeneralsExpPointsWnd;
                            GeneralsExpPointsCallbacks.SetWindow(window);
                        }

                        break;
                }
                break;
        }
    }

    public static void LeftHUDInput(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        if (message.MessageType != WndWindowMessageType.MouseDown
            && message.MessageType != WndWindowMessageType.MouseRightDown)
        {
            return;
        }

        var terrainPosition = context.Game.Scene3D.RadarDrawUtil.RadarToWorldSpace(
            message.MousePosition,
            control.ClientRectangle);

        // TODO: Fix left/right mouse handling
        // - If user has selected units, left mouse should move them and right mouse moves the camera
        // - If user has not selected units, both mouse buttons move the camera
        switch (message.MessageType)
        {
            case WndWindowMessageType.MouseDown: // Left mouse moves selected units
                var unit = context.Game.Scene3D.LocalPlayer.SelectedUnits.LastOrDefault();
                if (unit != null)
                {
                    // TODO: Duplicated in OrderGeneratorSystem
                    unit.OnLocalMove(context.Game.Audio);
                    var order = Order.CreateMoveOrder(context.Game.Scene3D.GetPlayerIndex(context.Game.Scene3D.LocalPlayer), terrainPosition);
                    context.Game.NetworkMessageBuffer.AddLocalOrder(order);
                }
                break;

            case WndWindowMessageType.MouseRightDown: // Right mouse moves camera.
                context.Game.Scene3D.TacticalView.LookAt(terrainPosition);
                break;
        }
    }

    public static void W3DLeftHUDDraw(Control control, DrawingContext2D drawingContext)
    {
        control.Window.Game.Scene3D.RadarDrawUtil.Draw(
            drawingContext,
            control.ClientRectangle);

        // In minimal-HUD mode the pre-rendered bar frame is gone; outline the minimap so it
        // reads against the terrain behind it. The outline follows the aspect-fitted radar
        // rectangle, not the control bounds. The ring's screen-space edges are snapped to
        // whole pixels (the window scales fractional client coords, which rendered the left
        // edge thinner than the rest) and extend 3px past the image edge to cover raster seams.
        if (control.Window.Game.Scene2D.ControlBar is GeneralsControlBar { MinimalHud: true } &&
            control.Window.Game.Scene3D.RadarDrawUtil.GetMinimapRectangle(control.ClientRectangle) is { } minimapRect)
        {
            var window = control.Window;
            var scale = MathF.Min(
                (float)window.Size.Width / window.CreationResolution.Width,
                (float)window.Size.Height / window.CreationResolution.Height);
            var origin = new Vector2(control.Bounds.X, control.Bounds.Y);

            var left = MathF.Floor((origin.X + minimapRect.X) * scale) - 3;
            var top = MathF.Floor((origin.Y + minimapRect.Y) * scale) - 3;
            var right = MathF.Ceiling((origin.X + minimapRect.Right) * scale) + 3;
            var bottom = MathF.Ceiling((origin.Y + minimapRect.Bottom) * scale) + 3;

            // DrawRectangle strokes inward from the rect boundary and scales the stroke width
            // by the window transform, so divide by the scale to get whole-pixel thickness.
            // The stroke band's outer edge is the snapped rect boundary; the rect extends 3
            // screen px past the image edge, so the ring covers raster seams.
            drawingContext.DrawRectangle(
                new Mathematics.RectangleF(
                    left / scale - origin.X,
                    top / scale - origin.Y,
                    (right - left) / scale,
                    (bottom - top) / scale),
                new Mathematics.ColorRgbaF(0, 0, 0, 1),
                3 / scale);
        }
    }
}
