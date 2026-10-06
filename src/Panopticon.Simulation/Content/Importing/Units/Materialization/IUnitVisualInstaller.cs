#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>
/// Imports and registers the Track C models and animations a materialization requested.
/// <see cref="Verify"/> runs before publication and must not touch the store.
/// </summary>
public interface IUnitVisualInstaller
{
    IReadOnlyList<ImportDiagnostic> Verify(IReadOnlyList<VisualAssetRequest> requests);

    /// <summary>Registers the requested assets into the store's current top scope.</summary>
    void Install(IReadOnlyList<VisualAssetRequest> requests, AssetStore store);
}
