using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace OpenSage.Tools.WorldBuilder.Avalonia;

public class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Shared dark-theme palette ported from the MAUI App resources.</summary>
public static class WbTheme
{
    public static readonly Color Background = Color.FromRgb(0x1E, 0x1E, 0x1E);
    public static readonly Color Surface = Color.FromRgb(0x2D, 0x2D, 0x2D);
    public static readonly Color Toolbar = Color.FromRgb(0x25, 0x25, 0x26);
    public static readonly Color Panel = Color.FromRgb(0x25, 0x25, 0x25);
    public static readonly Color Text = Color.FromRgb(0xD4, 0xD4, 0xD4);
    public static readonly Color TextDim = Color.FromRgb(0x80, 0x80, 0x80);
    public static readonly Color Border = Color.FromRgb(0x3F, 0x3F, 0x46);
    public static readonly Color Accent = Color.FromRgb(0x00, 0x7A, 0xCC);
    public static readonly Color Error = Color.FromRgb(0xF4, 0x47, 0x47);
    public static readonly Color ViewportBack = Color.FromRgb(0x22, 0x22, 0x22);
}
