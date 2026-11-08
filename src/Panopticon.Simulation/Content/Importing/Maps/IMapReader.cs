#nullable enable

using Panopticon.Content.Importing.MapSources;

namespace Panopticon.Content.Importing.Maps;

/// <summary>
/// Reader-source seam from the H2 map interchange design. Implementations open one map source, verify
/// source digests where the source carries them (the seam hook; legacy sources carry none), and return
/// a validated <see cref="MapInterchange"/>. Failures throw <see cref="MapImportException"/> with typed
/// diagnostics and publish no partial content.
/// </summary>
public interface IMapReader
{
    /// <summary>Reads the source and returns a model with no <see cref="MapInterchangeValidator.Validate"/> diagnostics.</summary>
    MapInterchange Read();
}
