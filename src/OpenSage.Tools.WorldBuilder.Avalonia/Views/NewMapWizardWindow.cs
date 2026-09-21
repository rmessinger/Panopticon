using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI new-map wizard.</summary>
public sealed class NewMapWizardWindow : WbDialog
{
    public event EventHandler<EditorMap>? MapCreated;

    public EditorMap? Result { get; private set; }

    private readonly TextBox _widthEntry;
    private readonly TextBox _heightEntry;
    private readonly TextBox _borderWidthEntry;
    private readonly Slider _heightSlider;
    private readonly TextBlock _heightValue;
    private readonly TextBlock _validationError;

    public NewMapWizardWindow()
        : base("New Map", 520, 620)
    {
        _widthEntry = Entry("200");
        _heightEntry = Entry("200");
        _borderWidthEntry = Entry("6");
        _heightValue = new TextBlock { Text = "50", VerticalAlignment = VerticalAlignment.Center };

        _heightSlider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = 50,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _heightSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                _heightValue.Text = ((int)_heightSlider.Value).ToString();
            }
        };

        _validationError = new TextBlock
        {
            IsVisible = false,
            Foreground = new SolidColorBrush(WbTheme.Error),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };

        var dimensions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
            RowDefinitions = new RowDefinitions("Auto,8,Auto"),
        };
        Grid.SetRow(Label("Width (tiles)"), 0);
        Grid.SetColumn(Label("Width (tiles)"), 0);
        dimensions.Children.Add(Label("Width (tiles)"));
        var heightLabel = Label("Height (tiles)");
        Grid.SetRow(heightLabel, 0);
        Grid.SetColumn(heightLabel, 2);
        dimensions.Children.Add(heightLabel);
        Grid.SetRow(_widthEntry, 2);
        dimensions.Children.Add(_widthEntry);
        Grid.SetRow(_heightEntry, 2);
        Grid.SetColumn(_heightEntry, 2);
        dimensions.Children.Add(_heightEntry);

        var sliderRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,80"),
            Children =
            {
                _heightSlider,
            },
        };
        Grid.SetColumn(_heightValue, 1);
        sliderRow.Children.Add(_heightValue);

        var cancel = SecondaryButton("Cancel");
        cancel.Click += (_, _) => Close();
        var create = PrimaryButton("Create Map");
        create.Click += OnCreate;

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
                            Heading("Create New Map"),
                            Card("Map Dimensions", new StackPanel
                            {
                                Spacing = 12,
                                Children =
                                {
                                    dimensions,
                                    Hint("Common sizes: 200×200, 300×300, 400×400, 500×500"),
                                },
                            }),
                            Card("Initial Ground Height", new StackPanel
                            {
                                Spacing = 12,
                                Children =
                                {
                                    sliderRow,
                                    Hint("Height range: 0 (sea level) to 255 (max elevation)"),
                                },
                            }),
                            Card("Border Width", new StackPanel
                            {
                                Spacing = 12,
                                Children =
                                {
                                    _borderWidthEntry,
                                    Hint("Number of non-playable tiles around the map border (default: 6)"),
                                },
                            }),
                            _validationError,
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
            Children = { cancel, create },
        };
        Grid.SetRow(buttons, 1);
        ((Grid)Content).Children.Add(buttons);
    }

    private void OnCreate(object? sender, EventArgs e)
    {
        if (!TryParseInputs(out var width, out var height, out var borderWidth, out var initialHeight, out var error))
        {
            _validationError.Text = error;
            _validationError.IsVisible = true;
            return;
        }

        _validationError.IsVisible = false;

        Result = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = width,
            PlayableHeight = height,
            InitialHeight = initialHeight,
            BorderWidth = borderWidth,
        });

        MapCreated?.Invoke(this, Result);
        Close();
    }

    private bool TryParseInputs(
        out int width, out int height,
        out int borderWidth, out byte initialHeight,
        out string error)
    {
        width = height = borderWidth = 0;
        initialHeight = 0;
        error = string.Empty;

        if (!int.TryParse(_widthEntry.Text, out width) || width < 10)
        {
            error = "Width must be at least 10.";
            return false;
        }

        if (!int.TryParse(_heightEntry.Text, out height) || height < 10)
        {
            error = "Height must be at least 10.";
            return false;
        }

        if (!int.TryParse(_borderWidthEntry.Text, out borderWidth) || borderWidth < 2)
        {
            error = "Border width must be at least 2.";
            return false;
        }

        initialHeight = (byte)Math.Clamp((int)_heightSlider.Value, 0, 255);
        return true;
    }
}
