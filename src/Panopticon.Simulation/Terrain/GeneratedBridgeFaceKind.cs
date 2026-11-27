#nullable enable

namespace Panopticon.Terrain;

/// <summary>
/// How one level-deck triangle was produced: copied from the authored layout, cut by the trim plane,
/// or added to close the cut.
/// </summary>
internal enum GeneratedBridgeFaceKind
{
    Original,
    Clipped,
    Cap,
}
