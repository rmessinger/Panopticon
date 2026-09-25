#nullable enable

using System;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// A parsed NIF animation key: <c>&lt;model-key&gt;#kf=&lt;kf-path&gt;</c>,
/// where the KF path is archive-relative.
/// </summary>
internal readonly record struct NifAnimationKey(string ModelName, string KfPath)
{
    public const string Prefix = "kf=";

    public static bool TryParse(string? key, out NifAnimationKey result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(key) || key.Contains('?'))
        {
            return false;
        }

        var separator = key.IndexOf('#');
        if (separator <= 0 || key.IndexOf('#', separator + 1) >= 0)
        {
            return false;
        }

        var fragment = key[(separator + 1)..];
        if (!fragment.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var kfPath = fragment[Prefix.Length..];
        if (kfPath.Length == 0)
        {
            return false;
        }

        result = new NifAnimationKey(key[..separator], kfPath);
        return true;
    }
}
