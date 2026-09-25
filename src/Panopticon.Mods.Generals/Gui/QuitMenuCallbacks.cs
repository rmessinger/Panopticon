using Panopticon.Gui.Wnd;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Mods.Generals.Gui;

[WndCallbacks]
public static class QuitMenuCallbacks
{
    public static void QuitMenuSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        switch (message.MessageType)
        {
            case WndWindowMessageType.SelectedButton when message.Element.Name == "QuitMenu.wnd:ButtonReturn":
                context.WindowManager.PopWindow();
                break;
            case WndWindowMessageType.SelectedButton when message.Element.Name == "QuitMenu.wnd:ButtonExit":
                context.WindowManager.PopWindow();
                // TODO: Show quit confirmation?
                context.Game.EndGame();
                break;
        }
    }
}
