#nullable enable

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenSage.Tests.Content.Importing;

public class GltfFixtureGeneratorTests
{
    private static string FixtureRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null && current.Name != "bin")
            {
                current = current.Parent;
            }

            current = current?.Parent ?? throw new InvalidOperationException("Could not locate test output root.");

            return Path.Combine(current.FullName, "Content", "Importing", "Gltf", "Fixtures");
        }
    }

    [Fact]
    public void Generation_IsByteStableAcrossRuns()
    {
        var first = Path.Combine(Path.GetTempPath(), "gltf-fixtures-a-" + Guid.NewGuid().ToString("N"));
        var second = Path.Combine(Path.GetTempPath(), "gltf-fixtures-b-" + Guid.NewGuid().ToString("N"));

        try
        {
            GenerateAll(first);
            GenerateAll(second);

            foreach (var file in Directory.GetFiles(first).Select(Path.GetFileName).Order())
            {
                Assert.True(File.Exists(Path.Combine(second, file)), $"Missing in second run: {file}");

                var firstBytes = File.ReadAllBytes(Path.Combine(first, file));
                var secondBytes = File.ReadAllBytes(Path.Combine(second, file));
                Assert.Equal(firstBytes, secondBytes);
            }
        }
        finally
        {
            TryDelete(first);
            TryDelete(second);
        }
    }

    [Fact]
    public void CommittedFixtures_MatchGeneratorOutput()
    {
        var generated = Path.Combine(Path.GetTempPath(), "gltf-fixtures-committed-" + Guid.NewGuid().ToString("N"));

        try
        {
            // Opt-in fixture regeneration: setting OPENAGE_GLTF_FIXTURE_OUTPUT to the committed
            // fixture directory rewrites every fixture in place (run when fixtures change).
            var regenerationRoot = Environment.GetEnvironmentVariable("OPENAGE_GLTF_FIXTURE_OUTPUT");
            if (!string.IsNullOrEmpty(regenerationRoot))
            {
                GenerateAll(regenerationRoot);
            }

            GenerateAll(generated);

            var root = FixtureRoot;
            foreach (var file in Directory.GetFiles(generated).Select(Path.GetFileName).Order())
            {
                var committedPath = Path.Combine(root, file);
                Assert.True(File.Exists(committedPath), $"Committed fixture missing: {file}");
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(generated, file)),
                    File.ReadAllBytes(committedPath));
            }
        }
        finally
        {
            TryDelete(generated);
        }
    }

    private static void GenerateAll(string directory)
    {
        GltfFixtureGenerator.GenerateAll(directory);
        foreach (var (name, content) in GltfHandAuthoredFixtures.TextFixtures)
        {
            File.WriteAllText(Path.Combine(directory, name), content);
        }

        foreach (var (name, content) in GltfHandAuthoredFixtures.BinaryFixtures)
        {
            File.WriteAllBytes(Path.Combine(directory, name), content);
        }
    }

    private static void TryDelete(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
