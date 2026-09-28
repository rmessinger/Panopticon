#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Result of converting a NIF scene graph: the bone hierarchy built from
/// the node hierarchy plus the baked sub-objects attached to it. The KF
/// animation importer (WI-C4c-04) reuses the skeleton for rest transforms.
/// CollapsedScaleNodes maps every node name that collapsed because its
/// own rest scale was singular (zero, negative, tiny, or non-finite) to
/// that scale, so the animation converter can report a targeted
/// singular-rest node by name.
/// </summary>
internal sealed record NifSceneGraph(
    ImportedSkeleton Skeleton,
    IReadOnlyList<ImportedSubObject> SubObjects,
    NifPropertyTargetMap PropertyTargets,
    IReadOnlyDictionary<string, float> CollapsedScaleNodes);
