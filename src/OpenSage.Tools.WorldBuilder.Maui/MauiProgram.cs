using CommunityToolkit.Maui;
using OpenSage.Tools.WorldBuilder.Maui.Views;

#if WINDOWS
using OpenSage.Tools.WorldBuilder.Maui.Platforms.Windows;
#elif MACCATALYST
using OpenSage.Tools.WorldBuilder.Maui.Platforms.MacCatalyst;
#endif

namespace OpenSage.Tools.WorldBuilder.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureMauiHandlers(handlers =>
            {
#if WINDOWS || MACCATALYST
                handlers.AddHandler<VeldridViewport, VeldridViewportHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        return builder.Build();
    }
}
