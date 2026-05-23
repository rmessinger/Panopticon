namespace OpenSage.Tools.WorldBuilder.Maui;

public partial class App : Application
{
    private const int DefaultWidth = 1600;
    private const int DefaultHeight = 900;

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Width = DefaultWidth;
        window.Height = DefaultHeight;
        window.Title = "OpenSAGE World Builder";
        window.Page = new NavigationPage(new MainPage());
        return window;
    }
}
