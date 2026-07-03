using System.Collections.Generic;
using System.Linq;
using OpenSage;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tools.ReplaySketch.Assertions;

public sealed class AssertionEvaluator
{
    private readonly IReadOnlyList<ScenarioAssertion> _assertions;
    private readonly Game _game;
    private readonly List<AssertionResult> _invariantFailures = new();
    private readonly HashSet<string> _failedInvariantIds = new();
    private bool _subscribed;
    public bool IsAborted { get; private set; }

    public AssertionEvaluator(IReadOnlyList<ScenarioAssertion> assertions, Game game)
    {
        _assertions = assertions;
        _game = game;
    }

    public void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _game.Updating += OnGameUpdating;
        _subscribed = true;
    }

    public AssertionReport Evaluate()
    {
        Unsubscribe();

        var results = new List<AssertionResult>();
        results.AddRange(_invariantFailures);

        var frame = CurrentFrame();
        foreach (var assertion in _assertions)
        {
            if (assertion is InvariantAssertion)
            {
                continue;
            }

            results.Add(assertion.Evaluate(_game, frame));
        }

        return new AssertionReport(results);
    }

    private void OnGameUpdating(object? sender, GameUpdatingEventArgs e)
    {
        var frame = CurrentFrame();
        foreach (var assertion in _assertions.OfType<InvariantAssertion>())
        {
            if (_failedInvariantIds.Contains(assertion.Id))
            {
                continue;
            }

            var result = assertion.Evaluate(_game, frame);
            if (!result.Passed)
            {
                _failedInvariantIds.Add(assertion.Id);
                _invariantFailures.Add(result);
                IsAborted = true;
            }
        }
    }

    private uint CurrentFrame() =>
        (uint)(_game.MapTime.TotalTime.TotalMilliseconds / _game.GameEngine.MsPerLogicFrame);

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _game.Updating -= OnGameUpdating;
        _subscribed = false;
    }
}
