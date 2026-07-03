using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public sealed class TeamViewModel
{
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required bool IsSingleton { get; init; }
}

public partial class TeamsPage : ContentPage
{
    private readonly EditorMap? _map;

    public TeamsPage(EditorMap? map)
    {
        InitializeComponent();
        _map = map;
        LoadTeams();
    }

    private void LoadTeams()
    {
        var teams = new List<TeamViewModel>();

        if (_map?.MapFile?.GetTeams() is { Count: > 0 } mapTeams)
        {
            foreach (var team in mapTeams)
            {
                teams.Add(new TeamViewModel
                {
                    Name = team.Name,
                    Owner = team.Owner ?? "(none)",
                    IsSingleton = team.IsSingleton,
                });
            }
        }

        if (teams.Count == 0)
        {
            teams.Add(new TeamViewModel
            {
                Name = "teamPlyr",
                Owner = "Player 1",
                IsSingleton = true,
            });
        }

        TeamsListView.ItemsSource = teams;
    }

    private async void OnClose(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
