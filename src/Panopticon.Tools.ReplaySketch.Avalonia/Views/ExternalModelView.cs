using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Panopticon.Content.Importing;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.ReplaySketch.ViewModels;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Views;

/// <summary>
/// External Model tab: source controls and clips, structural summary, captured preview with
/// correction entries, and filterable diagnostics with selected detail. Ported from the MAUI
/// view; the preview PNG loads through a copied stream so artifact files never stay locked.
/// </summary>
public sealed class ExternalModelView : UserControl
{
    private static readonly IBrush SectionBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush FieldBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#999999"));

    private ExternalModelViewModel? _viewModel;

    private readonly ComboBox _animationPicker = new();
    private readonly StackPanel _summaryPanel = new() { Spacing = 12 };
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform };
    private readonly TextBox _yawEntry = new();
    private readonly TextBox _orbitEntry = new();
    private readonly TextBox _elevationEntry = new();
    private readonly TextBox _distanceEntry = new();
    private readonly TextBox _sampleTimeEntry = new();
    private readonly TextBlock _eligibilityLabel = new();
    private readonly ListBox _diagnosticsList = new();
    private readonly TextBlock _diagnosticDetail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _stateLabel = new() { FontWeight = FontWeight.Bold };
    private readonly TextBlock _sourceLabel = new();
    private readonly TextBlock _animationSourceLabel = new() { Foreground = MutedBrush };

    public ExternalModelView()
    {
        _animationPicker.SelectionChanged += OnAnimationPickerSelectedIndexChanged;
        _diagnosticsList.SelectionChanged += OnDiagnosticSelected;

        var applyPreviewButton = new Button
        {
            Content = "Apply Preview Options",
            Padding = new Thickness(8, 3),
        };
        applyPreviewButton.Click += OnApplyPreviewClicked;

        var sourceArea = new StackPanel { Spacing = 4 };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttonRow.Children.Add(CommandButton("Choose Model...", nameof(ExternalModelViewModel.ChooseModelCommand)));
        buttonRow.Children.Add(CommandButton("Reload", nameof(ExternalModelViewModel.ReloadCommand)));
        buttonRow.Children.Add(CommandButton("Choose Animation...", nameof(ExternalModelViewModel.ChooseAnimationCommand)));
        buttonRow.Children.Add(CommandButton("Clear Animation", nameof(ExternalModelViewModel.ClearAnimationCommand)));
        buttonRow.Children.Add(CommandButton("Reset", nameof(ExternalModelViewModel.ResetCommand)));
        buttonRow.Children.Add(CommandButton("Cancel", nameof(ExternalModelViewModel.CancelCommand)));
        sourceArea.Children.Add(buttonRow);

        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _stateLabel.Foreground = FieldBrush;
        _sourceLabel.Foreground = new SolidColorBrush(Color.Parse("#BBBBBB"));
        stateRow.Children.Add(_stateLabel);
        stateRow.Children.Add(_sourceLabel);
        stateRow.Children.Add(_animationSourceLabel);
        sourceArea.Children.Add(stateRow);
        sourceArea.Children.Add(_animationPicker);

        var summaryArea = new StackPanel { Spacing = 0 };
        summaryArea.Children.Add(new TextBlock
        {
            Text = "Summary",
            FontWeight = FontWeight.Bold,
            Foreground = SectionBrush,
        });
        var summaryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        summaryRow.Children.Add(_summaryPanel);
        summaryArea.Children.Add(summaryRow);

        var previewBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#444")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
        };
        previewBorder.Child = _previewImage;

        var controls = new StackPanel { Spacing = 3, MinWidth = 200 };
        controls.Children.Add(new TextBlock
        {
            Text = "Preview corrections",
            FontWeight = FontWeight.Bold,
            Foreground = SectionBrush,
        });
        _yawEntry.Watermark = "Yaw (rad)";
        _orbitEntry.Watermark = "Camera orbit (rad)";
        _elevationEntry.Watermark = "Camera elevation (rad)";
        _distanceEntry.Watermark = "Camera distance";
        _sampleTimeEntry.Watermark = "Sample time (s)";
        controls.Children.Add(_yawEntry);
        controls.Children.Add(_orbitEntry);
        controls.Children.Add(_elevationEntry);
        controls.Children.Add(_distanceEntry);
        controls.Children.Add(_sampleTimeEntry);
        controls.Children.Add(applyPreviewButton);
        controls.Children.Add(CommandButton("Render", nameof(ExternalModelViewModel.RenderCommand)));
        _eligibilityLabel.Foreground = new SolidColorBrush(Color.Parse("#AAAAAA"));
        controls.Children.Add(_eligibilityLabel);

        var previewGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,220"), ColumnSpacing = 6 };
        previewGrid.Children.Add(previewBorder);
        Grid.SetColumn(controls, 1);
        previewGrid.Children.Add(controls);

        var diagnosticsArea = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        diagnosticsArea.Children.Add(new TextBlock
        {
            Text = "Diagnostics",
            FontWeight = FontWeight.Bold,
            Foreground = SectionBrush,
        });
        _diagnosticsList.ItemTemplate = new FuncDataTemplate<ExternalModelDiagnostic>((diagnostic, _) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2) };
            if (diagnostic is not null)
            {
                row.Children.Add(new TextBlock
                {
                    Text = diagnostic.Code,
                    Foreground = new SolidColorBrush(Color.Parse("#E77")),
                });
                row.Children.Add(new TextBlock { Text = diagnostic.Severity.ToString(), Foreground = MutedBrush });
                row.Children.Add(new TextBlock
                {
                    Text = diagnostic.Message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 700,
                    Foreground = FieldBrush,
                });
            }

            return row;
        });
        Grid.SetRow(_diagnosticsList, 1);
        diagnosticsArea.Children.Add(_diagnosticsList);

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(6),
            RowSpacing = 4,
        };
        layout.Children.Add(sourceArea);
        Grid.SetRow(summaryArea, 1);
        layout.Children.Add(summaryArea);
        Grid.SetRow(previewGrid, 2);
        layout.Children.Add(previewGrid);
        Grid.SetRow(diagnosticsArea, 3);
        layout.Children.Add(diagnosticsArea);
        Grid.SetRow(_diagnosticDetail, 4);
        layout.Children.Add(_diagnosticDetail);

        Content = layout;
    }

    private Button CommandButton(string text, string commandPropertyName)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
            CommandParameter = commandPropertyName,
        };
        button.CommandParameter = commandPropertyName;
        button.Click += (_, _) =>
        {
            if (_viewModel is null)
            {
                return;
            }

            var command = typeof(ExternalModelViewModel).GetProperty(commandPropertyName)?.GetValue(_viewModel)
                as System.Windows.Input.ICommand;
            command?.Execute(null);
        };
        return button;
    }

    /// <summary>Binds either the production view model or the screenshot fixture.</summary>
    public void Bind(ExternalModelViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _animationPicker.ItemsSource = viewModel.Animations.ToList();
        RebuildSummary();
        _diagnosticsList.ItemsSource = viewModel.Diagnostics;
        UpdateEligibility();
        UpdateEntriesFromOptions();
        UpdateState();
        _ = LoadPreviewImageAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(ExternalModelViewModel.Animations))
        {
            _animationPicker.ItemsSource = _viewModel?.Animations.ToList();
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.SummaryLines))
        {
            RebuildSummary();
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.Diagnostics))
        {
            _diagnosticsList.ItemsSource = _viewModel?.Diagnostics;
            _diagnosticDetail.Text = _viewModel?.SelectedDiagnostic?.Message;
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.Eligibility))
        {
            UpdateEligibility();
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.PreviewOptions))
        {
            UpdateEntriesFromOptions();
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.LatestPreviewImagePath))
        {
            _ = LoadPreviewImageAsync();
        }

        UpdateState();
    }

    private void UpdateState()
    {
        if (_viewModel is null)
        {
            return;
        }

        _stateLabel.Text = _viewModel.StateText;
        _sourceLabel.Text = _viewModel.SourceDisplayName ?? "";
        _animationSourceLabel.Text = _viewModel.AnimationSourceDisplayName ?? "";
    }

    private void RebuildSummary()
    {
        _summaryPanel.Children.Clear();
        if (_viewModel is null)
        {
            return;
        }

        foreach (var line in _viewModel.SummaryLines)
        {
            _summaryPanel.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = MutedBrush,
            });
        }
    }

    private void UpdateEligibility()
    {
        if (_viewModel is null)
        {
            return;
        }

        _eligibilityLabel.Text = _viewModel.Eligibility.CanRender
            ? "Eligible to render"
            : string.Join(", ", _viewModel.Eligibility.BlockingDiagnosticCodes.Concat(
                    _viewModel.Eligibility.MissingCapabilities)
                .DefaultIfEmpty("not ready"));
    }

    private void UpdateEntriesFromOptions()
    {
        if (_viewModel is null)
        {
            return;
        }

        var preview = _viewModel.PreviewOptions;
        _yawEntry.Text = Format(preview.ModelYaw);
        _orbitEntry.Text = Format(preview.CameraOrbit);
        _elevationEntry.Text = Format(preview.CameraElevation);
        _distanceEntry.Text = Format(preview.CameraDistance);
        _sampleTimeEntry.Text = Format(preview.AnimationSampleTime);
    }

    private static string Format(float value)
    {
        return value == 0 ? "0" : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task LoadPreviewImageAsync()
    {
        if (_viewModel?.LatestPreviewImagePath is not { } path || !File.Exists(path))
        {
            _previewImage.Source = null;
            return;
        }

        // Copy through a memory stream so the artifact file is never locked and can be
        // replaced by the next request.
        await using var fileStream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory);
        memory.Position = 0;
        _previewImage.Source = new Bitmap(memory);
    }

    private void OnAnimationPickerSelectedIndexChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_animationPicker.SelectedItem is ExternalAnimationDescriptor descriptor)
        {
            try
            {
                _viewModel.SelectAnimation(descriptor.Key);
            }
            catch (InvalidDataException)
            {
                // Selection raced a source replacement; the picker refreshes on the next
                // snapshot publication.
            }
        }
    }

    private void OnApplyPreviewClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!float.TryParse(_yawEntry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var yaw) ||
            !float.TryParse(_orbitEntry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var orbit) ||
            !float.TryParse(_elevationEntry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var elevation) ||
            !float.TryParse(_distanceEntry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var distance) ||
            !float.TryParse(_sampleTimeEntry.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sampleTime))
        {
            _diagnosticDetail.Text = "Preview values must be numeric.";
            return;
        }

        var current = _viewModel.PreviewOptions;
        _viewModel.NextPreviewOptions = current with
        {
            ModelYaw = yaw,
            CameraOrbit = orbit,
            CameraElevation = elevation,
            CameraDistance = distance,
            AnimationSampleTime = sampleTime,
        };
        _ = _viewModel.ApplyPreviewOptionsCommand.ExecuteAsync();
    }

    private void OnDiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_diagnosticsList.SelectedItem is ExternalModelDiagnostic diagnostic)
        {
            _diagnosticDetail.Text =
                $"[{diagnostic.Code}] {diagnostic.Severity} {diagnostic.Stage} {diagnostic.Source}\n{diagnostic.Message}";
            _viewModel?.SelectDiagnostic(diagnostic);
        }
    }
}
