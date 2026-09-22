using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI player-list page.</summary>
public sealed class PlayerListWindow : WbDialog
{
    public sealed record PlayerSlot(string Name, string Faction, string Human);

    public PlayerListWindow(EditorMap? map)
        : base("Player List", 560, 420)
    {
        var players = new List<PlayerSlot>();

        if (map?.MapFile?.SidesList?.Players is { Count: > 0 } mapPlayers)
        {
            foreach (var player in mapPlayers)
            {
                players.Add(new PlayerSlot(
                    string.IsNullOrEmpty(player.DisplayName) ? player.Name : player.DisplayName,
                    player.Faction,
                    player.IsHuman ? "Yes" : "No"));
            }
        }

        if (players.Count == 0)
        {
            players.Add(new PlayerSlot("Player 1", "America", "Yes"));
            players.Add(new PlayerSlot("Player 2", "China", "No"));
        }

        var close = PrimaryButton("Close");
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    Margin = new Thickness(24),
                    Content = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            Heading("Players"),
                            CreateList(players),
                        },
                    },
                },
            },
        };

        var buttons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24),
            Children = { close },
        };
        Grid.SetRow(buttons, 1);
        ((Grid)Content).Children.Add(buttons);
    }

    internal static ItemsControl CreateList(IEnumerable<PlayerSlot> players)
    {
        return new ItemsControl
        {
            ItemsSource = players,
            ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>((_, _) => new PlayerRow()),
        };
    }

    private sealed class PlayerRow : StackPanel
    {
        public PlayerRow()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            var name = new TextBlock { Width = 180, Foreground = new SolidColorBrush(WbTheme.Text) };
            var faction = new TextBlock { Width = 140, Foreground = new SolidColorBrush(WbTheme.Text) };
            var human = new TextBlock { Width = 60, Foreground = new SolidColorBrush(WbTheme.TextDim) };
            name.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Name"));
            faction.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Faction"));
            human.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Human"));
            Children.Add(name);
            Children.Add(faction);
            Children.Add(human);
        }
    }
}
