using Panopticon.Gui.Wnd;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Mods.Generals.Gui;

[WndCallbacks]
public static class QuitMessageBoxCallbacks
{
    public static void QuitMessageBoxSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        switch (message.MessageType)
        {
            case WndWindowMessageType.SelectedButton:
                switch (message.Element.Name)
                {
                    case "QuitMessageBox.wnd:ButtonCancel":
                        context.WindowManager.PopWindow();
                        break;
                    case "QuitMessageBox.wnd:ButtonOk":
                        context.Game.Exit();
                        break;
                }

                break;
        }
    }
}
