using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public sealed class HarnessRunClient : IHarnessRunClient
{
    private readonly Func<string?> _locate; private readonly IHarnessProcessFactory _processFactory;
    internal HarnessRunClient(Func<string?>? locate, IHarnessProcessFactory? processFactory) { var locator = new SiblingBuildExecutableLocator(); _locate = locate ?? (() => locator.Locate("Panopticon.Tools.TestHarness")); _processFactory = processFactory ?? new SystemHarnessProcessFactory(); }

    public HarnessRunClient() : this(null, null) { }

    public async Task<HarnessRunResult> RunAsync(HarnessLaunchRequest request, HarnessCatalog catalog, IProgress<string>? output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(catalog);
        var executable = _locate() ?? throw new FileNotFoundException("The TestHarness executable was not found in a sibling build output.");
        var path = Path.Combine(Path.GetTempPath(), $"panopticon-harness-{Guid.NewGuid():N}.json");
        IHarnessProcess? process = null;
        try
        {
            await File.WriteAllTextAsync(path, request.ToConfigJson(catalog), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            process = _processFactory.Start(executable, $"--config {Quote(path)}");
            using var registration = cancellationToken.Register(process.KillTree);
            var streaming = process.StreamOutputAsync(output, CancellationToken.None);
            try
            {
                var exit = await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await streaming.ConfigureAwait(false);
                var scenario = catalog.Scenarios.SingleOrDefault(s => s.Id == request.ScenarioId);
                var meaning = scenario is not null && scenario.ExitCodes.TryGetValue(exit, out var result) ? result : "undocumented exit code";
                return new(exit, meaning, false, request.KeepConfig ? path : null);
            }
            catch (OperationCanceledException)
            {
                process.KillTree();
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await streaming.ConfigureAwait(false);
                return new(-1, "cancelled", true, request.KeepConfig ? path : null);
            }
        }
        finally
        {
            process?.Dispose(); if (!request.KeepConfig) try { File.Delete(path); }
                catch (IOException) { }
        }
    }
    private static string Quote(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
