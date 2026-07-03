namespace OpenSage.Tools.ReplaySketch.Maui;

public partial class App : Application
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 800;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(new MainPage()))
        {
            Width = DefaultWidth,
            Height = DefaultHeight,
        };
    }
}
