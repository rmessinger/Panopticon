#nullable enable annotations

namespace Panopticon.Recording;

/// <summary>
/// WI-A5-29: record tags for the region-indexed state artifact (v2). A v2 artifact starts with
/// a <see cref="RegionSchemaHeader"/> (tag 0); snapshots become one
/// <see cref="RegionSnapshotRecord"/> (tag 5) per region plus a <see cref="FrameGlobalRecord"/>
/// (tag 4); deltas become one <see cref="RegionDeltaRecord"/> (tag 3) per active region plus a
/// tag-4 global. Whole-frame reconstruction unions the region records of a frame; region queries
/// decode only the requested regions' records.
/// </summary>
public static class RegionIndexedFormat
{
    public const byte SchemaHeaderTag = 0;
    public const byte RegionDeltaTag = 3;
    public const byte FrameGlobalTag = 4;
    public const byte RegionSnapshotTag = 5;

    /// <summary>Bump to invalidate older readers explicitly.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Default region schema: the domain's TopologyKey vocabulary.</summary>
    public const string DefaultSchemaId = "topology-key/1";
}
