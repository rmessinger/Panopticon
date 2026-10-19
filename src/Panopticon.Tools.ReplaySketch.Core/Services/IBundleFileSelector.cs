#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Selects a run-bundle path (a directory containing run.json, or the manifest itself) for the
/// results view. Core never invokes a picker directly; the front-end adapter does
/// (WI-A5-04, follows <see cref="IExternalModelFileSelector"/>).
/// </summary>
public interface IBundleFileSelector
{
    Task<string?> SelectBundleAsync(CancellationToken cancellationToken);
}
