using Panopticon.Gui.Wnd;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Mods.Generals.Gui;

[WndCallbacks]
public static class MessageBoxCallbacks
{
    public static void MessageBoxSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        switch (message.MessageType)
        {
            case WndWindowMessageType.SelectedButton:
                switch (message.Element.Name)
                {
                    case "MessageBox.wnd:ButtonOk":
                        context.WindowManager.PopWindow();
                        break;
                }

                break;
        }
    }
}
