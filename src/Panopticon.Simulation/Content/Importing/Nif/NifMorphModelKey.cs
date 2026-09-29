#nullable enable

using System;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// A parsed NIF morph model key:
/// <c>&lt;model-key&gt;#morph-kf=&lt;kf-path&gt;</c>, where the KF path is
/// archive-relative and supplies the model's morph target data before
/// materialization.
/// </summary>
internal readonly record struct NifMorphModelKey(string ModelName, string MorphKfPath)
{
    public const string Prefix = "morph-kf=";

    public static bool TryParse(string? key, out NifMorphModelKey result)
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

        result = new NifMorphModelKey(key[..separator], kfPath);
        return true;
    }
}
