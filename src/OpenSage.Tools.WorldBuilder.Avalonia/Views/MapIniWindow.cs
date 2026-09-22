using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI map INI editor page.</summary>
public sealed class MapIniWindow : WbDialog
{
    private readonly EditorMap? _map;
    private readonly TextBox _editor;

    public MapIniWindow(EditorMap? map)
        : base("Map INI Editor", 760, 560)
    {
        _map = map;
        _editor = new TextBox
        {
            Text = map?.MapIniContent ?? string.Empty,
            AcceptsReturn = true,
            IsReadOnly = false,
            FontFamily = new FontFamily("monospace"),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cancel = SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close();
        var apply = PrimaryButton("Apply");
        apply.Click += (_, _) =>
        {
            if (_map != null)
            {
                _map.MapIniContent = _editor.Text ?? string.Empty;
                _map.MarkDirty();
            }
            Close();
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Margin = new Thickness(24),
                    Content = _editor,
                },
            },
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24),
            Children = { cancel, apply },
        };
        Grid.SetRow(buttons, 1);
        ((Grid)Content).Children.Add(buttons);
    }
}
