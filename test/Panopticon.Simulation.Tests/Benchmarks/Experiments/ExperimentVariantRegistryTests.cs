using System;
using System.Linq;
using Panopticon.Benchmarks.Simulation.Experiments;
using Panopticon.Logic.AI;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A4-38 variant registry: registration and resolution identity, duplicate and unknown
/// failures, definition validation before any run, and settings construction through the
/// registered descriptors only.
/// </summary>
public class ExperimentVariantRegistryTests
{
    [Fact]
    public void CreateDefault_RegistersAiPolicySeam()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var descriptors = registry.Descriptors.Where(d => d.Seam == "ai-policy").ToList();
        Assert.Equal(2, descriptors.Count);
        Assert.Contains(descriptors, d => d.Id == "rule-based");
        Assert.Contains(descriptors, d => d.Id == "neural");
    }

    [Fact]
    public void Resolve_ReturnsStableIdentityAndVersion()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var descriptor = registry.Resolve("ai-policy", "rule-based");

        Assert.Equal("ai-policy", descriptor.Seam);
        Assert.Equal("rule-based", descriptor.Id);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Version));
    }

    [Fact]
    public void Register_RejectsDuplicates()
    {
        var registry = new ExperimentImplementationRegistry();

        registry.Register(AiPolicyExperimentImplementations.RuleBased());
        Assert.Throws<ArgumentException>(() => registry.Register(AiPolicyExperimentImplementations.RuleBased()));
    }

    [Fact]
    public void Register_RejectsUnsafeIdentifiers()
    {
        Assert.Throws<ArgumentException>(
            () => new ImplementationDescriptor("ai-policy", "Neural", "display", "1", () => new object()));
        Assert.Throws<ArgumentException>(
            () => new ImplementationDescriptor("ai policy", "neural", "display", "1", () => new object()));
        Assert.Throws<ArgumentException>(
            () => new ImplementationDescriptor("ai-policy", "../escape", "display", "1", () => new object()));
    }

    [Fact]
    public void Resolve_UnknownFailsWithSeamContext()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var error = Assert.Throws<ArgumentException>(() => registry.Resolve("ai-policy", "hierarchical"));
        Assert.Contains("ai-policy", error.Message);
        Assert.Contains("rule-based, neural", error.Message);
    }

    [Fact]
    public void TryResolve_ReportsUnknownWithoutThrowing()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        Assert.True(registry.TryResolve("ai-policy", "neural", out var found));
        Assert.Equal("neural", found!.Id);
        Assert.False(registry.TryResolve("ai-policy", "hierarchical", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void ValidateDefinition_RejectsUnknownSeamImplementationOrDuplicate()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();
        var valid = new ExperimentDefinition("n", "s", 1, 1, 10, [new ExperimentAxis("ai-policy", ["rule-based"])]);

        registry.ValidateDefinition(valid);

        Assert.Throws<ArgumentException>(() => registry.ValidateDefinition(
            new ExperimentDefinition("n", "s", 1, 1, 10, [new ExperimentAxis("ai-policy", ["hierarchical"])])));
        Assert.Throws<ArgumentException>(() => registry.ValidateDefinition(
            new ExperimentDefinition("n", "s", 1, 1, 10, [new ExperimentAxis("entity-storage", ["module-objects"])])));
        Assert.Throws<ArgumentException>(() => registry.ValidateDefinition(
            new ExperimentDefinition("n", "s", 1, 1, 10,
            [
                new ExperimentAxis("ai-policy", ["rule-based"]),
                new ExperimentAxis("ai-policy", ["neural"]),
            ])));
        Assert.Throws<ArgumentException>(() => registry.ValidateDefinition(
            new ExperimentDefinition("n", "s", 1, 1, 10, [new ExperimentAxis("ai-policy", ["neural", "neural"])])));
    }

    [Fact]
    public void Settings_AreConstructedThroughTheRegistry()
    {
        var registry = ExperimentImplementationRegistry.CreateDefault();

        var ruleBased = (AIDecisionPolicyPayload)registry.Resolve("ai-policy", "rule-based").CreateSettings();
        var neural = (AIDecisionPolicyPayload)registry.Resolve("ai-policy", "neural").CreateSettings();

        Assert.Equal(AIDecisionPolicyType.RuleBased, ruleBased.Type);
        Assert.Null(ruleBased.NeuralNetworkWeights);
        Assert.Equal(AIDecisionPolicyType.NeuralNetwork, neural.Type);
        Assert.Equal(NeuralNetworkAIDecisionPolicy.WeightCount, neural.NeuralNetworkWeights!.Length);
        AIDecisionPolicyFactory.Create(neural);
    }
}
