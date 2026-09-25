#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Injectable file selection for the Model tab. Returns the selected absolute file path,
/// or null when the user cancelled. Implementations guard platform pickers; Core never
/// invokes a picker directly.
/// </summary>
public interface IExternalModelFileSelector
{
    /// <summary>Selection restricted to external model documents (.gltf, .glb).</summary>
    Task<string?> SelectModelFileAsync(CancellationToken cancellationToken);

    /// <summary>Selection restricted to external animation documents (.gltf, .glb).</summary>
    Task<string?> SelectAnimationFileAsync(CancellationToken cancellationToken);
}
