using System.Threading;
using System.Threading.Tasks;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.Services;

public interface IHarnessCatalogClient
{
    Task<HarnessCatalogResult> LoadAsync(CancellationToken cancellationToken);
}
