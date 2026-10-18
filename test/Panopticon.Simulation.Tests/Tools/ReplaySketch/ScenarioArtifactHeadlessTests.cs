using System;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Simulation;
using Panopticon.Tests.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-A5-03, installed-game: a scenario carrying the new declarative fields (seed, domain
/// id, frame cap, recording options) runs headless through the harness with those fields in
/// effect: the authored seed lands in the run output, the rts domain is attached, and the frame
/// cap bounds the run before the default timeout.
/// </summary>
[Collection("SerialGameTests")]
public class ScenarioArtifactHeadlessTests : MockedGameTest
{
    [GameFact(SageGame.CncGenerals)]
    public void Scenario_WithSeedDomainAndFrameCap_RunsHeadlessUnderTheCap()
    {
        var scenario = ReplayScenarioFactory.CreateAlpineAssaultUSAvGLA();
        scenario.DomainId = RtsSimulationDomainModel.DefaultDomainId;
        scenario.Seed = 7137u;
        scenario.StopConditions = [new FrameCapStopCondition(600)];
        scenario.Recording = new ScenarioRecordingOptions { SampleInterval = 15 };

        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"a5-03-headless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            RunOutput? output = null;
            var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory, o => output = o);

            Assert.NotNull(report);
            Assert.NotNull(output);
            Assert.Equal(7137u, output!.Seed);
            Assert.Equal(RtsSimulationDomainModel.DefaultDomainId, output.DomainId);
            Assert.True(
                output.TotalLogicFrames <= 601,
                $"frame-capped scenario ran {output.TotalLogicFrames} frames, expected <= 601");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
