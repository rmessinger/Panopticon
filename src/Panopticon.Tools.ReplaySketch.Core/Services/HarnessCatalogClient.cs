using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public sealed class HarnessCatalogClient
{
    private readonly Func<string?> _locate; private readonly IHarnessProcessFactory _processFactory;
    internal HarnessCatalogClient(Func<string?>? locate, IHarnessProcessFactory? processFactory) { var locator = new SiblingBuildExecutableLocator(); _locate = locate ?? (() => locator.Locate("Panopticon.Tools.TestHarness")); _processFactory = processFactory ?? new SystemHarnessProcessFactory(); }
    public HarnessCatalogClient() : this(null, null) { }
    public async Task<HarnessCatalogResult> LoadAsync(CancellationToken cancellationToken)
    {
        var executable = _locate();
        if (executable is null) return new(null, "HRN100", "The TestHarness executable was not found in a sibling build output.");
        IHarnessProcess? process = null;
        try
        {
            try { process = _processFactory.Start(executable, "--list-scenarios"); } catch (Exception e) { return new(null, "HRN101", $"The TestHarness process could not be started: {e.Message}"); }
            using var registration = cancellationToken.Register(process.KillTree);
            try
            {
                var stdout = process.ReadStandardOutputAsync(CancellationToken.None); var stderr = process.ReadStandardErrorAsync(CancellationToken.None);
                var exit = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); var error = await stderr.ConfigureAwait(false);
                if (exit != 0) return new(null, "HRN102", $"TestHarness exited with code {exit}: {Tail(error)}");
                try { return new(HarnessCatalogJson.Deserialize(await stdout.ConfigureAwait(false)), null, null); } catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return new(null, "HRN103", $"TestHarness returned an invalid catalog: {e.Message}"); }
            }
            catch (OperationCanceledException) { process.KillTree(); throw; }
        }
        finally { process?.Dispose(); }
    }
    private static string Tail(string value) => value.Length <= 2000 ? value : value[^2000..];
}
