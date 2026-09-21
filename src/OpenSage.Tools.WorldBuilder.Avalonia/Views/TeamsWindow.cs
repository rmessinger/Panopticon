using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Avalonia.Views;

/// <summary>Modal replacement for the MAUI teams page.</summary>
public sealed class TeamsWindow : WbDialog
{
    public sealed record Team(string Name, string Owner, string Singleton);

    public TeamsWindow(EditorMap? map)
        : base("Teams", 560, 420)
    {
        var teams = new List<Team>();

        if (map?.MapFile?.GetTeams() is { Count: > 0 } mapTeams)
        {
            foreach (var team in mapTeams)
            {
                teams.Add(new Team(team.Name, team.Owner ?? "(none)", team.IsSingleton ? "Yes" : "No"));
            }
        }

        if (teams.Count == 0)
        {
            teams.Add(new Team("teamPlyr", "Player 1", "Yes"));
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
                            Heading("Teams"),
                            new ItemsControl
                            {
                                ItemsSource = teams,
                                ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>((_, _) => new TeamRow()),
                            },
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

    private sealed class TeamRow : StackPanel
    {
        public TeamRow()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            var name = new TextBlock { Width = 220, Foreground = new SolidColorBrush(WbTheme.Text) };
            var owner = new TextBlock { Width = 160, Foreground = new SolidColorBrush(WbTheme.Text) };
            var singleton = new TextBlock { Width = 80, Foreground = new SolidColorBrush(WbTheme.TextDim) };
            name.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Name"));
            owner.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Owner"));
            singleton.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("Singleton"));
            Children.Add(name);
            Children.Add(owner);
            Children.Add(singleton);
        }
    }
}
