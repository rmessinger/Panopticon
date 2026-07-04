using CommunityToolkit.Maui;
using OpenSage.Tools.ReplaySketch.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace OpenSage.Tools.ReplaySketch.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        if (OperatingSystem.IsAndroidVersionAtLeast(21)
            || OperatingSystem.IsIOSVersionAtLeast(15)
            || OperatingSystem.IsMacCatalystVersionAtLeast(15)
            || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            builder.UseMauiCommunityToolkit();
        }

        builder
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        return builder.Build();
    }
}
