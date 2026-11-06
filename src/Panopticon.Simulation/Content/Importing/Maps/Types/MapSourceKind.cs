#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Kind of source a <see cref="MapIdentity"/> was built from.</summary>
public enum MapSourceKind
{
    /// <summary>Legacy single-file map read through the legacy reader seam.</summary>
    LegacyMap,

    /// <summary>Versioned H3 package.</summary>
    H3Package,

    /// <summary>Produced by the B4b-7 generator.</summary>
    Generated,
}
