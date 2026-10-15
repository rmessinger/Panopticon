using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public sealed class HarnessCatalogClient : IHarnessCatalogClient
{
    private readonly TimeSpan _timeout;
    private readonly Func<string?> _locate; private readonly IHarnessProcessFactory _processFactory;
    internal HarnessCatalogClient(Func<string?>? locate, IHarnessProcessFactory? processFactory, TimeSpan? timeout = null) { var locator = new SiblingBuildExecutableLocator(); _locate = locate ?? (() => locator.Locate("Panopticon.Tools.TestHarness")); _processFactory = processFactory ?? new SystemHarnessProcessFactory(); _timeout = timeout ?? TimeSpan.FromSeconds(30); }
    public HarnessCatalogClient() : this(null, null) { }

    public async Task<HarnessCatalogResult> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        var executable = _locate();
        if (executable is null) return new(null, "HRN100", "The TestHarness executable was not found in a sibling build output.");
        IHarnessProcess? process = null;
        try
        {
            try { process = _processFactory.Start(executable, "--list-scenarios"); }
            catch (Exception e) { return new(null, "HRN101", $"The TestHarness process could not be started: {e.Message}"); }
            using var registration = deadline.Token.Register(process.KillTree);
            try
            {
                var stdout = process.ReadStandardOutputAsync(CancellationToken.None); var stderr = process.ReadStandardErrorAsync(CancellationToken.None);
                var exit = await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); var error = await stderr.ConfigureAwait(false);
                var json = await stdout.ConfigureAwait(false);
                if (exit != 0) return new(null, "HRN102", $"TestHarness exited with code {exit}: {Tail(error)}");
                try { return new(HarnessCatalogJson.Deserialize(json), null, null); }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { return new(null, "HRN103", $"TestHarness returned an invalid catalog: {e.Message}"); }
            }
            catch (OperationCanceledException)
            {
                process.KillTree();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new(null, "HRN102", "TestHarness catalog request timed out.");
            }
        }
        finally { process?.Dispose(); }
    }

    private static string Tail(string value) => value.Length <= 2000 ? value : value[^2000..];
}
