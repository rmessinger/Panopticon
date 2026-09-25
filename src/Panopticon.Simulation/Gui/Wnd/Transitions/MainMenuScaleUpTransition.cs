using System;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Gui.Wnd.Transitions;

internal sealed class MainMenuScaleUpTransition : WindowTransitionOperation
{
    protected override int FrameDuration => 0; // TODO

    public MainMenuScaleUpTransition(Control element, TimeSpan startTime)
        : base(element, startTime)
    {
    }

    protected override void OnUpdate(float progress)
    {
        // TODO
    }
}
