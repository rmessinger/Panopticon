using System.Text.Json;
using OpenSage.Tools.ReplaySketch.Assertions;
using OpenSage.Tools.ReplaySketch.Model;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class CodeAssertionTests
{
    [Fact]
    public void CodeAssertion_RegistryMiss_ReportsFail()
    {
        var assertion = new CodeAssertion
        {
            Id = "code-miss",
            PredicateName = $"missing-{Guid.NewGuid():N}",
        };

        var result = assertion.Evaluate(default!, frame: 12);

        Assert.False(result.Passed);
        Assert.Equal((uint)12, result.FailFrame);
        Assert.Contains("No predicate registered", result.Message);
    }

    [Fact]
    public void CodeAssertion_RegistryHit_ReportsPredicateResult()
    {
        var name = $"always-true-{Guid.NewGuid():N}";
        CodeAssertionRegistry.Register(name, (_, _) => true);

        var assertion = new CodeAssertion
        {
            Id = "code-hit",
            PredicateName = name,
        };

        var result = assertion.Evaluate(default!, frame: 1);

        Assert.True(result.Passed);
        Assert.Null(result.FailFrame);
    }

    [Fact]
    public void CodeAssertion_Json_RoundTrips()
    {
        ScenarioAssertion original = new CodeAssertion
        {
            Id = "code-json",
            PredicateName = "round-trip",
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<ScenarioAssertion>(json);
        var code = Assert.IsType<CodeAssertion>(roundTripped);

        Assert.Equal("code-json", code.Id);
        Assert.Equal("round-trip", code.PredicateName);
    }
}
