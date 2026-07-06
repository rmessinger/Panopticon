using System;
using System.Globalization;
using System.Text.Json.Serialization;
using OpenSage;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Model;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StateAssertion), "state")]
[JsonDerivedType(typeof(InvariantAssertion), "invariant")]
[JsonDerivedType(typeof(OutcomeAssertion), "outcome")]
[JsonDerivedType(typeof(CodeAssertion), "code")]
public abstract class ScenarioAssertion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public abstract AssertionResult Evaluate(Game game, uint frame);

    protected static bool TryResolvePath(Game game, string path, out double resolvedValue, out string? error)
    {
        resolvedValue = 0;
        error = null;

        if (path.StartsWith("player[", StringComparison.OrdinalIgnoreCase))
        {
            var close = path.IndexOf(']');
            if (close <= "player[".Length)
            {
                error = $"Invalid player path '{path}'.";
                return false;
            }

            if (!int.TryParse(path["player[".Length..close], NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerIndex))
            {
                error = $"Invalid player index in path '{path}'.";
                return false;
            }

            if (playerIndex < 0 || playerIndex >= game.PlayerManager.Players.Count)
            {
                error = $"Player index {playerIndex} out of range.";
                return false;
            }

            var player = game.PlayerManager.Players[playerIndex];
            var suffix = path[(close + 1)..];
            if (suffix.StartsWith('.'))
            {
                suffix = suffix[1..];
            }

            switch (suffix)
            {
                case "money":
                    resolvedValue = player.BankAccount.Money;
                    return true;
                case "unitCount":
                    resolvedValue = player.UnitCount;
                    return true;
                case "buildingCount":
                    resolvedValue = player.BuildingCount;
                    return true;
                case "attackOrderCount":
                    resolvedValue = ReplayActionMetrics.GetAttackOrderCount(player.Id);
                    return true;
                default:
                    error = $"Unsupported player path suffix '{suffix}'.";
                    return false;
            }
        }

        switch (path)
        {
            case "outcome.status":
                resolvedValue = 0; // OutcomeStatus.InProgress until P3 lands.
                return true;
            case "outcome.reason":
                resolvedValue = 0; // OutcomeReason default until P3 lands.
                return true;
            default:
                error = $"Unsupported assertion path '{path}'.";
                return false;
        }
    }

    protected static bool EvaluateOp(double actual, string op, double expected)
    {
        return op switch
        {
            "==" => actual == expected,
            "!=" => actual != expected,
            ">=" => actual >= expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            "<" => actual < expected,
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'."),
        };
    }
}

public sealed class StateAssertion : ScenarioAssertion
{
    public string Path { get; set; } = string.Empty;
    public string Op { get; set; } = "==";
    public double Value { get; set; }

    public override AssertionResult Evaluate(Game game, uint frame)
    {
        if (!TryResolvePath(game, Path, out var actual, out var error))
        {
            return new AssertionResult
            {
                AssertionId = Id,
                Passed = false,
                ActualValue = null,
                FailFrame = frame,
                Message = error,
            };
        }

        var passed = EvaluateOp(actual, Op, Value);
        return new AssertionResult
        {
            AssertionId = Id,
            Passed = passed,
            ActualValue = actual.ToString(CultureInfo.InvariantCulture),
            FailFrame = passed ? null : frame,
            Message = passed
                ? null
                : $"Assertion failed: {Path} {Op} {Value.ToString(CultureInfo.InvariantCulture)}",
        };
    }
}

public sealed class InvariantAssertion : ScenarioAssertion
{
    public string Path { get; set; } = string.Empty;
    public string Op { get; set; } = "==";
    public double Value { get; set; }

    public override AssertionResult Evaluate(Game game, uint frame)
    {
        if (!TryResolvePath(game, Path, out var actual, out var error))
        {
            return new AssertionResult
            {
                AssertionId = Id,
                Passed = false,
                ActualValue = null,
                FailFrame = frame,
                Message = error,
            };
        }

        var passed = EvaluateOp(actual, Op, Value);
        return new AssertionResult
        {
            AssertionId = Id,
            Passed = passed,
            ActualValue = actual.ToString(CultureInfo.InvariantCulture),
            FailFrame = passed ? null : frame,
            Message = passed
                ? null
                : $"Invariant failed: {Path} {Op} {Value.ToString(CultureInfo.InvariantCulture)}",
        };
    }
}

public sealed class OutcomeAssertion : ScenarioAssertion
{
    public string ExpectedStatus { get; set; } = string.Empty;
    public string? ExpectedReason { get; set; }
    public int? ExpectedWinningTeam { get; set; }

    public override AssertionResult Evaluate(Game game, uint frame)
    {
        const string actualStatus = "InProgress";
        var passed = string.Equals(ExpectedStatus, actualStatus, StringComparison.OrdinalIgnoreCase);
        return new AssertionResult
        {
            AssertionId = Id,
            Passed = passed,
            ActualValue = actualStatus,
            FailFrame = passed ? null : frame,
            Message = passed
                ? null
                : $"Outcome assertions are not implemented yet (expected '{ExpectedStatus}', actual '{actualStatus}').",
        };
    }
}

public sealed class CodeAssertion : ScenarioAssertion
{
    public string PredicateName { get; set; } = string.Empty;

    public override AssertionResult Evaluate(Game game, uint frame)
    {
        if (!CodeAssertionRegistry.TryGet(PredicateName, out var predicate) || predicate is null)
        {
            return new AssertionResult
            {
                AssertionId = Id,
                Passed = false,
                FailFrame = frame,
                Message = $"No predicate registered with name '{PredicateName}'.",
            };
        }

        var passed = predicate(game, frame);
        return new AssertionResult
        {
            AssertionId = Id,
            Passed = passed,
            FailFrame = passed ? null : frame,
            Message = passed ? null : $"Code predicate '{PredicateName}' returned false.",
        };
    }
}
