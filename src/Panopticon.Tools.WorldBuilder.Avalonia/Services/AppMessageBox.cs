using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Panopticon.Tools.WorldBuilder.Avalonia.Services;

/// <summary>Modal message dialogs replacing MAUI DisplayAlert.</summary>
public static class AppMessageBox
{
    public static Task ShowAsync(Window owner, string title, string message, string okText = "OK")
    {
        return ShowAsync(owner, title, message, okText, cancelText: null);
    }

    /// <summary>Returns true when the accept button closed the dialog.</summary>
    public static Task<bool> ShowAsync(Window owner, string title, string message, string acceptText, string? cancelText)
    {
        var result = cancelText is null;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(WbTheme.Surface),
        };

        var accept = new Button { Content = acceptText, MinWidth = 88 };
        accept.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        buttons.Children.Add(accept);

        if (cancelText is { } cancel)
        {
            var decline = new Button { Content = cancel, MinWidth = 88 };
            decline.Click += (_, _) =>
            {
                result = false;
                dialog.Close();
            };
            buttons.Children.Add(decline);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Width = 380,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(WbTheme.Text),
                },
                buttons,
            },
        };

        var completion = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => completion.TrySetResult(result);
        dialog.ShowDialog(owner);
        return completion.Task;
    }
}
