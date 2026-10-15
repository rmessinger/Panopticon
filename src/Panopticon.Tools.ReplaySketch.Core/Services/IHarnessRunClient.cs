using System;
using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public interface IHarnessRunClient
{
    Task<HarnessRunResult> RunAsync(
        HarnessLaunchRequest request,
        HarnessCatalog catalog,
        IProgress<string>? output,
        CancellationToken cancellationToken);
}
