using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.TestHarness;

internal static class HarnessScenarioCatalog
{
    public static HarnessCatalog Build() => new(HarnessCatalogJson.CurrentSchemaVersion, Scenarios(), Parameters());

    // AddedOrder is the BuiltInScenario declaration index: scenarios are appended to the enum as
    // they are authored, so the highest value is the newest addition. The launcher menu defaults
    // to newest-first.
    public static IReadOnlyList<HarnessScenarioDescriptor> Scenarios() =>
        Enum.GetValues<BuiltInScenario>().Select((scenario, index) => Scenario(scenario, index)).ToArray();
    public static IReadOnlyList<HarnessParameterDescriptor> Parameters()
    {
        var defaults = JsonSerializer.SerializeToNode(new TestHarnessConfig(), Program.JsonOptions)!;
        return typeof(TestHarnessConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property =>
            {
                var attribute = property.GetCustomAttribute<HarnessParameterAttribute>()
                    ?? throw new InvalidOperationException($"TestHarnessConfig.{property.Name} is missing HarnessParameterAttribute.");
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var kind = type == typeof(bool) ? HarnessParameterKind.Bool
                    : type == typeof(int) ? HarnessParameterKind.Int
                    : type == typeof(float) ? HarnessParameterKind.Float
                    : type == typeof(string) ? (property.Name.EndsWith("Path", StringComparison.Ordinal) || property.Name.EndsWith("Root", StringComparison.Ordinal) ? HarnessParameterKind.Path : HarnessParameterKind.String)
                    : type.IsEnum ? HarnessParameterKind.Enum : HarnessParameterKind.Json;
                var enumValues = type.IsEnum ? Enum.GetNames(type) : [];
                return new HarnessParameterDescriptor(property.Name, attribute.Group, kind, enumValues,
                    defaults[property.Name]?.DeepClone(), attribute.Description, attribute.Scenarios);
            }).ToArray();
    }

    private static HarnessScenarioDescriptor Scenario(BuiltInScenario scenario, int index)
    {
        var id = scenario.ToString();
        var category = id.StartsWith("Alpine", StringComparison.Ordinal) ? HarnessScenarioCategory.Replay
            : id.StartsWith("MassBattle", StringComparison.Ordinal) || id == nameof(BuiltInScenario.RenderingScaleProbe) ? HarnessScenarioCategory.MassBattle
            : id == nameof(BuiltInScenario.UnitValidationGround) ? HarnessScenarioCategory.ValidationGround
            : id is nameof(BuiltInScenario.EngagementCapture) or nameof(BuiltInScenario.ScenarioCapture) ? HarnessScenarioCategory.Capture
            : id == nameof(BuiltInScenario.PhysicsStack) ? HarnessScenarioCategory.Physics : HarnessScenarioCategory.Proof;
        var headlessOnly = scenario is BuiltInScenario.EngagementCapture or BuiltInScenario.ScenarioCapture or BuiltInScenario.VisualRegression or BuiltInScenario.PhysicsStack
            or BuiltInScenario.PbrMaterialProof or BuiltInScenario.GltfRuntimeProof or BuiltInScenario.Ee2NifProof
            or BuiltInScenario.Ee2PropertyProof or BuiltInScenario.Ee2ScaleProof or BuiltInScenario.Ee2MorphProof
            or BuiltInScenario.Ee2ParticleProof or BuiltInScenario.Ee2LegacyKfProof;
        var profiles = scenario == BuiltInScenario.ScenarioCapture ? CaptureProfileLoader.EmbeddedCaptureProfileNames().ToArray() : [];
        var input = scenario == BuiltInScenario.ScenarioCapture ? HarnessProfileInput.FileOrEmbedded
            : scenario is BuiltInScenario.AlpineAssaultUSAvGLA or BuiltInScenario.AlpineAssaultUSAvGLAFullBuild ? HarnessProfileInput.File : HarnessProfileInput.None;
        var codes = new Dictionary<int, string> { [0] = "success", [1] = "setup or input error" };
        if (category == HarnessScenarioCategory.Capture) { codes[1] = "profile or setup error"; codes[4] = "camera or weapon setup error"; codes[5] = "expectation not met"; }
        if (scenario == BuiltInScenario.UnitValidationGround) { codes[2] = "baseline regression or timeout"; codes[3] = "timeout"; }
        if (scenario == BuiltInScenario.PhysicsStack) codes[5] = "unmet settle or scatter bound";
        if (scenario is BuiltInScenario.MassBattleDeployment or BuiltInScenario.MassBattleCapacityProbe) codes[5] = "unmet acceptance or engagement check";
        if (scenario == BuiltInScenario.VisualRegression) { codes[2] = "visual regression or chrome audit failure"; codes[5] = "silhouette failure"; codes[6] = "host mismatch"; }
        var summary = scenario switch
        {
            BuiltInScenario.AlpineAssaultUSAvGLA => "Replay a USA versus GLA assault on Alpine Assault.",
            BuiltInScenario.AlpineAssaultUSAvGLAFullBuild => "Replay a full USA base build and assault against GLA.",
            BuiltInScenario.UnitValidationGround => "Measure unit actions against validation baselines.",
            BuiltInScenario.MassBattleDeployment => "Deploy armies and verify map acceptance probes.",
            BuiltInScenario.MassBattleCapacityProbe => "Measure a large-army engagement and capacity limits.",
            BuiltInScenario.EngagementCapture => "Capture a configured attacker and target engagement.",
            BuiltInScenario.ScenarioCapture => "Capture a file or embedded scene profile.",
            BuiltInScenario.VisualRegression => "Compare scenario captures with visual baselines.",
            BuiltInScenario.PhysicsStack => "Verify physics settling and scatter bounds.",
            BuiltInScenario.RenderingScaleProbe => "Measure rendering cost for a repeated-model field.",
            _ => $"Capture and validate {id.Replace("Proof", string.Empty, StringComparison.Ordinal)} rendering features.",
        };
        return new HarnessScenarioDescriptor(id, category, summary, headlessOnly ? HarnessRunModes.Headless : HarnessRunModes.Headless | HarnessRunModes.Windowed, input, profiles, codes, index);
    }
}
