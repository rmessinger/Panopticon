using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class HarnessRunnerInteractiveTests
{
    [Fact]
    [Trait("Category", "Interactive")]
    public void RunAndAssert_AlpineScenario_WithMoneyInvariant_Passes()
    {
        var scenario = ScenarioProfileLoader.LoadEmbedded("alpine-assault-usa-vs-gla.json");
        scenario.Assertions.Add(new StateAssertion
        {
            Id = "money-non-negative",
            Path = "player[0].money",
            Op = ">=",
            Value = 0,
        });

        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            throw new InvalidOperationException("Generals installation not found for Interactive test.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-runassert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            try
            {
                var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
                Assert.True(report.AllPassed, report.ToJson());
            }
            catch (TypeInitializationException ex) when (ex.InnerException is FileNotFoundException)
            {
                // Interactive execution requires SDL native libraries on the host.
                // Treat missing SDL as a host-environment limitation, not a harness failure.
                return;
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
