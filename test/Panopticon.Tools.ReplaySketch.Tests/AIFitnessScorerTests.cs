using System;
using System.Collections.Generic;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A4-14: outcome ordering (win > sudden death > frame cap > loss), separately visible
/// shaped terms, and explicit rejection of incomplete or incompatible run outputs.
/// </summary>
public class AIFitnessScorerTests
{
    private const int FrameCap = 5400;

    private static RunOutput CreateOutput(
        OutcomeStatus status,
        string[] winners,
        int? decidedOnFrame = null,
        uint frames = FrameCap,
        uint? money = 5_000u,
        int? ownedObjects = 15)
    {
        var output = new RunOutput
        {
            TotalLogicFrames = frames,
            Outcome = new RunOutcomeRecord
            {
                Status = status,
                WinningPlayers = new List<string>(winners),
                DecidedOnFrame = decidedOnFrame,
            },
        };
        output.MetricSamples.Add(new RunMetricSample
        {
            Frame = frames,
            Players =
            {
                new RunPlayerMetrics { Player = "Genome", Money = money ?? 0, OwnedObjects = ownedObjects ?? 0 },
                new RunPlayerMetrics { Player = "RuleBased", Money = 0, OwnedObjects = 0 },
            },
        });
        return output;
    }

    [Fact]
    public void Win_ReceivesTopOutcome_PlusShapedTermsAndSpeedBonus()
    {
        var result = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["Genome"], decidedOnFrame: 1350), "Genome", FrameCap);

        Assert.Equal(100.0, result.OutcomeTerm);
        Assert.Equal("win", result.OutcomeLabel);
        Assert.Equal(5.0, result.EconomyTerm, 5);
        Assert.Equal(5.0, result.ArmyTerm, 5);
        // 20 * (1 - 1350/5400) = 15.
        Assert.Equal(15.0, result.SpeedTerm, 5);
        Assert.Equal(125.0, result.Total, 5);
    }

    [Fact]
    public void Loss_ReceivesZeroOutcome_AndNoSpeedBonus()
    {
        var result = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["RuleBased"], decidedOnFrame: 1350), "Genome", FrameCap);

        Assert.Equal(0.0, result.OutcomeTerm);
        Assert.Equal("loss", result.OutcomeLabel);
        Assert.Equal(0.0, result.SpeedTerm);
        Assert.Equal(10.0, result.Total, 5);
    }

    [Fact]
    public void SuddenDeath_OutranksFrameCap_ButNotWin()
    {
        var suddenDeath = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.SuddenDeath, []), "Genome", FrameCap);
        var frameCap = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.InProgress, []), "Genome", FrameCap);
        var win = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["Genome"], decidedOnFrame: 100), "Genome", FrameCap);

        Assert.Equal(50.0, suddenDeath.OutcomeTerm);
        Assert.Equal(40.0, frameCap.OutcomeTerm);
        Assert.True(suddenDeath.OutcomeTerm > frameCap.OutcomeTerm);
        Assert.True(win.OutcomeTerm > suddenDeath.OutcomeTerm);
    }

    [Fact]
    public void ShapedTerms_CapAtTheirMaximum()
    {
        var result = AIFitnessScorer.Score(
            CreateOutput(OutcomeStatus.InProgress, [], money: 999_999u, ownedObjects: 999),
            "Genome",
            FrameCap);

        Assert.Equal(10.0, result.EconomyTerm, 5);
        Assert.Equal(10.0, result.ArmyTerm, 5);
    }

    [Fact]
    public void EqualShapingInputs_ProduceEqualTerms()
    {
        var first = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.InProgress, []), "Genome", FrameCap);
        var second = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.InProgress, []), "Genome", FrameCap);

        Assert.Equal(first.Total, second.Total);
        Assert.Equal(first.EconomyTerm, second.EconomyTerm);
        Assert.Equal(first.ArmyTerm, second.ArmyTerm);
    }

    [Fact]
    public void LaterWin_IsFaster_BonusGrows()
    {
        var slow = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["Genome"], decidedOnFrame: 4050), "Genome", FrameCap);
        var fast = AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["Genome"], decidedOnFrame: 540), "Genome", FrameCap);

        Assert.True(fast.SpeedTerm > slow.SpeedTerm);
    }

    [Fact]
    public void MissingOutcomeRecord_Throws()
    {
        var output = CreateOutput(OutcomeStatus.Decided, ["Genome"]);
        output.Outcome = null;

        Assert.Throws<InvalidOperationException>(() => AIFitnessScorer.Score(output, "Genome", FrameCap));
    }

    [Fact]
    public void MissingMetricSamples_Throw()
    {
        var output = CreateOutput(OutcomeStatus.Decided, ["Genome"]);
        output.MetricSamples.Clear();

        Assert.Throws<InvalidOperationException>(() => AIFitnessScorer.Score(output, "Genome", FrameCap));
    }

    [Fact]
    public void GenomePlayerAbsentFromSamples_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => AIFitnessScorer.Score(CreateOutput(OutcomeStatus.Decided, ["Genome"]), "SomeoneElse", FrameCap));
    }

    [Fact]
    public void InProgressBeforeFrameCap_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => AIFitnessScorer.Score(CreateOutput(OutcomeStatus.InProgress, [], frames: 100), "Genome", FrameCap));
    }
}
