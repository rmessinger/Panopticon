using OpenSage.Tools.WorldBuilder.Core;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public sealed class PlayerSlotViewModel
{
    public required string Name { get; init; }
    public required string Faction { get; init; }
    public required bool IsHuman { get; init; }
}

public partial class PlayerListPage : ContentPage
{
    private readonly EditorMap? _map;

    public PlayerListPage(EditorMap? map)
    {
        InitializeComponent();
        _map = map;
        LoadPlayers();
    }

    private void LoadPlayers()
    {
        var players = new List<PlayerSlotViewModel>();

        if (_map?.MapFile?.SidesList?.Players is { Count: > 0 } mapPlayers)
        {
            foreach (var player in mapPlayers)
            {
                players.Add(new PlayerSlotViewModel
                {
                    Name = string.IsNullOrEmpty(player.DisplayName) ? player.Name : player.DisplayName,
                    Faction = player.Faction,
                    IsHuman = player.IsHuman,
                });
            }
        }

        if (players.Count == 0)
        {
            players.Add(new PlayerSlotViewModel
            {
                Name = "Player 1",
                Faction = "America",
                IsHuman = true,
            });
            players.Add(new PlayerSlotViewModel
            {
                Name = "Player 2",
                Faction = "China",
                IsHuman = false,
            });
        }

        PlayerListView.ItemsSource = players;
    }

    private async void OnClose(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
