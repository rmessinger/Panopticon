using System.IO;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-16: install-backed smoke for <see cref="ScenarioMatrixRunner.RunToBundles"/>; every
/// cell lands in its keyed bundle directory with the aggregate manifest beside the cells.
/// </summary>
[Collection("SerialGameTests")]
public sealed class MatrixRunSetSmokeTests : MockedGameTest
{
    [GameFact(SageGame.CncGenerals)]
    public void RunToBundles_WritesManifestAndKeyedCellBundles()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var root = Path.Combine(Path.GetTempPath(), $"rs-runset-smoke-{System.Guid.NewGuid():N}");
        try
        {
            var matrix = new ScenarioMatrix
            {
                BaseScenario = new ReplayScenario
                {
                    MapPath = "maps/alpine assault/alpine assault.map",
                    Players = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA().Players,
                    StopConditions = [new FrameCapStopCondition(30)],
                },
                Axes = [new SweepAxis { Name = "seed", Values = ["707", "708"] }],
                Bindings = [new MatrixBinding { Target = MatrixBindingTarget.Seed, Axis = "seed" }],
            };

            var report = ScenarioMatrixRunner.RunToBundles(matrix, installation, Path.GetTempPath(), root);

            Assert.Equal(2, report.Cells.Count);
            Assert.True(report.AllPassed);
            Assert.True(File.Exists(Path.Combine(root, ScenarioMatrixRunner.ManifestFileName)));
            Assert.All(
                report.Cells,
                c => Assert.True(File.Exists(Path.Combine(root, c.RelativePath, RunBundle.ManifestFileName))));
            Assert.Contains("cells/seed=707", File.ReadAllText(Path.Combine(root, ScenarioMatrixRunner.ManifestFileName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
