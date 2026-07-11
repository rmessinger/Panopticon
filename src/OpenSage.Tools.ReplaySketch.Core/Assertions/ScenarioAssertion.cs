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

    public abstract AssertionResult Evaluate(IGame game, uint frame);

    protected static bool TryResolvePath(IGame game, string path, out double resolvedValue, out string? error)
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

        var outcome = game.CurrentOutcome;

        switch (path)
        {
            case "outcome.status":
                resolvedValue = (double)outcome.Status;
                return true;
            case "outcome.reason":
                resolvedValue = (double)outcome.Reason;
                return true;
            case "outcome.winningPlayer":
                resolvedValue = outcome.WinningPlayer != null
                    ? game.PlayerManager.GetPlayerIndex(outcome.WinningPlayer)
                    : -1;
                return true;
            case "outcome.winningTeam":
                resolvedValue = outcome.WinningTeam != null
                    ? outcome.WinningTeam.Id
                    : -1;
                return true;
            case "outcome.losingPlayer":
                resolvedValue = outcome.LosingPlayer != null
                    ? game.PlayerManager.GetPlayerIndex(outcome.LosingPlayer)
                    : -1;
                return true;
            case "outcome.decidedFrame":
                resolvedValue = outcome.DecidedOnFrame ?? -1;
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

    public override AssertionResult Evaluate(IGame game, uint frame)
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

    public override AssertionResult Evaluate(IGame game, uint frame)
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
    public int? ExpectedWinningPlayerIndex { get; set; }
    public int? ExpectedLosingPlayerIndex { get; set; }
    public int? ExpectedDecidedFrame { get; set; }

    public override AssertionResult Evaluate(IGame game, uint frame)
    {
        var outcome = game.CurrentOutcome;
        var actualStatus = outcome.Status.ToString();
        var passed = string.Equals(ExpectedStatus, actualStatus, StringComparison.OrdinalIgnoreCase);
        var actualWinningPlayerIndex = outcome.WinningPlayer != null
            ? game.PlayerManager.GetPlayerIndex(outcome.WinningPlayer)
            : -1;
        var actualWinningTeamId = (int?)outcome.WinningTeam?.Id ?? -1;
        var actualLosingPlayerIndex = outcome.LosingPlayer != null
            ? game.PlayerManager.GetPlayerIndex(outcome.LosingPlayer)
            : -1;
        var actualDecidedFrame = outcome.DecidedOnFrame ?? -1;

        if (passed && !string.IsNullOrWhiteSpace(ExpectedReason))
        {
            passed = string.Equals(ExpectedReason, outcome.Reason.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        if (ExpectedWinningPlayerIndex.HasValue && actualWinningPlayerIndex != ExpectedWinningPlayerIndex.Value)
        {
            passed = false;
        }

        if (ExpectedWinningTeam.HasValue && actualWinningTeamId != ExpectedWinningTeam.Value)
        {
            passed = false;
        }

        if (ExpectedLosingPlayerIndex.HasValue && actualLosingPlayerIndex != ExpectedLosingPlayerIndex.Value)
        {
            passed = false;
        }

        if (ExpectedDecidedFrame.HasValue && actualDecidedFrame != ExpectedDecidedFrame.Value)
        {
            passed = false;
        }

        return new AssertionResult
        {
            AssertionId = Id,
            Passed = passed,
            ActualValue = actualStatus,
            FailFrame = passed ? null : frame,
            Message = passed
                ? null
                : $"Outcome assertion failed: expected status '{ExpectedStatus}'"
                  + (string.IsNullOrWhiteSpace(ExpectedReason) ? string.Empty : $" reason '{ExpectedReason}'")
                  + (ExpectedWinningPlayerIndex.HasValue ? $" winning player '{ExpectedWinningPlayerIndex.Value}'" : string.Empty)
                  + (ExpectedWinningTeam.HasValue ? $" winning team '{ExpectedWinningTeam.Value}'" : string.Empty)
                  + (ExpectedLosingPlayerIndex.HasValue ? $" losing player '{ExpectedLosingPlayerIndex.Value}'" : string.Empty)
                  + (ExpectedDecidedFrame.HasValue ? $" decided frame '{ExpectedDecidedFrame.Value}'" : string.Empty)
                  + $"; actual status '{actualStatus}' reason '{outcome.Reason}' winning player '{actualWinningPlayerIndex}' winning team '{actualWinningTeamId}' losing player '{actualLosingPlayerIndex}' decided frame '{actualDecidedFrame}'.",
        };
    }
}

public sealed class CodeAssertion : ScenarioAssertion
{
    public string PredicateName { get; set; } = string.Empty;

    public override AssertionResult Evaluate(IGame game, uint frame)
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
