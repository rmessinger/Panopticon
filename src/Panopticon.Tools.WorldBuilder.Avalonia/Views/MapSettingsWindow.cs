using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI map-settings page.</summary>
public sealed class MapSettingsWindow : WbDialog
{
    private readonly ComboBox _weatherPicker;
    private readonly ComboBox _timeOfDayPicker;

    public MapSettingsWindow(EditorMap? map)
        : base("Map Settings", 460, 380)
    {
        _weatherPicker = new ComboBox
        {
            ItemsSource = new[] { "Normal", "Rainy", "Snowy" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _timeOfDayPicker = new ComboBox
        {
            ItemsSource = new[] { "Morning", "Afternoon", "Evening", "Night" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cancel = SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close();
        var apply = PrimaryButton("Apply");
        apply.Click += (_, _) =>
        {
            // TODO: apply settings to the map's WorldInfo when WorldInfo supports editing
            Close();
        };

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24),
                        Spacing = 16,
                        Children =
                        {
                            Heading("Map Settings"),
                            Card("Weather", _weatherPicker),
                            Card("Time of Day", _timeOfDayPicker),
                        },
                    },
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
