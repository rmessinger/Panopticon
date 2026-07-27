#nullable enable

using System;
using OpenSage.Logic;

namespace OpenSage.Mods.Generals.Gui;

/// <summary>
/// Minimal end-of-match consumer: subscribes to <see cref="IGame.MatchOutcomeDecided"/>
/// and surfaces the result. A full win/lose dialog (art, layout, scripted sequence) is a
/// follow-up; this is the documented hook point such a dialog attaches to.
/// </summary>
public sealed class MatchOutcomePresenter : IDisposable
{
    private readonly IGame _game;
    private bool _disposed;

    public event EventHandler<MatchOutcomeNotification>? OutcomePresented;

    public MatchOutcomePresenter(IGame game)
    {
        _game = game;
        _game.MatchOutcomeDecided += OnOutcomeDecided;
    }

    private void OnOutcomeDecided(object? sender, EventArgs e)
    {
        var outcome = _game.CurrentOutcome;
        var notification = new MatchOutcomeNotification(
            outcome.Status,
            outcome.Reason,
            outcome.WinningPlayer?.Name,
            outcome.LosingPlayer?.Name);

        OutcomePresented?.Invoke(this, notification);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _game.MatchOutcomeDecided -= OnOutcomeDecided;
        _disposed = true;
    }
}

public sealed record MatchOutcomeNotification(
    OutcomeStatus Status,
    OutcomeReason Reason,
    string? WinningPlayerName,
    string? LosingPlayerName);
