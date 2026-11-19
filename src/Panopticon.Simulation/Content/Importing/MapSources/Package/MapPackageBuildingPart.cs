#nullable enable

using Panopticon.Content.Importing.Maps;

namespace Panopticon.Content.Importing.MapSources.Package;

public sealed record MapPackageBuildingPart(string PartStableId, MapPackageVectorGeometry Footprint, int ChunkX, int ChunkY)
{
    public static MapPackageBuildingPart FromModel(BuildingPart part) =>
        new(part.PartStableId, MapPackageVectorGeometry.FromModel(part.Footprint), part.ChunkX, part.ChunkY);

    public BuildingPart ToModel() => new(PartStableId, Footprint.ToModel(), ChunkX, ChunkY);
}
