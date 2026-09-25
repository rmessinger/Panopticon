#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Result of converting a NIF scene graph: the bone hierarchy built from
/// the node hierarchy plus the baked sub-objects attached to it. The KF
/// animation importer (WI-C4c-04) reuses the skeleton for rest transforms.
/// </summary>
internal sealed record NifSceneGraph(
    ImportedSkeleton Skeleton,
    IReadOnlyList<ImportedSubObject> SubObjects);
