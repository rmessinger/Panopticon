using Avalonia.Controls;

namespace OpenSage.Tools.ReplaySketch.Avalonia;

public class MainWindow : Window
{
    public MainWindow()
    {
        Title = "OpenSAGE Replay Sketch (Avalonia)";
        Width = 1280;
        Height = 800;
        Content = new TextBlock { Text = "ReplaySketch Avalonia — Validation Ground editor coming next.", Margin = new(20) };
    }
}
