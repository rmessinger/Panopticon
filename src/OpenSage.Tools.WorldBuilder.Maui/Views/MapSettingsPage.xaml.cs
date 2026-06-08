using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public partial class MapSettingsPage : ContentPage
{
    private readonly EditorMap? _map;

    public MapSettingsPage(EditorMap? map)
    {
        InitializeComponent();
        _map = map;
        WeatherPicker.SelectedIndex = 0;
        TimeOfDayPicker.SelectedIndex = 0;
    }

    private async void OnCancel(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnApply(object? sender, EventArgs e)
    {
        // TODO: apply settings to the map's WorldInfo when WorldInfo supports editing
        await Navigation.PopModalAsync();
    }
}
