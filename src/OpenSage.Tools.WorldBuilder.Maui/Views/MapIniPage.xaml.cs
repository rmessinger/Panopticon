using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public partial class MapIniPage : ContentPage
{
    private readonly EditorMap? _map;

    public MapIniPage(EditorMap? map)
    {
        InitializeComponent();
        _map = map;
        LoadIniContent();
    }

    private void LoadIniContent()
    {
        IniEditor.Text = _map?.MapIniContent ?? string.Empty;
    }

    private async void OnCancel(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnApply(object? sender, EventArgs e)
    {
        if (_map != null)
        {
            _map.MapIniContent = IniEditor.Text ?? string.Empty;
            _map.MarkDirty();
        }

        await Navigation.PopModalAsync();
    }
}
