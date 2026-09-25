using System;
using Panopticon.Gui.Wnd.Controls;

namespace Panopticon.Gui.Wnd.Transitions;

// TODO: Implement.
internal class WinScaleUpTransition : WindowTransitionOperation
{
    public WinScaleUpTransition(Control element, TimeSpan startTime) : base(element, startTime) { }

    protected override int FrameDuration => 6;

    protected override void OnUpdate(float progress) { }
}
