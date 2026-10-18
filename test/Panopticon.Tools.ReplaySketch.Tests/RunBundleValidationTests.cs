using System;
using System.IO;
using Panopticon;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// WI-A5-09, engine-free: bundle load validation semantics over hand-built bundles. Missing
/// optional detail degrades explicitly; missing required files and unknown event schema
/// versions fail with errors that name the artifact.
/// </summary>
public sealed class RunBundleValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a5-bundle-valid-" + Guid.NewGuid().ToString("N"));

    private string BundleDir()
    {
        Directory.CreateDirectory(_root);
        return _root;
    }

    private void WriteStateArtifact(string fileName)
    {
        File.WriteAllText(Path.Combine(_root, fileName), "state");
    }

    private void WriteManifest(string stateFileName)
    {
        var manifest = new RunOutput
        {
            Seed = 1,
            ScenarioId = "hand-built",
            SnapshotArtifactPath = Path.Combine(_root, stateFileName),
        };
        manifest.SaveToFile(Path.Combine(_root, RunBundle.ManifestFileName));
    }

    [Fact]
    public void MissingStateArtifact_FailsNamingIt()
    {
        var bundle = BundleDir();
        WriteManifest("state-does-not-exist.bin");
        RunBundle.WriteEvents(Path.Combine(bundle, RunBundle.EventsFileName), []);

        var error = Assert.Throws<InvalidOperationException>(() => RunBundle.Load(bundle));
        Assert.Contains("state-does-not-exist.bin", error.Message);
    }

    [Fact]
    public void MissingEventsFile_FailsNamingIt()
    {
        var bundle = BundleDir();
        WriteStateArtifact(RunBundle.StateFileName);
        WriteManifest(RunBundle.StateFileName);

        var error = Assert.Throws<InvalidOperationException>(() => RunBundle.Load(bundle));
        Assert.Contains(RunBundle.EventsFileName, error.Message);
    }

    [Fact]
    public void MissingOptionalTriggeredDetail_DegradesExplicitly()
    {
        var bundle = BundleDir();
        WriteStateArtifact(RunBundle.StateFileName);
        WriteManifest(RunBundle.StateFileName);
        RunBundle.WriteEvents(Path.Combine(bundle, RunBundle.EventsFileName), []);

        var contents = RunBundle.Load(bundle);
        Assert.False(contents.HasTriggeredDetail);
        Assert.Empty(contents.Events);
    }

    [Fact]
    public void UnknownEventsSchemaVersion_FailsNamingFoundAndUnderstood()
    {
        var bundle = BundleDir();
        WriteStateArtifact(RunBundle.StateFileName);
        WriteManifest(RunBundle.StateFileName);
        File.WriteAllText(
            Path.Combine(bundle, RunBundle.EventsFileName),
            "{\"schemaVersion\": 99, \"events\": []}");

        var error = Assert.Throws<InvalidOperationException>(() => RunBundle.Load(bundle));
        Assert.Contains("99", error.Message);
        Assert.Contains("1", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
