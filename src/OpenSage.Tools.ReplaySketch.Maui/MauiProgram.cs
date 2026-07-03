using CommunityToolkit.Maui;
using OpenSage.Tools.ReplaySketch.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace OpenSage.Tools.ReplaySketch.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        return builder.Build();
    }
}
