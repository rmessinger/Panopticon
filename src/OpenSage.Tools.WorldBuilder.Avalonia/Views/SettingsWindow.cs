using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using OpenSage.Tools.WorldBuilder.Avalonia.Services;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI settings page (game install paths).</summary>
public sealed class SettingsWindow : WbDialog
{
    private readonly TextBox _generalsPath;
    private readonly TextBox _zeroHourPath;
    private readonly TextBox _bfmePath;

    public SettingsWindow()
        : base("Settings", 640, 460)
    {
        _generalsPath = Entry(WorldBuilderPreferences.GeneralsPath);
        _zeroHourPath = Entry(WorldBuilderPreferences.ZeroHourPath);
        _bfmePath = Entry(WorldBuilderPreferences.BfmePath);

        var cancel = SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close();
        var save = PrimaryButton("Save");
        save.Click += (_, _) =>
        {
            WorldBuilderPreferences.SavePaths(
                _generalsPath.Text ?? string.Empty,
                _zeroHourPath.Text ?? string.Empty,
                _bfmePath.Text ?? string.Empty);
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
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            Heading("Settings"),
                            Card("Generals Install Path", PathRow(_generalsPath)),
                            Card("Zero Hour Install Path", PathRow(_zeroHourPath)),
                            Card("Battle for Middle-earth Install Path", PathRow(_bfmePath)),
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
            Children = { cancel, save },
        };
        Grid.SetRow(buttons, 1);
        ((Grid)Content).Children.Add(buttons);
    }

    private static StackPanel PathRow(TextBox entry)
    {
        var browse = SecondaryButton("Browse…");
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(entry) is not Window owner)
            {
                return;
            }
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
            });
            if (folders.Count == 1)
            {
                entry.Text = folders[0].Path.LocalPath;
            }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                entry,
                browse,
            },
        };
        entry.HorizontalAlignment = HorizontalAlignment.Stretch;
        return row;
    }
}
