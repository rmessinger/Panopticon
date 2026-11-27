#nullable enable

using System.Numerics;

namespace Panopticon.Terrain;

/// <summary>
/// Render attributes of one level-deck triangle vertex in world space: the normal and the texture
/// coordinates of its mesh part (null when the part has no such set).
/// </summary>
internal readonly record struct GeneratedBridgeVertexAttributes(Vector3 Normal, Vector2? Uv0, Vector2? Uv1)
{
    /// <summary>
    /// Linear interpolation at <paramref name="t"/> from <paramref name="a"/> toward <paramref name="b"/>. The normal
    /// is renormalised; a texture set survives only when both ends carry it.
    /// </summary>
    internal static GeneratedBridgeVertexAttributes Lerp(GeneratedBridgeVertexAttributes a, GeneratedBridgeVertexAttributes b, double t)
    {
        var amount = (float)t;
        return new GeneratedBridgeVertexAttributes(
            Vector3.Normalize(Vector3.Lerp(a.Normal, b.Normal, amount)),
            LerpTexture(a.Uv0, b.Uv0, amount),
            LerpTexture(a.Uv1, b.Uv1, amount));
    }

    private static Vector2? LerpTexture(Vector2? a, Vector2? b, float amount)
    {
        return a is { } start && b is { } end ? Vector2.Lerp(start, end, amount) : null;
    }
}
