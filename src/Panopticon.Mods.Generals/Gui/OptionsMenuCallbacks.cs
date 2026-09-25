using System.Collections.Generic;
using Panopticon.Gui.Wnd;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Mods.Generals.Gui;

[WndCallbacks]
public static class OptionsMenuCallbacks
{
    private const string ComboBoxIPPrefix = "OptionsMenu.wnd:ComboBoxIP";

    public static void OptionsMenuSystem(Control control, WndWindowMessage message, ControlCallbackContext context)
    {
        switch (message.MessageType)
        {
            case WndWindowMessageType.SelectedButton:
                switch (message.Element.Name)
                {
                    case "OptionsMenu.wnd:ButtonBack":
                        context.WindowManager.PopWindow();
                        break;
                }

                break;
        }
    }

    public static void OptionsMenuInit(Window window, IGame game)
    {
        var comboBoxIP = (ComboBox)window.Controls.FindControl(ComboBoxIPPrefix);
        var items = new List<ListBoxDataItem>();

        comboBoxIP.Items = items.ToArray();
        comboBoxIP.SelectedIndex = 0;
    }
}
