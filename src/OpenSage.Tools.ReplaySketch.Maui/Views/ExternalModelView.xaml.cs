#nullable enable

using System.Globalization;
using System.IO;
using OpenSage.Content.Importing;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;
using OpenSage.Tools.ReplaySketch.ViewModels;

namespace OpenSage.Tools.ReplaySketch.Maui.Views;

/// <summary>
/// External Model tab view: binds one <see cref="ExternalModelViewModel"/> built at the view
/// boundary with real session, artifact store, preview client, dispatcher, and file selector
/// services. The preview PNG is loaded through a copied stream so the artifact file is never
/// locked. Screenshot mode injects a deterministic fake view model instead.
/// </summary>
public partial class ExternalModelView : ContentView
{
    private ExternalModelViewModel? _viewModel;

    public ExternalModelView()
    {
        InitializeComponent();
    }

    /// <summary>Binds either the production view model or the screenshot fixture.</summary>
    public void Bind(ExternalModelViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        AnimationPicker.ItemsSource = viewModel.Animations.ToList();
        SummaryPanel.Children.Clear();
        foreach (var line in viewModel.SummaryLines)
        {
            SummaryPanel.Children.Add(new Label
            {
                Text = line,
                FontSize = 10,
                TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#BBB")
                    : Color.FromArgb("#555"),
            });
        }

        DiagnosticsList.ItemsSource = viewModel.Diagnostics;
        UpdateEligibility();
        UpdateEntriesFromOptions();
        _ = LoadPreviewImageAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(ExternalModelViewModel.Animations))
        {
            AnimationPicker.ItemsSource = _viewModel?.Animations.ToList();
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.SummaryLines))
        {
            if (_viewModel is { } viewModel)
            {
                SummaryPanel.Children.Clear();
                foreach (var line in viewModel.SummaryLines)
                {
                    SummaryPanel.Children.Add(new Label
                    {
                        Text = line,
                        FontSize = 10,
                        TextColor = Application.Current?.RequestedTheme == AppTheme.Dark
                            ? Color.FromArgb("#BBB")
                            : Color.FromArgb("#555"),
                    });
                }
            }
        }

        if (e.PropertyName is null or nameof(ExternalModelViewModel.Diagnostics))
        {
            DiagnosticsList.ItemsSource = _viewModel?.Diagnostics;
            DiagnosticDetailLabel.Text = _viewModel?.SelectedDiagnostic?.Message;
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
            MainThread.BeginInvokeOnMainThread(async () => await LoadPreviewImageAsync());
        }
    }

    private void UpdateEligibility()
    {
        if (_viewModel is null)
        {
            return;
        }

        EligibilityLabel.Text = _viewModel.Eligibility.CanRender
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
        YawEntry.Text = Format(preview.ModelYaw);
        OrbitEntry.Text = Format(preview.CameraOrbit);
        ElevationEntry.Text = Format(preview.CameraElevation);
        DistanceEntry.Text = Format(preview.CameraDistance);
        SampleTimeEntry.Text = Format(preview.AnimationSampleTime);
    }

    private static string Format(float value)
    {
        return value == 0 ? "0" : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private async Task LoadPreviewImageAsync()
    {
        if (_viewModel?.LatestPreviewImagePath is not { } path || !File.Exists(path))
        {
            PreviewImage.Source = null;
            return;
        }

        // Copy through a memory stream so the artifact file is never locked and can be
        // replaced by the next request.
        using var fileStream = File.OpenRead(path);
        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory);
        memory.Position = 0;
        PreviewImage.Source = ImageSource.FromStream(() => new MemoryStream(memory.ToArray()));
    }

    private void OnAnimationPickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var descriptor = AnimationPicker.SelectedItem as ExternalAnimationDescriptor;
        if (descriptor is not null)
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

    private void OnApplyPreviewClicked(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!TryParse(YawEntry.Text, out var yaw) ||
            !TryParse(OrbitEntry.Text, out var orbit) ||
            !TryParse(ElevationEntry.Text, out var elevation) ||
            !TryParse(DistanceEntry.Text, out var distance) ||
            !TryParse(SampleTimeEntry.Text, out var sampleTime))
        {
            DiagnosticDetailLabel.Text = "Preview values must be numeric.";
            return;
        }

        var current = _viewModel.PreviewOptions;
        var next = current with
        {
            ModelYaw = yaw,
            CameraOrbit = orbit,
            CameraElevation = elevation,
            CameraDistance = distance,
            AnimationSampleTime = sampleTime,
        };

        _viewModel.NextPreviewOptions = next;
        _ = _viewModel.ApplyPreviewOptionsCommand.ExecuteAsync();
    }

    private void OnDiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ExternalModelDiagnostic diagnostic)
        {
            DiagnosticDetailLabel.Text =
                $"[{diagnostic.Code}] {diagnostic.Severity} {diagnostic.Stage} {diagnostic.Source}\n{diagnostic.Message}";
            _viewModel?.SelectDiagnostic(diagnostic);
        }
    }

    private static bool TryParse(string? text, out float value)
    {
        return float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }
}
