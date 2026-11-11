using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI new-map wizard.</summary>
public sealed class NewMapWizardWindow : WbDialog
{
    private const string CustomPresetName = "Custom";
    private const double BytesPerMiB = 1024.0 * 1024.0;

    public event EventHandler<EditorMap>? MapCreated;

    public EditorMap? Result { get; private set; }

    private readonly ComboBox _presetPicker;
    private readonly TextBox _widthEntry;
    private readonly TextBox _heightEntry;
    private readonly TextBox _borderWidthEntry;
    private readonly TextBox _initialHeightEntry;
    private readonly CheckBox _sixteenBitCheck;
    private readonly TextBlock _memoryEstimate;
    private readonly TextBlock _validationError;
    private bool _applyingPreset;

    public NewMapWizardWindow()
        : base("New Map", 520, 680)
    {
        _presetPicker = new ComboBox
        {
            ItemsSource = NewMapFactory.Presets.Select(p => p.Name).Append(CustomPresetName).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _widthEntry = Entry("200");
        _heightEntry = Entry("200");
        _borderWidthEntry = Entry("6");
        _initialHeightEntry = Entry("50");
        _sixteenBitCheck = new CheckBox
        {
            Content = "16-bit elevation (on by default for large presets)",
            Foreground = new SolidColorBrush(WbTheme.Text),
        };
        _memoryEstimate = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(WbTheme.Text),
            TextWrapping = TextWrapping.Wrap,
        };

        _presetPicker.SelectionChanged += (_, _) => OnPresetSelected();
        _widthEntry.PropertyChanged += (_, e) => SwitchToCustomOnManualEdit(e.Property);
        _heightEntry.PropertyChanged += (_, e) => SwitchToCustomOnManualEdit(e.Property);
        OnPresetSelected();

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

        var presetRow = new StackPanel
        {
            Spacing = 6,
            Children = { Label("Preset"), _presetPicker },
        };

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
                                    presetRow,
                                    dimensions,
                                    _memoryEstimate,
                                    Hint("Presets fill the fields. Custom sizes have no preset cap; only the array limit rejects a size."),
                                },
                            }),
                            Card("Initial Ground Height", new StackPanel
                            {
                                Spacing = 12,
                                Children =
                                {
                                    _initialHeightEntry,
                                    _sixteenBitCheck,
                                    Hint("Initial height: 0 to 255 without 16-bit elevation, 0 to 65535 with it. 16-bit elevation is verified on the legacy .map save path only; package round-trip of values above 255 is not covered."),
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

    private void OnPresetSelected()
    {
        var index = _presetPicker.SelectedIndex;
        if (index < 0 || index >= NewMapFactory.Presets.Count)
        {
            return;
        }

        var preset = NewMapFactory.Presets[index];
        _applyingPreset = true;
        _widthEntry.Text = preset.PlayableWidth.ToString();
        _heightEntry.Text = preset.PlayableHeight.ToString();
        _sixteenBitCheck.IsChecked = preset.Use16BitElevation;
        _applyingPreset = false;

        var parameters = TryParseParameters(out _);
        if (parameters is not null)
        {
            ShowMemoryEstimate(parameters);
        }
    }

    private void SwitchToCustomOnManualEdit(AvaloniaProperty property)
    {
        if (!_applyingPreset && property == TextBox.TextProperty)
        {
            _presetPicker.SelectedIndex = NewMapFactory.Presets.Count;
        }
    }

    private void ShowMemoryEstimate(NewMapParameters parameters)
    {
        var bytes = NewMapFactory.EstimateResidentBytes(parameters.PlayableWidth, parameters.PlayableHeight, parameters.BorderWidth, parameters.Use16BitElevation);
        _memoryEstimate.Text = $"Estimated resident memory: {bytes / BytesPerMiB:N1} MiB (estimate)";
    }

    private void OnCreate(object? sender, EventArgs e)
    {
        var parameters = TryParseParameters(out var error);
        if (parameters is null)
        {
            _validationError.Text = error;
            _validationError.IsVisible = true;
            return;
        }

        _validationError.IsVisible = false;
        ShowMemoryEstimate(parameters);

        Result = NewMapFactory.Create(parameters);

        MapCreated?.Invoke(this, Result);
        Close();
    }

    private NewMapParameters? TryParseParameters(out string error)
    {
        error = string.Empty;

        if (!int.TryParse(_widthEntry.Text, out var width))
        {
            error = "Width must be a whole number.";
            return null;
        }

        if (!int.TryParse(_heightEntry.Text, out var height))
        {
            error = "Height must be a whole number.";
            return null;
        }

        if (!int.TryParse(_borderWidthEntry.Text, out var borderWidth))
        {
            error = "Border width must be a whole number.";
            return null;
        }

        if (!int.TryParse(_initialHeightEntry.Text, out var initialHeight) || initialHeight < 0 || initialHeight > ushort.MaxValue)
        {
            error = "Initial height must be a whole number from 0 to 65535.";
            return null;
        }

        var parameters = new NewMapParameters
        {
            PlayableWidth = width,
            PlayableHeight = height,
            BorderWidth = borderWidth,
            InitialHeight = (ushort)initialHeight,
            Use16BitElevation = _sixteenBitCheck.IsChecked == true,
        };

        return NewMapFactory.TryValidate(parameters, out error) ? parameters : null;
    }
}
