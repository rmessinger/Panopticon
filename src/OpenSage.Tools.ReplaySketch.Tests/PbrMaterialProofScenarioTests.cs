using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenSage.Tools.TestHarness;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// GPU-free tests for the PbrMaterialProof scenario surface: built-in-scenario dispatch accepts
/// the new name, and the scenario's capture matrix and validation guide stay in sync. Full
/// captures run through the TestHarness with CNC_GENERALS_PATH.
/// </summary>
public class PbrMaterialProofScenarioTests
{
    [Theory]
    [InlineData("PbrMaterialProof")]
    [InlineData("pbrmaterialproof")]
    public void BuiltInScenario_ParsesPbrMaterialProof(string value)
    {
        Assert.True(Enum.TryParse<BuiltInScenario>(value, ignoreCase: true, out var parsed));
        Assert.Equal(BuiltInScenario.PbrMaterialProof, parsed);
    }

    [Fact]
    public void CaptureMatrix_And_ValidationGuide_Agree()
    {
        var scenarioSource = ReadScenarioSource();

        // Every Capture("name", ...) call in the scenario has a matching guide row.
        var captureCalls = ExtractCaptureNames(scenarioSource);
        Assert.NotEmpty(captureCalls);
        Assert.Contains("colorspace-emissive", captureCalls);
        Assert.Contains("skinned", captureCalls);
        Assert.Contains("normal-generated-tangents", captureCalls);

        var guideStart = scenarioSource.IndexOf("const string guide = \"\"\"", StringComparison.Ordinal);
        var guideEnd = scenarioSource.IndexOf("\"\"\";", guideStart, StringComparison.Ordinal);
        var guide = scenarioSource[guideStart..guideEnd];

        foreach (var name in captureCalls)
        {
            Assert.Contains($"| pbr-{name} |", guide);
        }
    }

    [Fact]
    public void Scenario_RequiresSyntheticInputs_Only()
    {
        var source = ReadScenarioSource();
        // No installed-asset loading: the proof builds every texture in memory.
        Assert.DoesNotContain("GetByName", source, StringComparison.Ordinal);
        Assert.Contains("CreateEmbeddedBytes", source, StringComparison.Ordinal);
    }

    private static string ReadScenarioSource()
    {
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var candidates = new[]
        {
            Path.Combine(assemblyDirectory, "PbrMaterialProofScenarioSource.txt"),
        };
        _ = candidates;

        // The scenario type lives in the TestHarness assembly; read its source from the
        // repository layout relative to the test assembly.
        var repositoryRoot = FindRepositoryRoot(assemblyDirectory);
        var sourcePath = Path.Combine(repositoryRoot, "src", "OpenSage.Tools.TestHarness", "PbrMaterialProofScenario.cs");
        Assert.True(File.Exists(sourcePath), $"Scenario source not found at {sourcePath}");
        return File.ReadAllText(sourcePath);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static System.Collections.Generic.IEnumerable<string> ExtractCaptureNames(string source)
    {
        var marker = "Capture(\"";
        var index = 0;
        while (true)
        {
            index = source.IndexOf(marker, index, StringComparison.Ordinal);
            if (index < 0)
            {
                yield break;
            }

            var start = index + marker.Length;
            var end = source.IndexOf('"', start);
            yield return source[start..end];
            index = end;
        }
    }
}
