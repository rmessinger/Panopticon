#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.MapSources.Package;

public sealed record MapPackageBuildingDocument(int Version, IReadOnlyList<MapPackageGeneratedBuilding> Buildings);
