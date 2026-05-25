using OpenSage.Tools.ReplaySketch.Maui.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();

    public MainPage()
    {
        InitializeComponent();

        ScenarioView.BindViewModel(_vm);
        ActionTreeView.BindViewModel(_vm);
        ActionTreeView.SetExternalDetailPanel(ActionDetail.ContentPanel);
        ExportView.BindViewModel(_vm);

        // When scenario or map changes, refresh the action tree
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Map)
                                or nameof(MainViewModel.SelectedSlotIndex))
            {
                ActionTreeView.RefreshTree();
            }
        };
    }
}
