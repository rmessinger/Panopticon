using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Data;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Model.Capture;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.TestHarness;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-H2a-17: a real EE2 unit, published through the shared unit-content layer, fights a
/// Generals unit in the existing capture host. Requires CNC_GENERALS_PATH and EMPIRE_EARTH_II_PATH;
/// a missing install is an unmet prerequisite, reported as a failure rather than a pass.
/// </summary>
[Trait("Category", "Interactive")]
[Collection("SerialGameTests")]
public class UnitContentImportIntegrationTests
{
    private const string ProfileName = "capture-mixed-source-ee2.json";

    private static GameInstallation RequireInstalls()
    {
        Assert.False(
            string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(UnitContentImportRunner.Ee2PathVariable)),
            $"Unmet prerequisite: {UnitContentImportRunner.Ee2PathVariable} must point at an Empire Earth II install.");
        Assert.True(new GeneralsInstallationService().TryGetInstallation(out var installation) && installation is not null, "Unmet prerequisite: Generals installation not found.");
        return installation!;
    }

    private static CaptureRunResult Run(CaptureProfile profile)
    {
        var output = Path.Combine(Path.GetTempPath(), $"panopticon_h2a_{System.Guid.NewGuid():N}");
        profile.OutputDirectory = output;
        try
        {
            return ScenarioCaptureRunner.Run(RequireInstalls(), profile, new CaptureRunSettings(null, FullHud: false, FallbackOutputDirectory: null), new NopReporter());
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void Ee2MacemanMovesFightsAndDiesBesideGenerals()
    {
        var result = Run(CaptureProfileLoader.LoadEmbeddedCapture(ProfileName));

        Assert.Equal(0, result.ExitCode);
        Assert.All(result.ExpectationResults, e => Assert.True(e.Passed, $"{e.Expectation}: {e.Detail}"));

        var frames = result.Manifest!.Frames;
        var maceman = frames.SelectMany(f => f.Actors).First(a => a.Id == "maceman");
        Assert.Equal(75f, maceman.MaxHealth);
        Assert.Equal("HeavyInfantry1UpgradeEpoch1", maceman.Definition);

        // Authored EE2 damage reaches the Generals target unscaled: every hit removes exactly 5.
        var rebelHealth = frames.Select(f => f.Actors.FirstOrDefault(a => a.Id == "rebel")?.Health).OfType<float>().ToArray();
        var drops = rebelHealth.Zip(rebelHealth.Skip(1), (a, b) => a - b).Where(d => d > 0).ToArray();
        Assert.All(drops, d => Assert.Equal(5f, d, 3));

        // The maceman keeps swinging in melee contact (inside the rebel's footprint) for the whole
        // engagement, and stops once dead: ~450 frames at a 45-frame reload is well over five hits.
        Assert.True(drops.Length >= 5, $"Expected sustained melee hits, got {drops.Length}.");
        var deathIndex = Array.FindIndex(frames.ToArray(), f => f.Actors.All(a => a.Id != "maceman"));
        Assert.True(deathIndex > 0);
        var rebelAtDeath = frames[deathIndex].Actors.First(a => a.Id == "rebel").Health;
        Assert.Equal(rebelAtDeath, frames[^1].Actors.First(a => a.Id == "rebel").Health);
    }

    [Fact]
    public void UnsupportedMechanicBlocksPublicationAndSpawning()
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture(ProfileName);
        profile.ContentImports[0].ExcludedMechanics.Remove("ConvertAttack");

        var result = Run(profile);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, result.CapturedFrames);
    }

    [Fact]
    public void CollidingImportPublishesNothingAndLeavesGeneralsIntact()
    {
        var installation = RequireInstalls();
        using var game = new Game(installation, null, new Configuration { NoAudio = true }, null);
        var import = CaptureProfileLoader.LoadEmbeddedCapture(ProfileName).ContentImports[0];
        var ranger = game.AssetStore.ObjectDefinitions.GetByName("AmericaInfantryRanger");
        var builtInCount = game.AssetStore.ObjectDefinitions.Count;
        var ee2 = game.AssetStore.GetSourceContent(ContentSource.Ee2);

        Assert.True(UnitContentImportRunner.Import(game, import, new NopReporter()));
        var published = ee2.ObjectDefinitions.GetByName("HeavyInfantry1UpgradeEpoch1");
        var depth = game.AssetStore.ScopeDepth;

        Assert.False(UnitContentImportRunner.Import(game, import, new NopReporter()));

        Assert.Equal(depth, game.AssetStore.ScopeDepth);
        Assert.Same(published, ee2.ObjectDefinitions.GetByName("HeavyInfantry1UpgradeEpoch1"));
        Assert.Same(ranger, game.AssetStore.ObjectDefinitions.GetByName("AmericaInfantryRanger"));
        Assert.Equal(builtInCount, game.AssetStore.ObjectDefinitions.Count);
        Assert.Null(game.AssetStore.ObjectDefinitions.GetByName("HeavyInfantry1UpgradeEpoch1"));
    }

    [Fact]
    public void ProfileValidatorRejectsUnpublishedSourceReferences()
    {
        var profile = CaptureProfileLoader.LoadEmbeddedCapture(ProfileName);
        profile.ContentImports.Clear();

        Assert.Contains(CaptureProfileValidator.Validate(profile), error => error.Contains("no ContentImports entry publishes"));
    }

    private sealed class NopReporter : ITestHarnessReporter
    {
        public void Info(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void ActionDispatched(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void ActionCompleted(ActionType actionType, string? playerName, uint frame)
        {
        }

        public void MatchOutcomeDecided(Logic.OutcomeStatus status, Logic.OutcomeReason reason, string? winnerName, uint frame)
        {
        }
    }
}
