using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-P0-12: Integration tests for runtime handle resolution in <see cref="HarnessRunner"/>.
/// Verifies that scenarios with <see cref="HandleCommandArgument"/> do not throw
/// "must be resolved before dispatch" when <see cref="HarnessRunner.RunAndAssert"/> executes.
/// </summary>
public class HandleResolutionInteractiveTests
{
    /// <summary>
    /// Two-command scenario: command A creates a building via BuildObject; command B issues a
    /// ForceAttackGround order referencing command A's produced object via a handle arg.
    /// The scenario must complete without InvalidOperationException.
    /// </summary>
    [Fact]
    [Trait("Category", "Interactive")]
    public void RunAndAssert_WithHandleCommandArgument_DoesNotThrow()
    {
        // Command A: BuildObject at frame 200 — the game engine will create an object.
        var cmdA = new ActionEntry(
            label: "BuildBarracks",
            command: CommandSpec.CreateOpen(
                OrderType.BuildObject,
                new PositionCommandArgument(
                    new NormalizedPosition(0.3f, 0.4f))),
            position: null,
            timing: new FixedTiming(200));

        // Command B: ForceAttackGround at frame 400 using the handle from command A.
        // This arg would throw at export time if handle resolution is not in place.
        var cmdB = new ActionEntry(
            label: "AttackBuilding",
            command: CommandSpec.CreateOpen(
                OrderType.ForceAttackGround,
                new HandleCommandArgument(cmdA.Id)),
            position: null,
            timing: new FixedTiming(200));

        var scenario = new ReplayScenario
        {
            MapPath = "maps/alpine assault/alpine assault.map",
            Players =
            {
                new PlayerSlotConfig
                {
                    Name = "USA",
                    FactionIndex = 2,
                    Color = 0,
                    StartPosition = 1,
                    Team = 1,
                    Actions = { cmdA, cmdB },
                },
                new PlayerSlotConfig
                {
                    Name = "GLA",
                    FactionIndex = 4,
                    Color = 1,
                    StartPosition = 2,
                    Team = 2,
                },
            },
        };

        RunInteractiveScenario(scenario);
    }

    private static void RunInteractiveScenario(ReplayScenario scenario)
    {
        var installationService = new GeneralsInstallationService();
        if (!installationService.TryGetInstallation(out var installation) || installation is null)
        {
            throw new InvalidOperationException("Generals installation not found for Interactive test.");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rs-handle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            try
            {
                // The key assertion: RunAndAssert must complete without throwing
                // InvalidOperationException("must be resolved before dispatch").
                var report = HarnessRunner.RunAndAssert(scenario, installation, tempDirectory);
                // We accept any report (pass or fail) — the goal is no unresolved-arg exception.
                _ = report;
            }
            catch (TypeInitializationException ex) when (ex.InnerException is FileNotFoundException)
            {
                // SDL2 native library not present on this host — skip gracefully.
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("must be resolved before dispatch"))
            {
                throw new Xunit.Sdk.XunitException(
                    "HarnessRunner threw an unresolved-arg exception. " +
                    "Handle resolution in the run loop is not working correctly: " + ex.Message);
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
