#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Process adapter seam for the preview client: creation, bounded stderr capture, awaited
/// exit, and exact owned-instance termination. Production wraps
/// <see cref="System.Diagnostics.Process"/>; tests substitute fakes.
/// </summary>
internal interface IExternalModelProcess : IDisposable
{
    /// <summary>Bounded standard-error capture; never throws for process failure.</summary>
    Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken);

    /// <summary>Awaits process exit; exit code returned, cancellation throws.</summary>
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Terminates exactly this process instance, including its child tree where supported.
    /// Never name-based.
    /// </summary>
    void KillTree();
}

internal interface IExternalModelProcessFactory
{
    /// <summary>
    /// Starts the executable with the given argument string. Shell execution is disabled
    /// and standard error is redirected.
    /// </summary>
    IExternalModelProcess Start(string executablePath, string arguments);
}
