#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Resolution policy for source nodata, mirroring the B4b manifest policy names.</summary>
public enum MapNodataPolicy
{
    Fail,

    NearestValid,

    Constant,
}
