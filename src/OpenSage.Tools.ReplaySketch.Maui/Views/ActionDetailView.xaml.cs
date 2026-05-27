namespace OpenSage.Tools.ReplaySketch.Maui.Views;

public partial class ActionDetailView : ContentView
{
    public ActionDetailView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The layout container that <see cref="ActionTreeView"/> writes selected-node
    /// edit controls into.
    /// </summary>
    public VerticalStackLayout ContentPanel => DetailPanel;
}
