using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// Right-panel host whose <see cref="ContentPanel"/> receives the selected node's edit
/// controls from <see cref="ActionTreeView"/>.
/// </summary>
public sealed class ActionDetailView : UserControl
{
    private readonly StackPanel _detailPanel = new() { Spacing = 4, Margin = new Thickness(8) };

    public ActionDetailView()
    {
        Content = new ScrollViewer { Content = _detailPanel };
    }

    /// <summary>
    /// The layout container that <see cref="ActionTreeView"/> writes selected-node
    /// edit controls into.
    /// </summary>
    public StackPanel ContentPanel => _detailPanel;
}
