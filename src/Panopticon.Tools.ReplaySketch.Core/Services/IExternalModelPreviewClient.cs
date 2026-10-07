using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>Outcome of one preview-client execution.</summary>
public sealed class ExternalModelPreviewResult
{
    public bool Succeeded { get; }

    public IReadOnlyList<Panopticon.Tools.ReplaySketch.Model.ExternalModelDiagnostic> Diagnostics { get; }

    public ExternalModelPreviewResult(
        bool succeeded,
        IReadOnlyList<Panopticon.Tools.ReplaySketch.Model.ExternalModelDiagnostic> diagnostics)
    {
        Succeeded = succeeded;
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// Awaitable, cancellable execution of one external-model preview request through a
/// TestHarness process. One request uses one process; cancellation terminates exactly that
/// owned process and awaits its exit. Not the fire-and-forget <see cref="IProcessLauncher"/>.
/// </summary>
public interface IExternalModelPreviewClient
{
    Task<ExternalModelPreviewResult> ExecuteAsync(
        Panopticon.Tools.ReplaySketch.Model.ExternalModelRequest request,
        IExternalModelArtifactLease lease,
        CancellationToken cancellationToken);
}
