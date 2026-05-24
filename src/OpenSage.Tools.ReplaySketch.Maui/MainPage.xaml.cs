using OpenSage.Tools.ReplaySketch.Maui.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();

    public MainPage()
    {
        InitializeComponent();

        ScenarioView.BindViewModel(_vm);
        MapPreviewView.BindViewModel(_vm);
        ActionTreeView.BindViewModel(_vm);
        ExportView.BindViewModel(_vm);

        // When scenario or map changes, redraw the map preview and refresh action tree
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map)
                                or nameof(MainViewModel.SelectedSlotIndex))
            {
                MapPreviewView.Invalidate();
                ActionTreeView.RefreshTree();
            }
        };
    }
}
