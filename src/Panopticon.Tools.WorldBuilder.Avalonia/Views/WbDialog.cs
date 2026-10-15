using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Panopticon.Tools.WorldBuilder.Avalonia.Views;

/// <summary>
/// Base class for WorldBuilder modal dialogs: dark theme, standard margins,
/// and helper factories for the common labeled-input rows.
/// </summary>
public abstract class WbDialog : Window
{
    protected WbDialog(string title, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(WbTheme.Panel);
        Foreground = new SolidColorBrush(WbTheme.Text);
    }

    protected static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(WbTheme.Text),
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    protected static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
        };
    }

    protected static TextBlock Hint(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        };
    }

    protected static TextBox Entry(string text = "")
    {
        return new TextBox
        {
            Text = text,
            PlaceholderText = "",
            Foreground = new SolidColorBrush(WbTheme.Text),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        };
    }

    protected static Button PrimaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = new SolidColorBrush(WbTheme.Accent),
            Foreground = Brushes.White,
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    protected static Button SecondaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    protected static Border Card(string title, Control content)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(WbTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(WbTheme.Surface),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    },
                    content,
                },
            },
        };
    }
}
