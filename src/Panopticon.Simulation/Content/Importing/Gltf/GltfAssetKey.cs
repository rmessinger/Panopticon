#nullable enable

using System;
using System.Globalization;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// Normalized `.gltf`/`.glb` model key. Rejects query strings and fragments.
/// </summary>
internal readonly record struct GltfModelKey(string Path);

/// <summary>
/// Animation key: `<model-path>#animation=&lt;zero-based-index&gt;`. No other fragment or
/// query form is accepted.
/// </summary>
internal readonly record struct GltfAnimationKey(string ModelPath, int AnimationIndex);

internal static class GltfAssetKey
{
    private const string AnimationFragmentPrefix = "animation=";

    public static bool TryParseModel(string key, out GltfModelKey modelKey)
    {
        modelKey = default;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Contains('?') || key.Contains('#'))
        {
            return false;
        }

        var normalizedPath = key.Replace('\\', '/');
        if (normalizedPath.Length == 0 || normalizedPath[0] == '/')
        {
            return false;
        }

        if (!normalizedPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) &&
            !normalizedPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        modelKey = new GltfModelKey(normalizedPath);
        return true;
    }

    public static bool TryParseAnimation(string key, out GltfAnimationKey animationKey)
    {
        animationKey = default;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (key.Contains('?'))
        {
            return false;
        }

        var fragmentIndex = key.IndexOf('#');
        if (fragmentIndex < 0)
        {
            return false;
        }

        if (key.IndexOf('#', fragmentIndex + 1) >= 0)
        {
            return false;
        }

        var fragment = key[(fragmentIndex + 1)..];
        if (!fragment.StartsWith(AnimationFragmentPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexText = fragment[AnimationFragmentPrefix.Length..];
        if (indexText.Length == 0 || !uint.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        if (index > int.MaxValue)
        {
            return false;
        }

        if (!TryParseModel(key[..fragmentIndex], out var modelKey))
        {
            return false;
        }

        animationKey = new GltfAnimationKey(modelKey.Path, (int)index);
        return true;
    }
}
