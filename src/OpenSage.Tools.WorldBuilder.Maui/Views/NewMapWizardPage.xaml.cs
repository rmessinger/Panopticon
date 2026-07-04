using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public partial class NewMapWizardPage : ContentPage
{
    public event EventHandler<EditorMap>? MapCreated;

    public EditorMap? Result { get; private set; }

    public NewMapWizardPage()
    {
        InitializeComponent();
    }

    private void OnHeightSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        HeightValueLabel.Text = ((int)e.NewValue).ToString();
    }

    private async void OnCancel(object? sender, EventArgs e)
    {
        Result = null;
        await Navigation.PopModalAsync();
    }

    private async void OnCreate(object? sender, EventArgs e)
    {
        if (!TryParseInputs(out var width, out var height, out var borderWidth, out var initialHeight, out var error))
        {
            ValidationError.Text = error;
            ValidationError.IsVisible = true;
            return;
        }

        ValidationError.IsVisible = false;

        Result = NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = width,
            PlayableHeight = height,
            InitialHeight = initialHeight,
            BorderWidth = borderWidth,
        });

        MapCreated?.Invoke(this, Result);
        await Navigation.PopModalAsync();
    }

    private bool TryParseInputs(
        out int width, out int height,
        out int borderWidth, out byte initialHeight,
        out string error)
    {
        width = height = borderWidth = 0;
        initialHeight = 0;
        error = string.Empty;

        if (!int.TryParse(WidthEntry.Text, out width) || width < 10)
        {
            error = "Width must be at least 10.";
            return false;
        }

        if (!int.TryParse(HeightEntry.Text, out height) || height < 10)
        {
            error = "Height must be at least 10.";
            return false;
        }

        if (!int.TryParse(BorderWidthEntry.Text, out borderWidth) || borderWidth < 2)
        {
            error = "Border width must be at least 2.";
            return false;
        }

        initialHeight = (byte)Math.Clamp((int)HeightSlider.Value, 0, 255);
        return true;
    }
}
