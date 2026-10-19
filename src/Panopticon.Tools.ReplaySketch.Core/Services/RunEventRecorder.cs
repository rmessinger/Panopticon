using System;
using System.Collections.Generic;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Tools.ReplaySketch.Model;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// WI-A5-09: records the run's structured event stream into a run bundle. Mirrors the semantic
/// mapping of <see cref="ReplayActionMonitor"/> (whose events it also consumes) and stamps every
/// record with the simulation frame at raise time, the category, the acting player, and the
/// entity identity where one exists. Records are plain data; <see cref="RunBundle"/> serializes
/// them.
/// </summary>
public sealed class RunEventRecorder : IDisposable
{
    private readonly IGame _game;
    private readonly Func<uint> _currentFrame;
    private readonly List<RunEventRecord> _events = [];

    public RunEventRecorder(IGame game, ReplayActionMonitor monitor, Func<uint> currentFrame)
    {
        _game = game;
        _currentFrame = currentFrame;

        monitor.ActionDispatched += OnActionDispatched;
        monitor.ActionCompleted += OnActionCompleted;
        _game.ObjectCreated += OnObjectCreated;
        _game.ObjectDestroyed += OnObjectDestroyed;
        _game.ConstructionCompleted += OnConstructionCompleted;
    }

    public IReadOnlyList<RunEventRecord> Events => _events;

    private void OnActionDispatched(object? sender, ReplayActionEventArgs e) =>
        Add("actionDispatched", e.ActionType.ToString(), e.Player, e.DefinitionName, null);

    private void OnActionCompleted(object? sender, ReplayActionEventArgs e) =>
        Add("actionCompleted", e.ActionType.ToString(), e.Player, e.DefinitionName, null);

    private void OnObjectCreated(object? sender, GameObjectCreatedEventArgs e) =>
        Add(
            "objectCreated",
            null,
            e.GameObject.Owner,
            e.GameObject.Definition.Name,
            e.GameObject.Id.Index);

    private void OnObjectDestroyed(object? sender, GameObjectDestroyedEventArgs e) =>
        Add(
            "objectDestroyed",
            null,
            e.GameObject.Owner,
            e.GameObject.Definition.Name,
            e.GameObject.Id.Index);

    private void OnConstructionCompleted(object? sender, GameObjectConstructionCompletedEventArgs e) =>
        Add(
            "constructionCompleted",
            null,
            e.GameObject.Owner,
            e.GameObject.Definition.Name,
            e.GameObject.Id.Index);

    private void Add(
        string category,
        string? actionType,
        Player? player,
        string? definitionName,
        uint? objectId)
    {
        _events.Add(new RunEventRecord(
            _currentFrame(),
            category,
            actionType,
            player?.Name,
            player is null ? null : (int?)player.Id,
            definitionName,
            objectId));
    }

    public void Dispose()
    {
        _game.ObjectCreated -= OnObjectCreated;
        _game.ObjectDestroyed -= OnObjectDestroyed;
        _game.ConstructionCompleted -= OnConstructionCompleted;
    }
}
